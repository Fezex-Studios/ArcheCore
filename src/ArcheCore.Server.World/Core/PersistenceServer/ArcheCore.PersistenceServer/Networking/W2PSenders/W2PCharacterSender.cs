using System.Collections.Concurrent;
using System.Threading.Tasks;
using ArcheCore.Network.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Senders
{
    public class W2PCharacterSender
    {
        private readonly PersistenceClient _client;

        public W2PCharacterSender(PersistenceClient client)
            => _client = client;

        public async Task<P2WCharacterLoadResponse> Load(int accountId)
        {
            var tcs = new TaskCompletionSource<P2WCharacterLoadResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _client.pendingLoads[accountId] = tcs;

            await _client.Send(
                PServerOpcodes.CharacterLoad,
                new W2PCharacterLoadRequest { AccountId = accountId });

            return await tcs.Task;
        }

        public async Task<P2WCreateCharacterResponse> Create(int accountId, string name)
        {
            var tcs = new TaskCompletionSource<P2WCreateCharacterResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _client.pendingCreates[accountId] = tcs;

            await _client.Send(
                PServerOpcodes.CharacterCreate,
                new W2PCreateCharacterRequest
                {
                    AccountId = accountId,
                    Name      = name
                });

            return await tcs.Task;
        }

        public async Task Save(
            long   characterId,
            int    accountId,
            string name,
            int    level,
            float  x,
            float  y,
            float  z)
        {
            await _client.Send(
                PServerOpcodes.CharacterSave,
                new W2PCharacterSaveRequest
                {
                    CharacterId = characterId,
                    AccountId   = accountId,
                    Name        = name,
                    Level       = level,
                    X           = x,
                    Y           = y,
                    Z           = z
                });
        }
    }
}