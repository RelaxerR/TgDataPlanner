using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgDataPlanner.Common;
using TgDataPlanner.Configuration;
using TgDataPlanner.Data;
using TgDataPlanner.Data.Entities;

namespace TgDataPlanner.Services;

/// <summary>
/// Сервис планирования игровых сессий.
/// Управляет процессом поиска свободных окон, авто-планирования и работы с рекомендациями.
/// </summary>
public class SessionPlanningService
{
    private readonly AppDbContext _db;
    private readonly ILogger<SessionPlanningService> _logger;
    private readonly SchedulingService _schedulingService;
    private readonly IRecommendationService _recommendationService;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SessionPlanningService"/>.
    /// </summary>
    /// <param name="db">Контекст базы данных.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    /// <param name="schedulingService">Сервис поиска пересечений.</param>
    /// <param name="recommendationService">Сервис рекомендаций.</param>
    public SessionPlanningService(
        AppDbContext db,
        ILogger<SessionPlanningService> logger,
        SchedulingService schedulingService,
        IRecommendationService recommendationService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _schedulingService = schedulingService ?? throw new ArgumentNullException(nameof(schedulingService));
        _recommendationService = recommendationService ?? throw new ArgumentNullException(nameof(recommendationService));
    }

    /// <summary>
    /// Автоматически запускает поиск окон, выбирает ближайшее и возвращает результат.
    /// </summary>
    /// <param name="groupId">Идентификатор группы.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Результат авто-планирования.</returns>
    public async Task<AutoPlanningResult?> AutoPlanGroupAsync(int groupId, CancellationToken ct)
    {
        var group = await LoadFreshGroupAsync(groupId, ct);
        if (group == null)
        {
            _logger.LogWarning("Группа {GroupId} не найдена для авто-планирования", groupId);
            return new AutoPlanningResult { Success = false, Message = "Группа не найдена" };
        }

        LogPlayerSlotsForDebug(group);

        var intersections = await _schedulingService.FindIntersectionsAsync(groupId, BotConstants.MinPlanningDurationHours);
        if (intersections.Count == 0)
        {
            _logger.LogInformation("Пересечения не найдены для группы {GroupName}, запускаем рекомендации", group.Name);
            return await HandleNoIntersectionsAsync(group, ct);
        }

        var nearestSlot = intersections.OrderBy(i => i.Start).First();
        _logger.LogInformation("Найдено пересечение для {GroupName}: {StartTime}", group.Name, nearestSlot.Start);

        return new AutoPlanningResult
        {
            Success = true,
            HasIntersection = true,
            SelectedSlot = nearestSlot,
            Message = $"Найдено окно: {nearestSlot.Start:dd.MM HH:mm} - {nearestSlot.End:HH:mm}"
        };
    }

    /// <summary>
    /// Обновляет данные сессии в группе.
    /// </summary>
    /// <param name="group">Группа для обновления.</param>
    /// <param name="sessionStartUtc">Время начала сессии в UTC.</param>
    public static void UpdateGroupSessionData(Group group, DateTime sessionStartUtc)
    {
        group.CurrentSessionUtc = sessionStartUtc;
        group.ConfirmedPlayerIds.Clear();
        group.DeclinedPlayerIds.Clear();
        group.SessionStatus = SessionStatus.Pending;
    }

    /// <summary>
    /// Проверяет доступность игроков на уже назначенной сессии.
    /// </summary>
    /// <param name="group">Группа для проверки.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Результат проверки доступности.</returns>
    public async Task<SessionAvailabilityResult> CheckSessionAvailabilityAsync(Group group, CancellationToken ct)
    {
        if (group.CurrentSessionUtc == null)
        {
            _logger.LogDebug("Группа {GroupName} не имеет назначенной сессии", group.Name);
            return new SessionAvailabilityResult { HasSession = false };
        }

        _logger.LogInformation("Проверка доступности для сессии {GroupName} на {SessionTime}", group.Name, group.CurrentSessionUtc);

        var sessionStart = group.CurrentSessionUtc.Value;
        var (canAttendPlayers, cannotAttendPlayers) = await CheckPlayersAvailabilityAsync(group, sessionStart, ct);
        var adminsCanAttend = await CheckAdminsAvailabilityForSessionAsync(group, canAttendPlayers);
        var attendanceRate = CalculateAttendanceRate(canAttendPlayers.Count, group.Players.Count);

        return new SessionAvailabilityResult
        {
            HasSession = true,
            CanAttendPlayers = canAttendPlayers,
            CannotAttendPlayers = cannotAttendPlayers,
            AdminsCanAttend = adminsCanAttend,
            AttendanceRate = attendanceRate,
            ShouldConfirm = attendanceRate >= BotConstants.ConfirmationThreshold && adminsCanAttend,
            ShouldReschedule = attendanceRate < BotConstants.ConfirmationThreshold || !adminsCanAttend
        };
    }

    /// <summary>
    /// Проверяет доступность всех игроков группы.
    /// </summary>
    private async Task<(List<Player> CanAttend, List<Player> CannotAttend)> CheckPlayersAvailabilityAsync(
        Group group,
        DateTime sessionStart,
        CancellationToken ct)
    {
        var canAttend = new List<Player>();
        var cannotAttend = new List<Player>();

        foreach (var player in group.Players)
        {
            var playerSlots = await _db.Slots.Where(s => s.PlayerId == player.TelegramId).ToListAsync(ct);
            var canPlayerAttend = playerSlots.Any(s => s.DateTimeUtc <= sessionStart && s.DateTimeUtc.AddHours(1) > sessionStart);
            (canPlayerAttend ? canAttend : cannotAttend).Add(player);
        }

        return (canAttend, cannotAttend);
    }

