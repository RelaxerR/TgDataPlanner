using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace TgDataPlanner.Data.Entities;

public class Player
{
    [Key]
    public long TelegramId { get; set; }
    public string Username { get; set; } = string.Empty;
    
    // Смещение в часах от UTC (например, +3)
    public int TimeZoneOffset { get; set; } 

    // Навигационные свойства
    public string? CurrentState { get; set; }
    public List<Group> Groups { get; set; } = new();
    public List<AvailabilitySlot> Slots { get; set; } = new();
}
