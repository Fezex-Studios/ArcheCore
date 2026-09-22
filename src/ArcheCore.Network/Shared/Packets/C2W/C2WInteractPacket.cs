using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    [MessagePackObject(true)]
    public class C2WInteractPacket
    {
        public int TargetNetworkId;

        /// <summary>
        /// Which of the target's actions to perform (InteractionActionType).
        /// 0 = the target's first (F) action - what every older client sends,
        /// so a client that predates actions still works.
        /// </summary>
        public int ActionType;
    }
}