using System.Threading.Tasks;
using ArcheCore.WorldServer.Managers;
using LiteNetLib;
using MessagePack;
using MMO.Shared.Packets;
using Shared.AuthService;
using UnityEngine;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.WorldServer.Networking.C2W
{
    public class C2WAuthenticateHandler : IPacketHandler
    {
        private readonly PlayerManager playerManager;

        public C2WAuthenticateHandler(PlayerManager playerManager)
        {
            this.playerManager = playerManager;
        }

        public void Handle(
            NetPeer peer,
            NetPacketReader reader)
        {
            C2WAuthenticateRequest request =
                MessagePackSerializer
                    .Deserialize<C2WAuthenticateRequest>(
                        reader.GetRemainingBytes());

            Debug.Log($"Auth Token: {request.Token}");

            _ = ValidateAndConnect(peer, request.Token);
        }

        private async Task ValidateAndConnect(NetPeer peer, string token)
        {
            int accountId = await AuthService.ValidateToken(token);

            if (accountId == -1)
            {
                Debug.LogWarning(
                    $"[C2WAuthenticateHandler] Invalid or expired token — disconnecting peer {peer.Address}");

                playerManager.EnqueueAction(() =>
                    peer.Disconnect());

                return;
            }

            Debug.Log(
                $"[C2WAuthenticateHandler] Token valid. AccountId={accountId} — loading character.");

            var persistence = PersistenceClient.Instance;

            if (persistence == null)
            {
                Debug.LogError("[C2WAuthenticateHandler] PersistenceClient.Instance is null — cannot load character");
                playerManager.EnqueueAction(() => peer.Disconnect());
                return;
            }

            P2WCharacterLoadResponse p2WCharacter =
                await persistence.W2PCharacter.Load(accountId);

            if (!p2WCharacter.Found)
            {
                Debug.LogWarning(
                    $"[C2WAuthenticateHandler] No character found for AccountId={accountId} — disconnecting peer.");

                // TODO: once character creation is built, redirect to char create screen instead
                playerManager.EnqueueAction(() => peer.Disconnect());
                return;
            }

            Debug.Log(
                $"[C2WAuthenticateHandler] Character loaded: {p2WCharacter.Name} — spawning.");

            playerManager.EnqueueAction(() =>
                playerManager.HandlePlayerConnected(peer, accountId, p2WCharacter));
        }
    }
}