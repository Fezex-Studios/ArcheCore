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
    public static class W2CSpawnHarvestNodePacketSender
    {
        public static void Send(ReplicationManager replication, NetPeer peer, HarvestNodeEntity node)
        {
            replication.Send(
                Opcodes.W2CSpawnHarvestNode,
                new W2CSpawnHarvestNodePacket
                {
                    NetworkId     = node.NetworkId,
                    TemplateId    = node.TemplateId,
                    Name          = node.Template.Name,
                    ModelType     = node.Template.ModelType,
                    X             = node.Position.X,
                    Y             = node.Position.Y,
                    Z             = node.Position.Z,
                    Yaw           = node.Yaw,
                    InteractRange = node.InteractRange,
                    IsDepleted    = node.IsDepleted,
                    Actions       = InteractionActionCatalog.ActionsFor(InteractableKind.HarvestNode, node.TemplateId)
                },
                peer);
        }
    }

    public static class W2CHarvestNodeStatePacketSender
    {
        public static void Send(ReplicationManager replication, IEnumerable<NetPeer> peers, int networkId, bool isDepleted)
        {
            replication.Broadcast(
                Opcodes.W2CHarvestNodeState,
                new W2CHarvestNodeStatePacket { NetworkId = networkId, IsDepleted = isDepleted },
                peers);
        }
    }

    public static class W2CHarvestStartedPacketSender
    {
        public static void Send(NetPeer peer, int nodeNetworkId, string nodeName, int durationMs)
        {
            WorldserverPacketSender.SendPacket(
                peer,
                Opcodes.W2CHarvestStarted,
                new W2CHarvestStartedPacket
                {
                    NodeNetworkId = nodeNetworkId,
                    NodeName      = nodeName,
                    DurationMs    = durationMs
                });
        }
    }

    public static class W2CHarvestCompletedPacketSender
    {
        public static void Send(NetPeer peer, int nodeNetworkId, int itemTemplateId, int quantity, string itemName)
        {
            WorldserverPacketSender.SendPacket(
                peer,
                Opcodes.W2CHarvestCompleted,
                new W2CHarvestCompletedPacket
                {
                    NodeNetworkId  = nodeNetworkId,
                    ItemTemplateId = itemTemplateId,
                    Quantity       = quantity,
                    ItemName       = itemName
                });
        }
    }

    public static class W2CHarvestCancelledPacketSender
    {
        public static void Send(NetPeer peer, int nodeNetworkId, string reason)
        {
            WorldserverPacketSender.SendPacket(
                peer,
                Opcodes.W2CHarvestCancelled,
                new W2CHarvestCancelledPacket { NodeNetworkId = nodeNetworkId, Reason = reason });
        }
    }
}
