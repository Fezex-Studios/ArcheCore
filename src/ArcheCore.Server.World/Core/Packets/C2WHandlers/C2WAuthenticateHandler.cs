using ArcheCore.Network.Shared;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Core.Services.Authservice;
using LiteNetLib;
using MessagePack;
using NLog;
using Shared.AuthService;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.Networking.C2W
{
    [PacketOpcode(Opcodes.Authenticate)]
public class C2WAuthenticateHandler : IPacketHandler
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly PlayerManager playerManager;
        private readonly AuthService authService;
        private readonly PersistenceClient persistence;

        // One instance per handler, and the dispatcher builds handlers once
        // at registration, so this is effectively per-server state.
        private readonly AuthThrottle throttle = new();

        public C2WAuthenticateHandler(PlayerManager playerManager, AuthService authService, PersistenceClient persistence)
        {
            this.playerManager = playerManager;
            this.authService = authService;
            this.persistence = persistence;
        }

        public void Handle(
            NetPeer peer,
            NetPacketReader reader)
        {
            C2WAuthenticateRequest request =
                MessagePackSerializer
                    .Deserialize<C2WAuthenticateRequest>(
                        reader.GetRemainingBytes());

            // The token is a bearer credential: whoever holds it IS that
            // account until it expires. It never goes in a log, at any
            // level. Logs get tailed, shipped, pasted into chat and
            // attached to bug reports, and every one of those is a session
            // handover. Log the peer instead - that's the part you
            // actually need when reading this back.
            Logger.Debug($"[Auth] Authenticate received from {peer.Address}");

            // Already in world? Re-authenticating a live session is not a
            // flow that should exist.
            if (playerManager.TryGetNetworkId(peer, out _))
            {
                Logger.Debug($"[Auth] {peer.Address} is already in world - ignored.");
                return;
            }

            switch (throttle.TryBegin(peer))
            {
                case AuthThrottle.Decision.AlreadyInFlight:
                    // Silent: an honest client on a laggy link can
                    // legitimately resend before the first one lands.
                    return;

                case AuthThrottle.Decision.TooManyAttempts:
                    Logger.Warn(
                        $"[Auth] {peer.Address} exceeded the authentication attempt limit - disconnecting.");
                    peer.Disconnect();
                    return;
            }

            _ = ValidateAndConnect(peer, request.Token);
        }

        private async Task ValidateAndConnect(NetPeer peer, string token)
        {
            try
            {
                await ValidateAndConnectCore(peer, token);
            }
            finally
            {
                // However this ended - success, rejection, or a thrown
                // exception - the in-flight slot has to come back, or the
                // peer is locked out of retrying for the rest of its
                // connection. Enqueued because throttle is tick-thread state
                // and this continuation is not on the tick thread.
                playerManager.EnqueueAction(() => throttle.Complete(peer));
            }
        }

        private async Task ValidateAndConnectCore(NetPeer peer, string token)
        {
            int accountId = await authService.ValidateToken(token);

            if (accountId == -1)
            {
                Logger.Warn(
                    $"[C2WAuthenticateHandler] Invalid or expired token — disconnecting peer {peer.Address}");

                playerManager.EnqueueAction(() =>
                    peer.Disconnect());

                return;
            }

            Logger.Info(
                $"[C2WAuthenticateHandler] Token valid. AccountId={accountId} — fetching character list.");

            // No more auto-load-and-spawn. We always fetch the roster and
            // let the client decide: empty list -> create, 1+ -> select.
            P2WCharacterListResponse list;

            try
            {
                list = await persistence.W2PCharacterList.Send(accountId);
            }
            catch (Exception e)
            {
                // Under the old TCP client this call could only hang forever
                // on failure — under HTTP it throws instead, so this is the
                // difference between a player silently stuck and one who
                // gets disconnected with a logged reason.
                Logger.Error(e,
                    $"[C2WAuthenticateHandler] Failed to fetch character list for AccountId={accountId} — disconnecting peer {peer.Address}");

                playerManager.EnqueueAction(() =>
                    peer.Disconnect());

                return;
            }

            Logger.Info(
                $"[C2WAuthenticateHandler] AccountId={accountId} has {list.Characters?.Length ?? 0} character(s).");

            playerManager.EnqueueAction(() =>
            {
                // Peer is authenticated but hasn't entered world yet —
                // tracked the same way for both the create-flow and the
                // select-flow, since both need to know which account this
                // peer belongs to before they're allowed to spawn.
                playerManager.TrackPendingSelection(peer, accountId);

                WorldserverPacketSender.SendPacket(
                    peer,
                    Opcodes.W2CCharacterList,
                    new W2CCharacterListPacket
                    {
                        Characters = list.Characters ?? System.Array.Empty<Network.Shared.Packets.PersistenceServer.P2W.CharacterSummary>()
                    });
            });
        }
    }
}