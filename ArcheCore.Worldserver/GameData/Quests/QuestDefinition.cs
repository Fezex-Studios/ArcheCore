using System.ComponentModel.DataAnnotations.Schema;

namespace ArcheCore.Worldserver.GameData.Quests;

[Table("Quests")]
public class QuestTable
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}