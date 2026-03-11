using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TgDataPlanner.Common;

namespace TgDataPlanner.Services;

/// <summary>
/// Статус процесса планирования сессии
/// </summary>
public enum PlanningStatus
{
    /// <summary>
    /// Ожидание заполнения доступности всеми игроками
    /// </summary>
    WaitingForAvailability,

    /// <summary>
    /// Найдено прямое пересечение, ожидание подтверждения
    /// </summary>
    WaitingForConfirmation,

    /// <summary>
    /// Прямое пересечение не найдено, отправлены рекомендации
    /// </summary>
    RecommendationsSent,

    /// <summary>
    /// Сессия успешно запланирована
    /// </summary>
    Scheduled,

    /// <summary>
    /// Планирование отменено
    /// </summary>
    Cancelled
}

/// <summary>
/// Информация о процессе планирования сессии
/// </summary>
public class PlanningSession
{
    /// <summary>
    /// Уникальный идентификатор сессии планирования
    /// </summary>
    public long SessionId { get; set; }

    /// <summary>
    /// Идентификатор чата Telegram
    /// </summary>
    public long ChatId { get; set; }

    /// <summary>
    /// Идентификатор пользователя, создавшего запрос (Админ)
    /// </summary>
    public long RequestedBy { get; set; }

    /// <summary>
    /// Список участников сессии
    /// </summary>
    public List<PlanningParticipant> Participants { get; set; } = new List<PlanningParticipant>();

    /// <summary>
    /// Требуемая длительность сессии в часах
    /// </summary>
    public double SessionDurationHours { get; set; }

    /// <summary>
    /// Текущий статус планирования
    /// </summary>
    public PlanningStatus Status { get; set; }

    /// <summary>
    /// Найденное время начала сессии (если найдено)
    /// </summary>
    public DateTime? ScheduledStartTime { get; set; }

    /// <summary>
    /// Найденное время окончания сессии (если найдено)
    /// </summary>
    public DateTime? ScheduledEndTime { get; set; }

    /// <summary>
    /// Результат рекомендаций (если прямое пересечение не найдено)
    /// </summary>
    public RecommendationResult RecommendationResult { get; set; }

    /// <summary>
    /// Текущий выбранный вариант рекомендации (индекс в списке)
    /// </summary>
    public int SelectedRecommendationIndex { get; set; } = -1;

    /// <summary>
    /// Количество игроков, подтвердивших выбранное время
    /// </summary>
    public int ConfirmationCount { get; set; }

    /// <summary>
    /// Дата и время создания сессии планирования
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Дата и время последнего обновления
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Возвращает количество игроков, заполнивших доступность
    /// </summary>
    public int PlayersWithAvailabilityCount => Participants.Count(p => p.AvailabilitySlots.Count > 0);

    /// <summary>
    /// Возвращает общее количество игроков
    /// </summary>
    public int TotalPlayersCount => Participants.Count;

    /// <summary>
    /// Проверяет, все ли игроки заполнили доступность
    /// </summary>
    public bool AllPlayersFilledAvailability => PlayersWithAvailabilityCount == TotalPlayersCount;

    /// <summary>
    /// Проверяет, достигнут ли порог согласий (75%)
    /// </summary>
    public bool HasEnoughConfirmations => TotalPlayersCount > 0 
        && (double)ConfirmationCount / TotalPlayersCount >= 0.75;

    /// <summary>
    /// Процент игроков, подтвердивших время
    /// </summary>
    public double ConfirmationPercentage => TotalPlayersCount > 0
        ? (double)ConfirmationCount / TotalPlayersCount * 100
        : 0;
}

/// <summary>
/// Участник сессии планирования
/// </summary>
public class PlanningParticipant
{
    /// <summary>
    /// Идентификатор игрока (Telegram ChatId или UserId)
    /// </summary>
    public long PlayerId { get; set; }

    /// <summary>
    /// Имя игрока
    /// </summary>
    public string PlayerName { get; set; }

    /// <summary>
    /// Список доступных временных слотов игрока
    /// </summary>
    public List<TimeSlot> AvailabilitySlots { get; set; } = new List<TimeSlot>();

