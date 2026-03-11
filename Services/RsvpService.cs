using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgDataPlanner.Common;
using TgDataPlanner.Configuration;
using TgDataPlanner.Data;
using TgDataPlanner.Data.Entities;

namespace TgDataPlanner.Services;

/// <summary>
/// Сервис для управления RSVP (подтверждение/отказ от участия в сессии).
/// Инкапсулирует логику обработки ответов игроков и финализации сессий.
/// </summary>
public class RsvpService
{
    private readonly AppDbContext _db;
    private readonly ILogger<RsvpService> _logger;
    private readonly SessionPlanningService _planningService;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="RsvpService"/>.
    /// </summary>
    /// <param name="db">Контекст базы данных.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    /// <param name="planningService">Сервис планирования.</param>
    public RsvpService(
        AppDbContext db,
        ILogger<RsvpService> logger,
        SessionPlanningService planningService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _planningService = planningService ?? throw new ArgumentNullException(nameof(planningService));
    }

    /// <summary>
    /// Обновляет статус RSVP игрока в группе.
    /// </summary>
    /// <param name="group">Группа, в которой обновляется статус.</param>
    /// <param name="userId">Идентификатор игрока.</param>
    /// <param name="isComing">True если игрок подтверждает участие.</param>
    public static void UpdatePlayerRsvpStatus(Group group, long userId, bool isComing)
    {
        if (isComing)
        {
            if (!group.ConfirmedPlayerIds.Contains(userId))
                group.ConfirmedPlayerIds.Add(userId);
            group.DeclinedPlayerIds.Remove(userId);
        }
        else
        {
            group.ConfirmedPlayerIds.Remove(userId);
            if (!group.DeclinedPlayerIds.Contains(userId))
                group.DeclinedPlayerIds.Add(userId);
        }
    }

    /// <summary>
    /// Рассчитывает процент участия в сессии.
    /// </summary>
    /// <param name="group">Группа для расчёта.</param>
    /// <returns>Доля подтвердивших игроков (0.0–1.0).</returns>
    public double CalculateParticipationRate(Group group)
    {
        var allPlayers = GetTargetPlayers(group);
        var totalPlayers = allPlayers.Count;
        var confirmedCount = group.ConfirmedPlayerIds.Count;
        return totalPlayers > 0 ? (double)confirmedCount / totalPlayers : 0;
    }

    /// <summary>
    /// Проверяет, ответили ли все игроки группы.
    /// </summary>
    /// <param name="group">Группа для проверки.</param>
    /// <returns>True если все игроки ответили.</returns>
    public bool AreAllPlayersResponded(Group group)
    {
        var allPlayers = GetTargetPlayers(group);
        var totalPlayers = allPlayers.Count;
        var respondedCount = group.ConfirmedPlayerIds.Count + group.DeclinedPlayerIds.Count;
        return respondedCount >= totalPlayers && totalPlayers > 0;
    }

    /// <summary>
    /// Принимает решение о проведении сессии на основе процента подтверждений и присутствия админов.
    /// </summary>
    /// <param name="group">Группа для проверки.</param>
    /// <param name="participationRate">Текущий процент участия.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Результат финализации сессии.</returns>
    public async Task<SessionFinalizationResult> FinalizeSessionDecisionAsync(
        Group group,
        double participationRate,
        CancellationToken ct)
    {
        var allPlayers = GetTargetPlayers(group);
        var totalPlayers = allPlayers.Count;
        var confirmedCount = group.ConfirmedPlayerIds.Count;

        var adminsCanAttend = await CheckAdminsAvailabilityAsync(group, group.CurrentSessionUtc!.Value, ct);

        if (participationRate >= BotConstants.ConfirmationThreshold && adminsCanAttend.CanAttend)
        {
            return await ConfirmSessionAsync(group, participationRate, confirmedCount, totalPlayers, ct);
        }
        else if (!adminsCanAttend.CanAttend)
        {
            return await RescheduleSessionAsync(group, confirmedCount, totalPlayers, participationRate, adminsCanAttend.CannotAttendAdmins, ct);
        }
        else
        {
            return await CancelSessionAsync(group, confirmedCount, totalPlayers, participationRate, ct);
        }
    }

    /// <summary>
    /// Проверяет доступность администраторов на сессии.
    /// </summary>
    /// <param name="group">Группа для проверки.</param>
    /// <param name="sessionStart">Время начала сессии.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Результат проверки доступности админов.</returns>
    private async Task<(bool CanAttend, List<Player> CannotAttendAdmins)> CheckAdminsAvailabilityAsync(
        Group group,
        DateTime sessionStart,
        CancellationToken ct)
    {
        var adminsInGroup = group.Players.Where(p => group.AdminIds.Contains(p.TelegramId)).ToList();
        var cannotAttend = new List<Player>();

        foreach (var admin in adminsInGroup)
        {
            var adminSlots = await _db.Slots.Where(s => s.PlayerId == admin.TelegramId).ToListAsync(ct);
            var canAttend = adminSlots.Any(s => s.DateTimeUtc <= sessionStart && s.DateTimeUtc.AddHours(1) > sessionStart);
            if (!canAttend)
                cannotAttend.Add(admin);
        }

        return (cannotAttend.Count == 0, cannotAttend);
    }

