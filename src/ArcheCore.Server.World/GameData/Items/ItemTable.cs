
using System.ComponentModel.DataAnnotations;

namespace ArcheCore.Server.World.GameData.Items;

public class ItemTable
{
    [Key]
    public int item_id { get; set; }
    public string name { get; set; }
    public string description { get; set; }
    public string icon_name { get; set; }
}