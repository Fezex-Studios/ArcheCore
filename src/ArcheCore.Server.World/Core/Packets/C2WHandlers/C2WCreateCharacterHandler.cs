using ArcheCore.Network.Shared;
using ArcheCore.Library.Net.Worldserver;
using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Core.Services;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using MessagePack;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.Networking.C2W
{
    [PacketOpcode(Opcodes.C2WCreateCharacterRequest)]
    public class C2WCreateCharacterHandler : IPacketHandler
    {
        private static readonly Logger Logger =
            LogManager.GetCurrentClassLogger();

        private readonly PlayerManager     _playerManager;
        private readonly PersistenceClient _persistence;
        private readonly SpawnPointService _spawnPoints;

        public C2WCreateCharacterHandler(
            PlayerManager     playerManager,
            PersistenceClient persistence,
            SpawnPointService spawnPoints)
        {
            _playerManager = playerManager;
            _persistence   = persistence;
            _spawnPoints   = spawnPoints;
        }

        public void Handle(NetPeer peer, NetPacketReader reader)
        {
            var request = MessagePackSerializer
                .Deserialize<C2WCreateCharacterRequest>(
                    reader.GetRemainingBytes());

            var accountId = _playerManager.GetPendingAccountId(peer);
            if (accountId is null)
            {
                if (_playerManager.TryGetNetworkId(peer, out _))
                {
                    Logger.Debug("[CreateCharacter] Already in world - request ignored.");
                    return;
                }

                Logger.Warn(
                    "[CreateCharacter] Peer has no pending selection — disconnecting");
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

            // One select/create per connection - a double-clicked Create
            // would otherwise create TWO characters and spawn twice.
            if (!_playerManager.TryBeginSpawn(peer))
            {
                Logger.Debug($"[CreateCharacter] AccountId={accountId} already has a spawn in progress - ignored.");
                return;
            }

            _ = CreateAndSpawn(peer, accountId.Value, name);
        }

        private async Task CreateAndSpawn(
            NetPeer peer,
            int     accountId,
            string  name)
        {
            P2WCreateCharacterResponse response;

            try
            {
                response = await _persistence.W2PCharacterCreate.Send(accountId, name);
            }
            catch (Exception e)
            {
                Logger.Error(e,
                    $"[CreateCharacter] Persistence request failed for AccountId={accountId} — disconnecting");
                _playerManager.EnqueueAction(() => peer.Disconnect());
                return;
            }

            if (!response.Success)
            {
                Logger.Warn(
                    $"[CreateCharacter] Failed for AccountId={accountId}");
                _playerManager.EnqueueAction(() => peer.Disconnect());
                return;
            }

            Logger.Info(
                $"[CreateCharacter] Created '{response.Name}' AccountId={accountId}");

            var spawn = _spawnPoints.GetDefaultSpawn();

            var characterData = new P2WCharacterLoadResponse
            {
                Found       = true,
                AccountId   = accountId,
                CharacterId = response.CharacterId,
                Name        = response.Name,
                Level       = 1,
                X           = spawn.X,
                Y           = spawn.Y,
                Z           = spawn.Z
            };

            // isNewCharacter: true -> the spawn position is saved immediately,
            // since the DB row still holds the placeholder (0, 2, 0).
            _playerManager.EnqueueAction(() =>
                _playerManager.HandlePlayerConnected(
                    peer, accountId, characterData, isNewCharacter: true));
        }
    }
}
