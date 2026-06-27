
using ArcheCore.Net.Shared.Packets.C2W;
using ArcheCore.Net.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Net.Worldserver;
using ArcheCore.Worldserver.Core.Services.Authservice;
using ArcheCore.WorldServer.Managers;
using LiteNetLib;
using MessagePack;
using NLog;
using Shared.AuthService;
using Worldserver.ArcheCore.PersistenceServer.Scripts;


namespace ArcheCore.WorldServer.Networking.C2W
{
    public class C2WAuthenticateHandler : IPacketHandler
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly PlayerManager playerManager;
        private readonly AuthService authService; 


        public C2WAuthenticateHandler(PlayerManager playerManager, AuthService authService)
        {
            this.playerManager = playerManager;
            this.authService = authService;  // was assigning null before
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

            var persistence = PersistenceClient.Instance;

            if (persistence == null)
            {
                Logger.Error("[C2WAuthenticateHandler] PersistenceClient.Instance is null — cannot load character");
                playerManager.EnqueueAction(() => peer.Disconnect());
                return;
            }

            P2WCharacterLoadResponse p2WCharacter =
                await persistence.W2PCharacter.Load(accountId);

            if (!p2WCharacter.Found)
            {
                Logger.Warn(
                    $"[C2WAuthenticateHandler] No character found for AccountId={accountId} — disconnecting peer.");

                // TODO: once character creation is built, redirect to char create screen instead
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