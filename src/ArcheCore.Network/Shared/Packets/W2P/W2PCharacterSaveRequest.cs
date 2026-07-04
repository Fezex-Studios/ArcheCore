using MessagePack;


namespace ArcheCore.Network.Shared.Packets.PersistenceServer.W2P
{
    [MessagePackObject(true)]
    public class W2PCharacterSaveRequest
    {
        public long CharacterId;

        public int AccountId;

        public string Name;
    
        public int Level;
    
        public float X;
    
        public float Y;
    
        public float Z;
    }
}