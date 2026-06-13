namespace ArcheCore.Net.PersistenceServer
{
    public enum PServerOpcodes : ushort
    {
        W2PConnectRequest = 1,
        CharacterSave = 2,
        CharacterLoad = 3,
        P2WConnectResponse = 4,
    }
}