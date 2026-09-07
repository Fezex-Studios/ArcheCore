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

            Logger.Warn($"Auth Token: {request.Token}");

            _ = ValidateAndConnect(peer, request.Token);
        }

        private async Task ValidateAndConnect(NetPeer peer, string token)
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
            P2WCharacterListResponse list =
                await persistence.W2PCharacter.LoadList(accountId);

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