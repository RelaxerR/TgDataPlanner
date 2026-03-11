namespace TgDataPlanner.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Common;

/// <summary>
/// Сервис рекомендаций для планирования игровых сессий.
/// Используется когда прямое пересечение доступности всех игроков не найдено.
/// Философия: показать окна с максимальным количеством участников.
/// </summary>
public class RecommendationService : IRecommendationService
{
    /// <summary>
    /// Минимальная длительность окна в часах (для первого прохода).
    /// </summary>
    private const int MinDurationHours = 3;

    /// <summary>
    /// Минимальная длительность окна в часах (для второго прохода, если не найдено).
    /// </summary>
    private const int FallbackDurationHours = 2;

    /// <summary>
    /// Находит варианты планирования сессии на основе доступности игроков.
    /// Возвращает окна с максимальным количеством игроков.
    /// </summary>
    private RecommendationResult FindRecommendations(
        IEnumerable<PlayerAvailability> players,
        double searchWindowHours,
        int timeStepMinutes)
    {
        var result = new RecommendationResult();
        var playersList = players.ToList();
        if (playersList.Count == 0)
        {
            return result;
        }

        // Нормализуем время начала поиска до начала текущего часа
        var searchStartTime = NormalizeToHourStart(DateTime.UtcNow);
        var searchEndTime = searchStartTime.AddHours(searchWindowHours);
        var timeStep = TimeSpan.FromMinutes(timeStepMinutes);

        // Сначала ищем окна с минимальной длительностью (3 часа)
        var options = FindAllWindowsWithMaxPlayers(
            playersList,
            searchStartTime,
            searchEndTime,
            timeStep,
            MinDurationHours);

        // Если не найдено, пробуем 2 часа
        if (options.Count == 0)
        {
            options = FindAllWindowsWithMaxPlayers(
                playersList,
                searchStartTime,
                searchEndTime,
                timeStep,
                FallbackDurationHours);
        }

        result.AddOptions(options);
        return result;
    }

    /// <summary>
    /// Находит варианты планирования сессии с базовыми параметрами поиска.
    /// </summary>
    public RecommendationResult FindRecommendations(
        IEnumerable<PlayerAvailability> players,
        double sessionDurationHours)
    {
        return FindRecommendations(players, searchWindowHours: 168, timeStepMinutes: 60);
    }

    /// <summary>
    /// Проверяет, может ли указанный игрок присутствовать в предложенное время.
    /// </summary>
    public (bool CanAttend, double TimeShift) CheckPlayerAvailability(
        PlayerAvailability player,
        DateTime proposedStart,
        DateTime proposedEnd,
        double maxShiftHours)
    {
        var coverage = CalculateCoverage(player.AvailableSlots, proposedStart, proposedEnd);
        var isAvailable = coverage.HoursCovered >= (proposedEnd - proposedStart).TotalHours * 0.5;
        return (isAvailable, 0);
    }

    /// <summary>
    /// Рассчитывает сдвиг времени для игрока относительно его предпочтений.
    /// </summary>
    public double CalculateTimeShift(PlayerAvailability player, DateTime proposedStart)
    {
        if (!player.PreferredStartTime.HasValue)
        {
            return 0;
        }

        var shift = (proposedStart - player.PreferredStartTime.Value).TotalHours;
        return Math.Round(shift, 2);
    }

    /// <summary>
    /// Находит все окна с максимальным количеством игроков.
    /// </summary>
    private List<RecommendationOption> FindAllWindowsWithMaxPlayers(
        List<PlayerAvailability> players,
        DateTime searchStartTime,
        DateTime searchEndTime,
        TimeSpan timeStep,
        int durationHours)
    {
        var allOptions = new List<RecommendationOption>();
        var sessionDuration = TimeSpan.FromHours(durationHours);

        // Перебираем все возможные окна
        for (var currentTime = searchStartTime; currentTime < searchEndTime; currentTime += timeStep)
        {
            var proposedEnd = currentTime + sessionDuration;
            var option = EvaluateTimeSlot(players, currentTime, proposedEnd);
            // Добавляем варианты где хотя бы 1 игрок может присутствовать частично
            if (option.AttendingPlayersCount > 0 || option.PartialAttendPlayersCount > 0)
            {
                allOptions.Add(option);
            }
        }

        if (allOptions.Count == 0)
        {
            return allOptions;
        }

        // Сортируем по приоритету:
        // 1. Максимальное количество игроков с полным покрытием
        // 2. Максимальное количество игроков с частичным покрытием
        // 3. Максимальное количество часов покрытия
        // 4. Минимальное время начала
        var bestOptions = allOptions
            .OrderByDescending(o => o.AttendingPlayersCount)
            .ThenByDescending(o => o.PartialAttendPlayersCount)
            .ThenByDescending(o => o.TotalCoverageHours)
            .ThenBy(o => o.ProposedStartTime)
            .Take(5)
            .ToList();

        return bestOptions;
    }

