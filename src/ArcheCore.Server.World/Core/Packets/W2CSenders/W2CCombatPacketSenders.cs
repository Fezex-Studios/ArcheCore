using System;
using System.Collections.Generic;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Core.Entities;
using ArcheCore.Server.World.Core.Interaction;
using ArcheCore.Server.World.Managers;
using LiteNetLib;
using Shared;

namespace ArcheCore.Server.World.Networking.W2C
{
    public static class W2CCombatEventPacketSender
    {
        public static void Send(ReplicationManager replication, IEnumerable<NetPeer> peers, W2CCombatEventPacket packet)
        {
            replication.Broadcast(Opcodes.W2CCombatEvent, packet, peers);
        }
    }

    public static class W2CHealthUpdatePacketSender
    {
        public static void Send(NetPeer peer, int health, int maxHealth)
        {
            WorldserverPacketSender.SendPacket(
                peer,
                Opcodes.W2CHealthUpdate,
                new W2CHealthUpdatePacket { Health = health, MaxHealth = maxHealth });
        }
    }

    public static class W2CLootWindowPacketSender
    {
        public static void Send(NetPeer peer, CorpseEntity corpse, Func<int, string> itemName)
        {
            var items = new LootWindowItem[corpse.Items.Count];
            for (int i = 0; i < items.Length; i++)
            {
                var (id, qty) = corpse.Items[i];
                items[i] = new LootWindowItem { ItemTemplateId = id, Quantity = qty, ItemName = itemName(id) };
            }

            WorldserverPacketSender.SendPacket(
                peer,
                Opcodes.W2CLootWindow,
                new W2CLootWindowPacket
                {
                    CorpseNetworkId = corpse.NetworkId,
                    CorpseName      = corpse.Name,
                    Gold            = corpse.Gold,
                    Items           = items
                });
        }
    }

    public static class W2CSpawnCorpsePacketSender
    {
        public static void Send(ReplicationManager replication, NetPeer peer, CorpseEntity corpse)
        {
            replication.Send(
                Opcodes.W2CSpawnCorpse,
                new W2CSpawnCorpsePacket
                {
                    NetworkId     = corpse.NetworkId,
                    Name          = corpse.Name,
                    ModelType     = "Corpse",
                    X             = corpse.Position.X,
                    Y             = corpse.Position.Y,
                    Z             = corpse.Position.Z,
                    InteractRange = corpse.InteractRange,
                    OwnerId       = corpse.OwnerId,
                    OwnerName     = corpse.OwnerName,
                    Actions       = InteractionActionCatalog.ActionsFor(InteractableKind.Lootable, corpse.TemplateId)
                },
                peer);
        }
    }
}
