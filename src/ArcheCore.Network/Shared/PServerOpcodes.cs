namespace ArcheCore.Network.PersistenceServer
{
    public enum PServerOpcodes : ushort
    {
        W2PConnectRequest = 1,
        CharacterSave = 2,
        CharacterLoad = 3,
        P2WConnectResponse = 4,
        HelloWorld =5,
        CharacterCreate = 6,
        P2WCharacterCreateResponse = 7,
    }
}