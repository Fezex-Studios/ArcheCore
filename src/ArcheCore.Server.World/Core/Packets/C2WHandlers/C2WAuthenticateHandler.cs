
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
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
    public class C2WAuthenticateHandler : IPacketHandler
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly PlayerManager playerManager;
        private readonly AuthService authService; 
        private readonly PersistenceClient persistence;


        public C2WAuthenticateHandler(PlayerManager playerManager, AuthService authService, PersistenceClient persistence)
        {
            this.playerManager = playerManager;
            this.authService = authService;  // was assigning null before
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
                $"[C2WAuthenticateHandler] Token valid. AccountId={accountId} — loading character.");

            var p2WCharacter = await persistence.W2PCharacter.Load(accountId);

            if (!p2WCharacter.Found)
            {
                Logger.Warn(
                    $"[C2WAuthenticateHandler] No character found for AccountId={accountId} — disconnecting peer.");

                playerManager.EnqueueAction(() => peer.Disconnect());
                return;
            }

            Logger.Info(
                $"[C2WAuthenticateHandler] Character loaded: {p2WCharacter.Name} — spawning.");

            playerManager.EnqueueAction(() =>
                playerManager.HandlePlayerConnected(peer, accountId, p2WCharacter));
        }
    }
}