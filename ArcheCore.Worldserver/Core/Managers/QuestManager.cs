using ArcheCore.Worldserver.GameData.Quests;

namespace ArcheCore.Worldserver.Core.Managers;

public class QuestManager
{
    private List<QuestTable> _quests = new();

    public IReadOnlyList<QuestTable> Quests => _quests;

    public void Load(List<QuestTable> quests)
    {
        _quests = quests;
    }

    public QuestTable? Get(int id)
    {
        return _quests.FirstOrDefault(q => q.Id == id);
    }
}