using ArcheCore.Network.Shared;
using ArcheCore.Library.Net.Worldserver;
using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using MessagePack;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.Networking.C2W
{
    [PacketOpcode(Opcodes.C2WSelectCharacter)]
    public class C2WSelectCharacterHandler : IPacketHandler
    {
        private static readonly Logger Logger =
            LogManager.GetCurrentClassLogger();

        private readonly PlayerManager     _playerManager;
        private readonly PersistenceClient _persistence;

        public C2WSelectCharacterHandler(
            PlayerManager     playerManager,
            PersistenceClient persistence)
        {
            _playerManager = playerManager;
            _persistence   = persistence;
        }

        public void Handle(NetPeer peer, NetPacketReader reader)
        {
            var request = MessagePackSerializer
                .Deserialize<C2WSelectCharacterRequest>(
                    reader.GetRemainingBytes());

            var accountId = _playerManager.GetPendingAccountId(peer);
            if (accountId is null)
            {
                // Either never authenticated, or already in world. A player who
                // is already in world re-sending select is harmless - ignore it.
                if (_playerManager.TryGetNetworkId(peer, out _))
                {
                    Logger.Debug("[SelectCharacter] Already in world - request ignored.");
                    return;
                }

                Logger.Warn("[SelectCharacter] Peer has no pending selection — disconnecting");
                peer.Disconnect();
                return;
            }

            if (request.CharacterId <= 0)
            {
                Logger.Warn(
                    $"[SelectCharacter] Invalid CharacterId={request.CharacterId} — disconnecting");
                peer.Disconnect();
                return;
            }

            // One select/create per connection. A double-clicked "Enter World"
            // used to spawn the same peer twice and then kick it as a
            // "duplicate login" of itself.
            if (!_playerManager.TryBeginSpawn(peer))
            {
                Logger.Debug($"[SelectCharacter] AccountId={accountId} already has a spawn in progress - ignored.");
                return;
            }

            _ = LoadAndSpawn(peer, accountId.Value, request.CharacterId);
        }

        private async Task LoadAndSpawn(
            NetPeer peer,
            int     accountId,
            long    characterId)
        {
            P2WCharacterLoadResponse character;

            try
            {
                character = await _persistence.W2PCharacterLoad.Send(accountId, characterId);
            }
            catch (Exception e)
            {
                Logger.Error(e,
                    $"[SelectCharacter] Persistence request failed for AccountId={accountId}, CharacterId={characterId} — disconnecting");
                _playerManager.EnqueueAction(() => peer.Disconnect());
                return;
            }

            if (!character.Found)
            {
                // The id doesn't exist, or belongs to another account (the
                // persistence query filters on both).
                Logger.Warn(
                    $"[SelectCharacter] CharacterId={characterId} not found/owned by AccountId={accountId}");
                _playerManager.EnqueueAction(() => peer.Disconnect());
                return;
            }

            Logger.Info(
                $"[SelectCharacter] AccountId={accountId} selected '{character.Name}' (CharacterId={characterId})");

            // HandlePlayerConnected re-checks that the peer is still connected
            // and not already spawned before doing anything.
            _playerManager.EnqueueAction(() =>
                _playerManager.HandlePlayerConnected(
                    peer, accountId, character));
        }
    }
}
