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
        double sessionDurationHours,
        double searchWindowHours,
        int timeStepMinutes = 60)
    {
        var result = new RecommendationResult();
        var playersList = players.ToList();

        if (playersList.Count == 0)
        {
            return result;
        }

        var searchStartTime = DateTime.UtcNow;
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
        return FindRecommendations(players, sessionDurationHours, searchWindowHours: 168, timeStepMinutes: 60);
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
        var isAvailable = CanCoverTimeRange(player.AvailableSlots, proposedStart, proposedEnd);
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

            if (option.AttendingPlayersCount > 0)
            {
                allOptions.Add(option);
            }
        }

        // Находим максимальное количество игроков
        var maxPlayers = allOptions.Any() ? allOptions.Max(o => o.AttendingPlayersCount) : 0;

        // Оставляем только окна с максимальным количеством игроков
        var bestOptions = allOptions
            .Where(o => o.AttendingPlayersCount == maxPlayers)
            .OrderBy(o => o.ProposedStartTime)
            .Take(5)
            .ToList();

        return bestOptions;
    }

    /// <summary>
    /// Проверяет, могут ли доступные слоты игрока покрыть предложенный временной диапазон.
    /// Учитывает несколько смежных слотов (например, 3 слота по 1 часу для 3-часовой сессии).
    /// </summary>
    private static bool CanCoverTimeRange(List<TimeSlot> availableSlots, DateTime rangeStart, DateTime rangeEnd)
    {
        if (availableSlots.Count == 0)
        {
            return false;
        }

        // Сортируем слоты по времени
        var sortedSlots = availableSlots.OrderBy(s => s.Start).ToList();

        // Ищем непрерывную последовательность слотов, покрывающую диапазон
        var coveredEnd = rangeStart;

        foreach (var slot in sortedSlots)
        {
            // Если слот начинается там, где мы закончили покрытие (или раньше)
            if (slot.Start <= coveredEnd && slot.End > coveredEnd)
            {
                coveredEnd = slot.End;
            }

            // Если покрыли весь диапазон
            if (coveredEnd >= rangeEnd)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Оценивает временной слот и создает вариант рекомендации.
    /// </summary>
    private RecommendationOption EvaluateTimeSlot(
        List<PlayerAvailability> players,
        DateTime proposedStart,
        DateTime proposedEnd)
    {
        var option = new RecommendationOption
        {
            ProposedStartTime = proposedStart,
            ProposedEndTime = proposedEnd,
            Priority = RecommendationPriority.AllAttendShift1h,
            TotalPlayersCount = players.Count,
            AttendingPlayersCount = 0
        };

        var attendingPlayerIds = new List<long>();
        var attendingPlayerNames = new List<string>();

        foreach (var player in players)
        {
            var isAvailable = CanCoverTimeRange(player.AvailableSlots, proposedStart, proposedEnd);

            if (isAvailable)
            {
                attendingPlayerIds.Add(player.PlayerId);
                attendingPlayerNames.Add(player.PlayerName);
            }
        }

        option.AttendingPlayersCount = attendingPlayerIds.Count;
        option.ExcludedPlayerIds = players
            .Select(p => p.PlayerId)
            .Except(attendingPlayerIds)
            .ToList();

        // Сохраняем имена присутствующих игроков
        option.AttendingPlayerNames = attendingPlayerNames;

        return option;
    }
}