using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// World -> Client: "you are not where you said you were; go here."
    ///
    /// Sent only by MovementValidator, only after repeated violations, and
    /// only to the offending client. It is the other half of server-side
    /// validation: rejecting a movement packet without telling the client
    /// leaves it walking confidently through a world where the server has
    /// it standing still, and every other player watching a character
    /// frozen at the last accepted position. The divergence grows until
    /// something else resyncs it.
    ///
    /// RELIABLE, unlike everything else on the movement path. A dropped
    /// snapshot costs one frame of smoothness; a dropped correction leaves
    /// the client desynced indefinitely, which is exactly the state the
    /// correction exists to end.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CPositionCorrectionPacket
    {
        public float x;
        public float y;
        public float z;
    }
}