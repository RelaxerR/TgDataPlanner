using TgDataPlanner.Common;

namespace TgDataPlanner.Data.Entities;

public class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long TelegramChatId { get; set; } // ID чата, куда бот добавлен
    
    public GroupState State { get; set; } = GroupState.Idle;
    
    // Для логики переключения групп
    public int SelectionOrder { get; set; } 
    public DateTime? LastGameDate { get; set; }

    public List<Player> Players { get; set; } = new();
}
