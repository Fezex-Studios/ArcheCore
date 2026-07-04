using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using MessagePack;
using NLog;
using Shared;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.Networking.C2W
{
    public class C2WCreateCharacterHandler : IPacketHandler
    {
        private static readonly Logger Logger =
            LogManager.GetCurrentClassLogger();

        private readonly PlayerManager    _playerManager;
        private readonly PersistenceClient _persistence;

        public C2WCreateCharacterHandler(
            PlayerManager     playerManager,
            PersistenceClient persistence)
        {
            _playerManager = playerManager;
            _persistence   = persistence;
        }

        public void Handle(NetPeer peer, NetPacketReader reader)
        {
            var request = MessagePackSerializer
                .Deserialize<C2WCreateCharacterRequest>(
                    reader.GetRemainingBytes());

            if (!_playerManager.TryGetPendingAccountId(peer, out int accountId))
            {
                Logger.Warn(
                    "[CreateCharacter] Peer has no pending creation — disconnecting");
                peer.Disconnect();
                return;
            }

            string name = request.Name?.Trim() ?? string.Empty;

            if (name.Length < 2 || name.Length > 20)
            {
                Logger.Warn(
                    $"[CreateCharacter] Invalid name '{name}' — disconnecting");
                peer.Disconnect();
                return;
            }

            _playerManager.ClearPendingCreation(peer);

            _ = CreateAndSpawn(peer, accountId, name);
        }

        private async Task CreateAndSpawn(
            NetPeer peer,
            int     accountId,
            string  name)
        {
            P2WCreateCharacterResponse response =
                await _persistence.W2PCharacter.Create(accountId, name);

            if (!response.Success)
            {
                Logger.Warn(
                    $"[CreateCharacter] Failed for AccountId={accountId}");
                _playerManager.EnqueueAction(() => peer.Disconnect());
                return;
            }

            Logger.Info(
                $"[CreateCharacter] Created '{response.Name}' " +
                $"AccountId={accountId}");

            var characterData = new P2WCharacterLoadResponse
            {
                Found       = true,
                AccountId   = accountId,
                CharacterId = response.CharacterId,
                Name        = response.Name,
                Level       = 1,
                X           = 0,
                Y           = 2,
                Z           = 0
            };

            _playerManager.EnqueueAction(() =>
                _playerManager.HandlePlayerConnected(
                    peer, accountId, characterData));
        }
    }
}