    /// <summary>
    /// Подтверждает сессию и обновляет статус в БД.
    /// </summary>
    private async Task<SessionFinalizationResult> ConfirmSessionAsync(
        Group group,
        double participationRate,
        int confirmedCount,
        int totalPlayers,
        CancellationToken ct)
    {
        group.SessionStatus = SessionStatus.Confirmed;
        await _db.SaveChangesAsync(ct);

        return new SessionFinalizationResult
        {
            Status = SessionStatus.Confirmed,
            ParticipationRate = participationRate,
            ConfirmedCount = confirmedCount,
            TotalPlayers = totalPlayers
        };
    }

    /// <summary>
    /// Запускает перепланирование сессии.
    /// </summary>
    private async Task<SessionFinalizationResult> RescheduleSessionAsync(
        Group group,
        int confirmedCount,
        int totalPlayers,
        double participationRate,
        List<Player> cannotAttendAdmins,
        CancellationToken ct)
    {
        group.SessionStatus = SessionStatus.Rescheduled;
        ResetGroupVotingData(group);
        await _db.SaveChangesAsync(ct);

        return new SessionFinalizationResult
        {
            Status = SessionStatus.Rescheduled,
            ParticipationRate = participationRate,
            ConfirmedCount = confirmedCount,
            TotalPlayers = totalPlayers,
            CannotAttendAdmins = cannotAttendAdmins
        };
    }

    /// <summary>
    /// Отменяет сессию из-за недостатка игроков.
    /// </summary>
    private async Task<SessionFinalizationResult> CancelSessionAsync(
        Group group,
        int confirmedCount,
        int totalPlayers,
        double participationRate,
        CancellationToken ct)
    {
        group.SessionStatus = SessionStatus.Cancelled;
        group.CurrentSessionUtc = null;
        await _db.SaveChangesAsync(ct);

        return new SessionFinalizationResult
        {
            Status = SessionStatus.Cancelled,
            ParticipationRate = participationRate,
            ConfirmedCount = confirmedCount,
            TotalPlayers = totalPlayers
        };
    }

    /// <summary>
    /// Сбрасывает данные голосования в группе.
    /// </summary>
    /// <param name="group">Группа для сброса.</param>
    private static void ResetGroupVotingData(Group group)
    {
        group.CurrentSessionUtc = null;
        group.ConfirmedPlayerIds.Clear();
        group.DeclinedPlayerIds.Clear();
        group.FinishedVotingPlayerIds.Clear();
    }

    /// <summary>
    /// Возвращает список всех игроков группы для планирования и голосования.
    /// </summary>
    private static List<Player> GetTargetPlayers(Group group) =>
        group.Players.DistinctBy(p => p.TelegramId).ToList();

    /// <summary>
    /// Проверяет, завершили ли все игроки группы голосование.
    /// </summary>
    /// <param name="group">Группа для проверки.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>True если все игроки завершили голосование.</returns>
    public async Task<bool> AreAllPlayersFinishedVotingAsync(Group group, CancellationToken ct)
    {
        var freshGroup = await _db.Groups
            .Include(g => g.Players)
            .AsSplitQuery()
            .FirstOrDefaultAsync(g => g.Id == group.Id, ct);

        if (freshGroup?.Players == null || freshGroup.Players.Count == 0)
        {
            _logger.LogWarning("Группа {GroupId} не имеет игроков для проверки голосования", group?.Id);
            return false;
        }

        var finishedVotingIds = freshGroup.FinishedVotingPlayerIds?.ToHashSet() ?? [];
        foreach (var player in freshGroup.Players.Where(player => player?.TelegramId == null || !finishedVotingIds.Contains(player.TelegramId)))
        {
            _logger.LogDebug("Игрок {TelegramId} ещё не завершил голосование в группе {GroupName}", player.TelegramId, freshGroup.Name);
            return false;
        }

        _logger.LogDebug("Все {Count} игроков завершили голосование в группе {GroupName}", freshGroup.Players.Count, freshGroup.Name);
        return true;
    }
}

/// <summary>
/// Результат финализации сессии.
/// </summary>
public class SessionFinalizationResult
{
    public SessionStatus Status { get; init; }
    public double ParticipationRate { get; init; }
    public int ConfirmedCount { get; init; }
    public int TotalPlayers { get; init; }
    public List<Player> CannotAttendAdmins { get; init; } = [];
}