    /// <summary>
    /// Проверяет доступность администраторов для сессии.
    /// </summary>
    private static Task<bool> CheckAdminsAvailabilityForSessionAsync(Group group, List<Player> canAttendPlayers)
    {
        var adminsInGroup = group.Players.Where(p => group.AdminIds.Contains(p.TelegramId)).ToList();
        return Task.FromResult(adminsInGroup.All(canAttendPlayers.Contains));
    }

    /// <summary>
    /// Рассчитывает процент присутствующих игроков.
    /// </summary>
    private static double CalculateAttendanceRate(int canAttendCount, int totalCount) =>
        totalCount > 0 ? (double)canAttendCount / totalCount : 0;

    /// <summary>
    /// Загружает актуальные данные группы из БД.
    /// </summary>
    private async Task<Group?> LoadFreshGroupAsync(int groupId, CancellationToken ct) =>
        await _db.Groups
            .Include(g => g.Players)
            .ThenInclude(p => p.Slots)
            .AsSplitQuery()
            .FirstOrDefaultAsync(g => g.Id == groupId, ct);

    /// <summary>
    /// Логирует количество слотов у каждого игрока для отладки.
    /// </summary>
    private void LogPlayerSlotsForDebug(Group group)
    {
        foreach (var player in group.Players)
        {
            _logger.LogDebug("Игрок {Username} (ID={TelegramId}) имеет {SlotsCount} слотов",
                player.Username,
                player.TelegramId,
                player.Slots.Count);
        }
    }

    /// <summary>
    /// Обрабатывает ситуацию, когда пересечения не найдены — запускает рекомендации.
    /// </summary>
    private async Task<AutoPlanningResult?> HandleNoIntersectionsAsync(Group group, CancellationToken ct)
    {
        var playersAvailability = await BuildPlayersAvailabilityAsync(group, ct);
        var recommendationResult = _recommendationService.FindRecommendations(playersAvailability, BotConstants.MinPlanningDurationHours);

        if (!recommendationResult.HasRecommendations)
        {
            return new AutoPlanningResult
            {
                Success = false,
                HasIntersection = false,
                HasRecommendations = false,
                Message = "Рекомендации не найдены"
            };
        }

        var bestOption = recommendationResult.GetBestOption();
        if (bestOption != null)
            return new AutoPlanningResult
            {
                Success = true,
                HasIntersection = false,
                HasRecommendations = true,
                RecommendationResult = recommendationResult,
                BestOption = bestOption,
                Message = $"Найдена рекомендация: {bestOption.ProposedStartTime:dd.MM HH:mm}"
            };
        return null;
    }

    /// <summary>
    /// Строит список доступности игроков для сервиса рекомендаций.
    /// </summary>
    private async Task<List<PlayerAvailability>> BuildPlayersAvailabilityAsync(Group group, CancellationToken ct)
    {
        var playersAvailability = new List<PlayerAvailability>();
        foreach (var player in group.Players)
        {
            var slots = await _db.Slots
                .Where(s => s.PlayerId == player.TelegramId)
                .OrderBy(s => s.DateTimeUtc)
                .ToListAsync(ct);
            var availableSlots = slots.Select(s => new TimeSlot
            {
                Start = s.DateTimeUtc,
                End = s.DateTimeUtc.AddHours(1)
            }).ToList();
            playersAvailability.Add(new PlayerAvailability
            {
                PlayerId = player.TelegramId,
                PlayerName = player.Username,
                AvailableSlots = availableSlots,
                PreferredStartTime = availableSlots.FirstOrDefault()?.Start,
            });
        }
        _logger.LogInformation("Построена доступность для {Count} игроков группы {GroupName}",
            playersAvailability.Count,
            group.Name);
        return playersAvailability;
    }

    /// <summary>
    /// Сбрасывает данные голосования в группе.
    /// </summary>
    /// <param name="group">Группа для сброса.</param>
    public static void ResetGroupVotingData(Group group)
    {
        group.CurrentSessionUtc = null;
        group.ConfirmedPlayerIds.Clear();
        group.DeclinedPlayerIds.Clear();
        group.FinishedVotingPlayerIds.Clear();
    }

    /// <summary>
    /// Получает рекомендации для группы.
    /// </summary>
    /// <param name="group">Группа для получения рекомендаций.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Результат рекомендаций.</returns>
    public async Task<RecommendationResult> GetRecommendationsForGroupAsync(Group group, CancellationToken ct)
    {
        var playersAvailability = await BuildPlayersAvailabilityAsync(group, ct);
        return _recommendationService.FindRecommendations(playersAvailability, BotConstants.MinPlanningDurationHours);
    }
}

/// <summary>
/// Результат авто-планирования.
/// </summary>
public class AutoPlanningResult
{
    public bool Success { get; init; }
    public bool HasIntersection { get; init; }
    public bool HasRecommendations { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeRange? SelectedSlot { get; init; }
    public RecommendationResult? RecommendationResult { get; init; }
    public RecommendationOption? BestOption { get; init; }
}

/// <summary>
/// Результат проверки доступности сессии.
/// </summary>
public class SessionAvailabilityResult
{
    public bool HasSession { get; init; }
    public List<Player> CanAttendPlayers { get; init; } = [];
    public List<Player> CannotAttendPlayers { get; init; } = [];
    public bool AdminsCanAttend { get; init; }
    public double AttendanceRate { get; init; }
    public bool ShouldConfirm { get; init; }
    public bool ShouldReschedule { get; init; }
}