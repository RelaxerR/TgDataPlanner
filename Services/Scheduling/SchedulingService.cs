using DefaultNamespace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgDataPlanner.Telegram.Handlers;

namespace TgDataPlanner.Services.Scheduling;

public class SchedulingService(
    AppDbContext db,
    ILogger<CommandHandler> logger)
{
    public async Task<List<DateTimeRange>> FindIntersections(int groupId, int minHours)
    {
        logger.LogInformation("--- Запуск поиска пересечений для группы ID: {GroupId} ---", groupId);

        var group = await db.Groups
            .Include(g => g.Players)
            .ThenInclude(p => p.Slots)
            .FirstOrDefaultAsync(g => g.Id == groupId);

        if (group == null) {
            logger.LogWarning("Группа {GroupId} не найдена в БД.", groupId);
            return new();
        }

        logger.LogInformation("Группа: {Name}. Игроков в группе: {Count}", group.Name, group.Players.Count);

        // Логируем наличие слотов у каждого игрока
        foreach (var p in group.Players) {
            logger.LogInformation("Игрок {Name} (ID: {Id}): {SlotCount} слотов.", p.Username, p.TelegramId, p.Slots.Count);
        }

        var allSlots = group.Players
            .SelectMany(p => p.Slots.Select(s => new { p.TelegramId, s.DateTimeUtc }))
            .ToList();

        // Логируем группировку: сколько игроков свободны в конкретные моменты времени
        var groupedByTime = allSlots
            .GroupBy(s => s.DateTimeUtc)
            .Select(g => new { 
                Time = g.Key, 
                Count = g.Select(x => x.TelegramId).Distinct().Count() 
            })
            .ToList();

        logger.LogInformation("Всего уникальных временных точек у игроков: {Count}", groupedByTime.Count);

        // Ищем только те точки, гдеCount равен количеству игроков в группе
        var commonSlots = groupedByTime
            .Where(g => g.Count == group.Players.Count)
            .Select(g => g.Time)
            .OrderBy(t => t)
            .ToList();

        logger.LogInformation("Найдено общих точек (где свободны ВСЕ): {Count}", commonSlots.Count);
        
        if (commonSlots.Any()) {
            logger.LogInformation("Пример общей точки (UTC): {FirstPoint}", commonSlots.First());
        }

        var result = BuildIntervals(commonSlots, minHours);
        logger.LogInformation("Итого сформировано интервалов длины >= {MinHours}ч: {ResultCount}", minHours, result.Count);
        
        return result;
    }

    private List<DateTimeRange> BuildIntervals(List<DateTime> slots, int minHours)
    {
        var intervals = new List<DateTimeRange>();
        if (!slots.Any()) return intervals;

        var start = slots[0];
        var current = slots[0];

        for (int i = 1; i <= slots.Count; i++)
        {
            // Если это последний слот или следующий слот не идет через 1 час
            if (i == slots.Count || slots[i] != current.AddHours(1))
            {
                var duration = (current.AddHours(1) - start).TotalHours;
                if (duration >= minHours)
                {
                    intervals.Add(new DateTimeRange { Start = start, End = current.AddHours(1) });
                }
                if (i < slots.Count) start = slots[i];
            }
            if (i < slots.Count) current = slots[i];
        }
        return intervals;
    }
}

public class DateTimeRange
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}