    /// <summary>
    /// Проверяет, могут ли доступные слоты игрока покрыть предложенный временной диапазон.
    /// Возвращает информацию о покрытии (полное/частичное).
    /// </summary>
    private static CoverageResult CalculateCoverage(List<TimeSlot> availableSlots, DateTime rangeStart, DateTime rangeEnd)
    {
        if (availableSlots.Count == 0)
        {
            return new CoverageResult { HoursCovered = 0, IsFullCoverage = false };
        }

        // Нормализуем диапазон к началу часа
        rangeStart = NormalizeToHourStart(rangeStart);
        rangeEnd = NormalizeToHourStart(rangeEnd);

        // Сортируем слоты по времени
        var sortedSlots = availableSlots
            .OrderBy(s => s.Start)
            .Select(s => new TimeSlot
            {
                Start = NormalizeToHourStart(s.Start),
                End = NormalizeToHourStart(s.End)
            })
            .ToList();

        var totalHoursNeeded = (rangeEnd - rangeStart).TotalHours;

        var hoursCovered = (from slot in sortedSlots.Where(slot => slot.End > rangeStart).TakeWhile(slot => slot.Start < rangeEnd) let intersectionStart = slot.Start > rangeStart ? slot.Start : rangeStart let intersectionEnd = slot.End < rangeEnd ? slot.End : rangeEnd where intersectionEnd > intersectionStart select (intersectionEnd - intersectionStart).TotalHours).Sum();

        // Учитываем возможные перекрытия слотов (не считаем дважды)
        hoursCovered = Math.Min(hoursCovered, totalHoursNeeded);

        return new CoverageResult
        {
            HoursCovered = hoursCovered,
            IsFullCoverage = hoursCovered >= totalHoursNeeded
        };
    }

    /// <summary>
    /// Оценивает временной слот и создает вариант рекомендации.
    /// </summary>
    private RecommendationOption EvaluateTimeSlot(
        List<PlayerAvailability> players,
        DateTime proposedStart,
        DateTime proposedEnd)
    {
        // Нормализуем время к началу часа
        proposedStart = NormalizeToHourStart(proposedStart);
        proposedEnd = NormalizeToHourStart(proposedEnd);

        var totalDuration = (proposedEnd - proposedStart).TotalHours;

        var option = new RecommendationOption
        {
            ProposedStartTime = proposedStart,
            ProposedEndTime = proposedEnd,
            Priority = RecommendationPriority.AllAttendShift1h,
            TotalPlayersCount = players.Count,
            AttendingPlayersCount = 0,
            PartialAttendPlayersCount = 0,
            TotalCoverageHours = 0
        };

        var attendingPlayerIds = new List<long>();
        var attendingPlayerNames = new List<string>();
        var partialPlayerIds = new List<long>();
        var totalCoverageHours = 0.0;

        foreach (var player in players)
        {
            var coverage = CalculateCoverage(player.AvailableSlots, proposedStart, proposedEnd);

            if (coverage.IsFullCoverage)
            {
                // Игрок может покрыть весь диапазон
                attendingPlayerIds.Add(player.PlayerId);
                attendingPlayerNames.Add(player.PlayerName);
                totalCoverageHours += totalDuration;
            }
            else if (coverage.HoursCovered > 0)
            {
                // Игрок может покрыть часть диапазона
                partialPlayerIds.Add(player.PlayerId);
                totalCoverageHours += coverage.HoursCovered;
            }
        }

        option.AttendingPlayersCount = attendingPlayerIds.Count;
        option.PartialAttendPlayersCount = partialPlayerIds.Count;
        option.TotalCoverageHours = totalCoverageHours;
        option.ExcludedPlayerIds = players
            .Select(p => p.PlayerId)
            .Except(attendingPlayerIds)
            .Except(partialPlayerIds)
            .ToList();

        // Сохраняем имена присутствующих игроков
        option.AttendingPlayerNames = attendingPlayerNames;

        return option;
    }

    /// <summary>
    /// Нормализует дату и время до начала часа (обнуляет минуты, секунды, миллисекунды).
    /// </summary>
    /// <param name="dateTime">Исходная дата и время.</param>
    /// <returns>Дата и время, приведённая к началу часа.</returns>
    private static DateTime NormalizeToHourStart(DateTime dateTime)
    {
        return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, 0, 0, dateTime.Kind);
    }
}

/// <summary>
/// Результат расчёта покрытия временного диапазона слотами игрока.
/// </summary>
public class CoverageResult
{
    /// <summary>
    /// Количество часов, покрытых слотами игрока.
    /// </summary>
    public double HoursCovered { get; init; }

    /// <summary>
    /// Флаг полного покрытия диапазона.
    /// </summary>
    public bool IsFullCoverage { get; init; }
}