    /// <summary>
    /// Предпочтительное время начала (если указано)
    /// </summary>
    public DateTime? PreferredStartTime { get; set; }

    /// <summary>
    /// Предпочтительное время окончания (если указано)
    /// </summary>
    public DateTime? PreferredEndTime { get; set; }

    /// <summary>
    /// Подтвердил ли игрок выбранное время
    /// </summary>
    public bool HasConfirmed { get; set; }

    /// <summary>
    /// Дата и время последнего обновления доступности
    /// </summary>
    public DateTime LastAvailabilityUpdate { get; set; }
}

/// <summary>
/// Результат попытки планирования сессии
/// </summary>
public class PlanningResult
{
    /// <summary>
    /// Успешно ли выполнено планирование
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Статус планирования
    /// </summary>
    public PlanningStatus Status { get; set; }

    /// <summary>
    /// Сообщение для пользователя
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// Найденное время начала (если найдено)
    /// </summary>
    public DateTime? ScheduledStartTime { get; set; }

    /// <summary>
    /// Найденное время окончания (если найдено)
    /// </summary>
    public DateTime? ScheduledEndTime { get; set; }

    /// <summary>
    /// Список рекомендаций (если прямое пересечение не найдено)
    /// </summary>
    public List<RecommendationOption> Recommendations { get; set; } = new List<RecommendationOption>();
}

/// <summary>
/// Сервис планирования игровых сессий.
/// Управляет процессом сбора доступности, поиска пересечений и работы с рекомендациями.
/// </summary>
public class SessionPlanningService
{
    private readonly IRecommendationService _recommendationService;
    private readonly Dictionary<long, PlanningSession> _planningSessions;
    private readonly object _lock = new object();

    public SessionPlanningService(IRecommendationService recommendationService)
    {
        _recommendationService = recommendationService;
        _planningSessions = new Dictionary<long, PlanningSession>();
    }

    /// <summary>
    /// Создает новую сессию планирования по запросу администратора.
    /// </summary>
    public PlanningSession CreatePlanningSession(
        long chatId,
        long requestedBy,
        List<long> participantIds,
        double sessionDurationHours)
    {
        lock (_lock)
        {
            var session = new PlanningSession
            {
                SessionId = GenerateSessionId(),
                ChatId = chatId,
                RequestedBy = requestedBy,
                SessionDurationHours = sessionDurationHours,
                Status = PlanningStatus.WaitingForAvailability,
                Participants = participantIds.Select(id => new PlanningParticipant
                {
                    PlayerId = id,
                    PlayerName = $"Игрок {id}" // TODO: Заменить на получение имени из БД
                }).ToList()
            };

            _planningSessions[session.SessionId] = session;

            return session;
        }
    }

    /// <summary>
    /// Получает сессию планирования по идентификатору.
    /// </summary>
    public PlanningSession GetSession(long sessionId)
    {
        lock (_lock)
        {
            _planningSessions.TryGetValue(sessionId, out var session);
            return session;
        }
    }

    /// <summary>
    /// Получает активную сессию планирования для чата.
    /// </summary>
    public PlanningSession GetActiveSessionForChat(long chatId)
    {
        lock (_lock)
        {
            return _planningSessions.Values
                .FirstOrDefault(s => s.ChatId == chatId && s.Status != PlanningStatus.Scheduled && s.Status != PlanningStatus.Cancelled);
        }
    }

