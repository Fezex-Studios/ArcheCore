using MessagePack;

[MessagePackObject(true)]
public class P2WCharacterLoadResponse
{
    public bool Found;
    public long CharacterId;

    public string Name;

    public int Level;

    public float X;

    public float Y;

    public float Z;
}