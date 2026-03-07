using DefaultNamespace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgDataPlanner.Data;
using TgDataPlanner.Data.Entities;

namespace TgDataPlanner.Services;

/// <summary>
/// Сервис для анализа расписаний игроков и поиска общих свободных временных окон.
/// Используется для планирования игровых сессий на основе доступности участников группы.
/// </summary>
public class SchedulingService
{
    private readonly AppDbContext _db;
    private readonly ILogger<SchedulingService> _logger;

    /// <summary>
    /// Конфигурационные константы сервиса.
    /// </summary>
    private static class Config
    {
        public const int MinPlayersForLogging = 1;      // Минимальное количество игроков для детального логирования
        public const int MaxSamplePointsToLog = 5;      // Максимальное количество примеров точек времени для логирования
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SchedulingService"/>.
    /// </summary>
    /// <param name="db">Контекст базы данных для доступа к сущностям.</param>
    /// <param name="logger">Логгер для записи событий сервиса.</param>
    public SchedulingService(
        AppDbContext db,
        ILogger<SchedulingService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Находит непрерывные временные интервалы, когда все игроки группы свободны.
    /// </summary>
    /// <param name="groupId">Идентификатор группы для анализа.</param>
    /// <param name="minHours">Минимальная длительность интервала в часах.</param>
    /// <returns>Список найденных интервалов, удовлетворяющих критерию длительности.</returns>
    public async Task<List<DateTimeRange>> FindIntersectionsAsync(int groupId, int minHours)
    {
        _logger.LogInformation(
            "Запуск поиска пересечений: Группа={GroupId}, Мин.длительность={MinHours}ч",
            groupId, minHours);

        var group = await LoadGroupWithPlayersAndSlotsAsync(groupId);
        if (group is null)
        {
            _logger.LogWarning("Группа с ID={GroupId} не найдена в базе данных", groupId);
            return [];
        }

        LogGroupStatistics(group);

        var allPlayerSlots = ExtractAllPlayerSlots(group);
        var commonTimePoints = FindCommonTimePoints(allPlayerSlots, group.Players.Count);

        if (commonTimePoints.Count != 0)
        {
            LogSampleTimePoints(commonTimePoints);
        }

        var result = BuildContinuousIntervals(commonTimePoints, minHours);

        _logger.LogInformation(
            "Поиск завершён: найдено {Count} интервалов длительностью ≥{MinHours}ч",
            result.Count, minHours);

        return result;
    }

    /// <summary>
    /// Загружает группу с связанными игроками и их слотами доступности.
    /// </summary>
    /// <param name="groupId">Идентификатор группы.</param>
    /// <returns>Объект группы или null, если не найдена.</returns>
    private async Task<Group?> LoadGroupWithPlayersAndSlotsAsync(int groupId) =>
        await _db.Groups
            .AsNoTracking()
            .Include(g => g.Players)
                .ThenInclude(p => p.Slots)
            .FirstOrDefaultAsync(g => g.Id == groupId);

    /// <summary>
    /// Логирует статистику по группе и её участникам.
    /// </summary>
    /// <param name="group">Объект группы для анализа.</param>
    private void LogGroupStatistics(Group group)
    {
        _logger.LogInformation(
            "Группа '{GroupName}': {PlayerCount} игроков",
            group.Name, group.Players.Count);

        if (group.Players.Count < Config.MinPlayersForLogging)
            return;

        foreach (var player in group.Players)
        {
            _logger.LogDebug(
                "Игрок '{Username}' (ID={TelegramId}): {SlotCount} слотов доступности",
                player.Username, player.TelegramId, player.Slots?.Count ?? 0);
        }
    }

    /// <summary>
    /// Извлекает плоский список всех слотов игроков с привязкой к их идентификаторам.
    /// </summary>
    /// <param name="group">Группа для анализа.</param>
    /// <returns>Список анонимных объектов с playerId и временем слота.</returns>
    private static List<(long PlayerId, DateTime TimeUtc)> ExtractAllPlayerSlots(Group group) =>
        group.Players
            .Where(p => true)
            .SelectMany(player => player.Slots!.Select(slot => (player.TelegramId, slot.DateTimeUtc)))
            .ToList();

    /// <summary>
    /// Находит временные точки, когда свободны все игроки группы.
    /// </summary>
    /// <param name="allSlots">Список всех слотов игроков.</param>
    /// <param name="totalPlayersCount">Общее количество игроков в группе.</param>
    /// <returns>Отсортированный список общих временных точек в UTC.</returns>
    private static List<DateTime> FindCommonTimePoints(
        List<(long PlayerId, DateTime TimeUtc)> allSlots,
        int totalPlayersCount)
    {
        if (!allSlots.Any())
            return [];

        return allSlots
            .GroupBy(slot => slot.TimeUtc)
            .Where(group => group.Select(s => s.PlayerId).Distinct().Count() == totalPlayersCount)
            .Select(group => group.Key)
            .OrderBy(time => time)
            .ToList();
    }

    /// <summary>
    /// Логирует примеры найденных общих временных точек для отладки.
    /// </summary>
    /// <param name="commonPoints">Список общих временных точек.</param>
    private void LogSampleTimePoints(List<DateTime> commonPoints)
    {
        _logger.LogInformation(
            "Найдено {Count} общих временных точек (все игроки свободны)",
            commonPoints.Count);

        var sample = commonPoints.Take(Config.MaxSamplePointsToLog);
        foreach (var point in sample)
        {
            _logger.LogDebug("Пример общей точки (UTC): {TimeUtc}", point);
        }
    }

    /// <summary>
    /// Формирует непрерывные интервалы из отсортированных временных точек.
    /// </summary>
    /// <param name="sortedTimePoints">Отсортированный список временных точек в UTC.</param>
    /// <param name="minDurationHours">Минимальная длительность интервала в часах.</param>
    /// <returns>Список интервалов, удовлетворяющих критерию длительности.</returns>
    private List<DateTimeRange> BuildContinuousIntervals(
        List<DateTime> sortedTimePoints,
        int minDurationHours)
    {
        if (sortedTimePoints.Count == 0)
            return [];

        var intervals = new List<DateTimeRange>();
        var intervalStart = sortedTimePoints[0];
        var previousTime = sortedTimePoints[0];

        for (var i = 1; i <= sortedTimePoints.Count; i++)
        {
            var isLastPoint = (i == sortedTimePoints.Count);
            var currentTime = isLastPoint ? default : sortedTimePoints[i];

            var isSequenceBroken = isLastPoint || !IsConsecutiveHour(previousTime, currentTime);

            if (isSequenceBroken)
            {
                var intervalEnd = previousTime.AddHours(1);
                var duration = (intervalEnd - intervalStart).TotalHours;

                if (duration >= minDurationHours)
                {
                    intervals.Add(new DateTimeRange(intervalStart, intervalEnd));
                    _logger.LogDebug(
                        "Добавлен интервал: {Start} — {End} (длительность: {Hours}ч)",
                        intervalStart, intervalEnd, duration);
                }

                if (!isLastPoint)
                {
                    intervalStart = currentTime;
                }
            }

            if (!isLastPoint)
            {
                previousTime = currentTime;
            }
        }

        return intervals;
    }

    /// <summary>
    /// Проверяет, являются ли две временные точки последовательными часами.
    /// </summary>
    /// <param name="first">Первая временная точка.</param>
    /// <param name="second">Вторая временная точка.</param>
    /// <returns>True, если вторая точка ровно на час позже первой.</returns>
    private static bool IsConsecutiveHour(DateTime first, DateTime second) =>
        second == first.AddHours(1);
}

/// <summary>
/// Представляет временной интервал с началом и концом в формате UTC.
/// Используется для описания найденных окон доступности.
/// </summary>
/// <param name="Start">Начало интервала (включительно).</param>
/// <param name="End">Конец интервала (исключительно).</param>
public record DateTimeRange(DateTime Start, DateTime End)
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DateTimeRange"/>.
    /// </summary>
    public DateTimeRange() : this(default, default) { }

    /// <summary>
    /// Вычисляет длительность интервала в часах.
    /// </summary>
    /// <returns>Длительность в часах как число с плавающей точкой.</returns>
    public double GetDurationHours() => (End - Start).TotalHours;

    /// <summary>
    /// Возвращает человекочитаемое представление интервала.
    /// </summary>
    /// <returns>Строка формата "dd.MM HH:mm — HH:mm".</returns>
    public override string ToString() =>
        $"{Start:dd.MM HH:mm} — {End:HH:mm}";
}