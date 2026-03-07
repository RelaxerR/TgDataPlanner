namespace TgDataPlanner.Data.Entities;

public class AvailabilitySlot
{
    public int Id { get; set; }
    public long PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    // Всегда храним в UTC
    public DateTime DateTimeUtc { get; set; }
    
    // Флаг для повторяющегося расписания
    public bool IsWeeklyPermanent { get; set; } 
}
