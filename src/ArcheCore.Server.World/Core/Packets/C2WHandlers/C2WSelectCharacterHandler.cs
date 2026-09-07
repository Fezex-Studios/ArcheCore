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

        private readonly PlayerManager    _playerManager;
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

            _ = LoadAndSpawn(peer, accountId.Value, request.CharacterId);
        }

        private async Task LoadAndSpawn(
            NetPeer peer,
            int     accountId,
            long    characterId)
        {
            P2WCharacterLoadResponse character =
                await _persistence.W2PCharacter.Load(accountId, characterId);

            if (!character.Found)
            {
                // Either the id doesn't exist, or it belongs to a different
                // account — the persistence query filters on both, so this
                // covers a tampered client trying to load someone else's
                // character.
                Logger.Warn(
                    $"[SelectCharacter] CharacterId={characterId} not found/owned by AccountId={accountId}");
                _playerManager.EnqueueAction(() => peer.Disconnect());
                return;
            }

            Logger.Info(
                $"[SelectCharacter] AccountId={accountId} selected '{character.Name}' (CharacterId={characterId})");

            // No explicit "clear pending" step needed anymore - the peer
            // stops being pending the moment SpawnPlayer assigns it a
            // NetworkId inside HandlePlayerConnected below.
            _playerManager.EnqueueAction(() =>
                _playerManager.HandlePlayerConnected(
                    peer, accountId, character));
        }
    }
}