    /// <summary>
    /// Обновляет доступность игрока в сессии планирования.
    /// </summary>
    public PlanningResult UpdatePlayerAvailability(
        long sessionId,
        long playerId,
        List<TimeSlot> availableSlots,
        DateTime? preferredStart = null,
        DateTime? preferredEnd = null)
    {
        lock (_lock)
        {
            if (!_planningSessions.TryGetValue(sessionId, out var session))
            {
                return new PlanningResult
                {
                    Success = false,
                    Status = PlanningStatus.Cancelled,
                    Message = "Сессия планирования не найдена"
                };
            }

            var participant = session.Participants.FirstOrDefault(p => p.PlayerId == playerId);
            if (participant == null)
            {
                return new PlanningResult
                {
                    Success = false,
                    Status = session.Status,
                    Message = "Игрок не является участником сессии"
                };
            }

            participant.AvailabilitySlots = availableSlots;
            participant.PreferredStartTime = preferredStart;
            participant.PreferredEndTime = preferredEnd;
            participant.LastAvailabilityUpdate = DateTime.UtcNow;
            session.UpdatedAt = DateTime.UtcNow;

            // Проверяем, все ли игроки заполнили доступность
            if (session.AllPlayersFilledAvailability && session.Status == PlanningStatus.WaitingForAvailability)
            {
                return TryAutoPlanSession(session);
            }

            return new PlanningResult
            {
                Success = true,
                Status = session.Status,
                Message = $"Доступность обновлена. Заполнено: {session.PlayersWithAvailabilityCount}/{session.TotalPlayersCount}"
            };
        }
    }

    /// <summary>
    /// Пытается автоматически запланировать сессию при заполнении всеми игроками.
    /// </summary>
    private PlanningResult TryAutoPlanSession(PlanningSession session)
    {
        // Ищем прямое пересечение без сдвигов
        var directIntersection = FindDirectIntersection(session);

        if (directIntersection != null)
        {
            // Найдено прямое пересечение - запрашиваем подтверждение
            session.ScheduledStartTime = directIntersection.Start;
            session.ScheduledEndTime = directIntersection.End;
            session.Status = PlanningStatus.WaitingForConfirmation;

            return new PlanningResult
            {
                Success = true,
                Status = PlanningStatus.WaitingForConfirmation,
                ScheduledStartTime = directIntersection.Start,
                ScheduledEndTime = directIntersection.End,
                Message = $"Найдено время для всех игроков: {directIntersection.Start:dd.MM.yyyy HH:mm} - {directIntersection.End:dd.MM.yyyy HH:mm}. Требуется подтверждение."
            };
        }

        // Прямое пересечение не найдено - запускаем рекомендации
        return StartRecommendationProcess(session);
    }

    /// <summary>
    /// Ищет прямое пересечение доступности всех игроков без сдвигов.
    /// </summary>
    private TimeSlot FindDirectIntersection(PlanningSession session)
    {
        if (session.Participants.Count == 0)
        {
            return null;
        }

        var allSlots = session.Participants
            .Where(p => p.AvailabilitySlots.Count > 0)
            .Select(p => p.AvailabilitySlots)
            .ToList();

        if (allSlots.Count != session.Participants.Count)
        {
            return null;
        }

        // Начинаем с первого игрока
        var intersection = allSlots[0].ToList();

        // Последовательно пересекаем со слотами остальных игроков
        for (int i = 1; i < allSlots.Count; i++)
        {
            var newIntersection = new List<TimeSlot>();

            foreach (var slot1 in intersection)
            {
                foreach (var slot2 in allSlots[i])
                {
                    var intersectStart = slot1.Start > slot2.Start ? slot1.Start : slot2.Start;
                    var intersectEnd = slot1.End < slot2.End ? slot1.End : slot2.End;

                    if (intersectStart < intersectEnd)
                    {
                        newIntersection.Add(new TimeSlot
                        {
                            Start = intersectStart,
                            End = intersectEnd
                        });
                    }
                }
            }

            intersection = newIntersection;

            if (intersection.Count == 0)
            {
                return null;
            }
        }

        // Проверяем, есть ли пересечение достаточной длительности
        var sessionDuration = TimeSpan.FromHours(session.SessionDurationHours);
        var suitableSlot = intersection.FirstOrDefault(s => s.DurationHours >= session.SessionDurationHours);

        if (suitableSlot != null)
        {
            return new TimeSlot
            {
                Start = suitableSlot.Start,
                End = suitableSlot.Start + sessionDuration
            };
        }

        return null;
    }

    /// <summary>
    /// Запускает процесс рекомендаций когда прямое пересечение не найдено.
    /// </summary>
    private PlanningResult StartRecommendationProcess(PlanningSession session)
    {
        var playersAvailability = session.Participants.Select(p => new PlayerAvailability
        {
            PlayerId = p.PlayerId,
            PlayerName = p.PlayerName,
            AvailableSlots = p.AvailabilitySlots,
            PreferredStartTime = p.PreferredStartTime,
            PreferredEndTime = p.PreferredEndTime
        }).ToList();

        var recommendationResult = _recommendationService.FindRecommendations(
            playersAvailability,
            session.SessionDurationHours);

        if (!recommendationResult.HasRecommendations)
        {
            session.Status = PlanningStatus.Cancelled;

            return new PlanningResult
            {
                Success = false,
                Status = PlanningStatus.Cancelled,
                Message = "К сожалению, не удалось найти подходящее время для сессии. Попробуйте расширить доступность."
            };
        }

        session.RecommendationResult = recommendationResult;
        session.Status = PlanningStatus.RecommendationsSent;

        // Берем лучший вариант для предложения
        var bestOption = recommendationResult.GetBestOption();
        session.SelectedRecommendationIndex = 0;

        return new PlanningResult
        {
            Success = true,
            Status = PlanningStatus.RecommendationsSent,
            ScheduledStartTime = bestOption.ProposedStartTime,
            ScheduledEndTime = bestOption.ProposedEndTime,
            Recommendations = recommendationResult.Options.Take(3).ToList(),
            Message = $"Прямое пересечение не найдено. Предлагаем {recommendationResult.OptionsCount} вариантов. Лучший: {bestOption.ProposedStartTime:dd.MM.yyyy HH:mm} ({bestOption.GetPriorityDescription()})"
        };
    }

    /// <summary>
    /// Обрабатывает подтверждение игроком выбранного времени.
    /// </summary>
    public PlanningResult ConfirmTime(
        long sessionId,
        long playerId)
    {
        lock (_lock)
        {
            if (!_planningSessions.TryGetValue(sessionId, out var session))
            {
                return new PlanningResult
                {
                    Success = false,
                    Status = PlanningStatus.Cancelled,
                    Message = "Сессия планирования не найдена"
                };
            }

            var participant = session.Participants.FirstOrDefault(p => p.PlayerId == playerId);
            if (participant == null)
            {
                return new PlanningResult
                {
                    Success = false,
                    Status = session.Status,
                    Message = "Игрок не является участником сессии"
                };
            }

            if (participant.HasConfirmed)
            {
                return new PlanningResult
                {
                    Success = false,
                    Status = session.Status,
                    Message = "Вы уже подтвердили это время"
                };
            }

            participant.HasConfirmed = true;
            session.ConfirmationCount = session.Participants.Count(p => p.HasConfirmed);
            session.UpdatedAt = DateTime.UtcNow;

            // Проверяем, достигнут ли порог 75%
            if (session.HasEnoughConfirmations)
            {
                session.Status = PlanningStatus.Scheduled;

                return new PlanningResult
                {
                    Success = true,
                    Status = PlanningStatus.Scheduled,
                    ScheduledStartTime = session.ScheduledStartTime,
                    ScheduledEndTime = session.ScheduledEndTime,
                    Message = $"Сессия запланирована! {session.ConfirmationCount}/{session.TotalPlayersCount} игроков подтвердили ({session.ConfirmationPercentage:F0}%)."
                };
            }

            return new PlanningResult
            {
                Success = true,
                Status = session.Status,
                Message = $"Подтверждение принято. {session.ConfirmationCount}/{session.TotalPlayersCount} игроков подтвердили ({session.ConfirmationPercentage:F0}%). Нужно {Math.Ceiling(session.TotalPlayersCount * 0.75)} подтверждений."
            };
        }
    }

    /// <summary>
    /// Выбирает другой вариант рекомендации.
    /// </summary>
    public PlanningResult SelectRecommendation(
        long sessionId,
        long playerId,
        int recommendationIndex)
    {
        lock (_lock)
        {
            if (!_planningSessions.TryGetValue(sessionId, out var session))
            {
                return new PlanningResult
                {
                    Success = false,
                    Status = PlanningStatus.Cancelled,
                    Message = "Сессия планирования не найдена"
                };
            }

            // Только админ может выбирать вариант рекомендации
            if (playerId != session.RequestedBy)
            {
                return new PlanningResult
                {
                    Success = false,
                    Status = session.Status,
                    Message = "Только создатель запроса может выбрать вариант рекомендации"
                };
            }

            if (session.RecommendationResult == null || recommendationIndex >= session.RecommendationResult.Options.Count)
            {
                return new PlanningResult
                {
                    Success = false,
                    Status = session.Status,
                    Message = "Неверный индекс рекомендации"
                };
            }

            session.SelectedRecommendationIndex = recommendationIndex;
            var selectedOption = session.RecommendationResult.Options[recommendationIndex];

            session.ScheduledStartTime = selectedOption.ProposedStartTime;
            session.ScheduledEndTime = selectedOption.ProposedEndTime;

            // Сбрасываем подтверждения при смене варианта
            foreach (var participant in session.Participants)
            {
                participant.HasConfirmed = false;
            }
            session.ConfirmationCount = 0;
            session.Status = PlanningStatus.WaitingForConfirmation;
            session.UpdatedAt = DateTime.UtcNow;

            return new PlanningResult
            {
                Success = true,
                Status = PlanningStatus.WaitingForConfirmation,
                ScheduledStartTime = selectedOption.ProposedStartTime,
                ScheduledEndTime = selectedOption.ProposedEndTime,
                Message = $"Выбран вариант #{recommendationIndex + 1}: {selectedOption.ProposedStartTime:dd.MM.yyyy HH:mm} ({selectedOption.GetPriorityDescription()})"
            };
        }
    }

    /// <summary>
    /// Отменяет сессию планирования.
    /// </summary>
    public PlanningResult CancelSession(long sessionId, long playerId)
    {
        lock (_lock)
        {
            if (!_planningSessions.TryGetValue(sessionId, out var session))
            {
                return new PlanningResult
                {
                    Success = false,
                    Status = PlanningStatus.Cancelled,
                    Message = "Сессия планирования не найдена"
                };
            }

            // Только админ может отменить сессию
            if (playerId != session.RequestedBy)
            {
                return new PlanningResult
                {
                    Success = false,
                    Status = session.Status,
                    Message = "Только создатель запроса может отменить сессию"
                };
            }

            session.Status = PlanningStatus.Cancelled;
            session.UpdatedAt = DateTime.UtcNow;

            return new PlanningResult
            {
                Success = true,
                Status = PlanningStatus.Cancelled,
                Message = "Сессия планирования отменена"
            };
        }
    }

    /// <summary>
    /// Запускает рекомендации вручную (по запросу админа).
    /// </summary>
    public PlanningResult RequestRecommendations(long sessionId, long playerId)
    {
        lock (_lock)
        {
            if (!_planningSessions.TryGetValue(sessionId, out var session))
            {
                return new PlanningResult
                {
                    Success = false,
                    Status = PlanningStatus.Cancelled,
                    Message = "Сессия планирования не найдена"
                };
            }

            // Только админ может запросить рекомендации
            if (playerId != session.RequestedBy)
            {
                return new PlanningResult
                {
                    Success = false,
                    Status = session.Status,
                    Message = "Только создатель запроса может запросить рекомендации"
                };
            }

            // Проверяем, все ли заполнили доступность
            if (!session.AllPlayersFilledAvailability)
            {
                return new PlanningResult
                {
                    Success = false,
                    Status = session.Status,
                    Message = $"Не все игроки заполнили доступность: {session.PlayersWithAvailabilityCount}/{session.TotalPlayersCount}"
                };
            }

            return StartRecommendationProcess(session);
        }
    }

    /// <summary>
    /// Генерирует уникальный идентификатор сессии.
    /// </summary>
    private long GenerateSessionId()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// Очищает старые завершенные сессии.
    /// </summary>
    public void CleanupOldSessions(int maxAgeDays = 7)
    {
        lock (_lock)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-maxAgeDays);

            var oldSessionIds = _planningSessions
                .Where(kv => kv.Value.UpdatedAt < cutoffDate &&
                       (kv.Value.Status == PlanningStatus.Scheduled ||
                        kv.Value.Status == PlanningStatus.Cancelled))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var sessionId in oldSessionIds)
            {
                _planningSessions.Remove(sessionId);
            }
        }
    }
}
