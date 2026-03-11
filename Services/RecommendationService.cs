namespace TgDataPlanner.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using TgDataPlanner.Common;

/// <summary>
/// Сервис рекомендаций для планирования игровых сессий.
/// Используется когда прямое пересечение доступности всех игроков не найдено.
/// Философия: лучше сдвинуть время всем немного, но чтобы участвовали все.
/// </summary>
public class RecommendationService : IRecommendationService
{
    /// <summary>
    /// Находит варианты планирования сессии на основе доступности игроков.
    /// Варианты сортируются по приоритету согласно философии:
    /// "Лучше сдвинуть время всем немного, но чтобы участвовали все".
    /// </summary>
    public RecommendationResult FindRecommendations(
        IEnumerable<PlayerAvailability> players,
        double sessionDurationHours,
        double searchWindowHours = 168,
        int timeStepMinutes = 30)
    {
        var result = new RecommendationResult();
        var playersList = players.ToList();

        if (playersList.Count == 0)
        {
            return result;
        }

        var searchStartTime = DateTime.UtcNow;
        var searchEndTime = searchStartTime.AddHours(searchWindowHours);
        var sessionDuration = TimeSpan.FromHours(sessionDurationHours);
        var timeStep = TimeSpan.FromMinutes(timeStepMinutes);

        // Приоритет 1: Все участвуют, сдвиг до 1 часа
        var priority1Options = FindOptionsWithPriority(
            playersList,
            searchStartTime,
            searchEndTime,
            timeStep,
            sessionDuration,
            RecommendationPriority.AllAttendShift1h,
            maxShiftHours: 1.0,
            allowExclusions: false,
            maxExcludedShiftHours: 0);

        result.AddOptions(priority1Options);

        // Приоритет 2: Один не участвует, без сдвига времени
        var priority2Options = FindOptionsWithPriority(
            playersList,
            searchStartTime,
            searchEndTime,
            timeStep,
            sessionDuration,
            RecommendationPriority.ExcludeOneNoShift,
            maxShiftHours: 0,
            allowExclusions: true,
            maxExcludedShiftHours: 0);

        result.AddOptions(priority2Options);

        // Приоритет 3: Все участвуют, сдвиг до 2 часов
        var priority3Options = FindOptionsWithPriority(
            playersList,
            searchStartTime,
            searchEndTime,
            timeStep,
            sessionDuration,
            RecommendationPriority.AllAttendShift2h,
            maxShiftHours: 2.0,
            allowExclusions: false,
            maxExcludedShiftHours: 0);

        result.AddOptions(priority3Options);

        // Приоритет 4: Один не участвует, сдвиг до 1 часа
        var priority4Options = FindOptionsWithPriority(
            playersList,
            searchStartTime,
            searchEndTime,
            timeStep,
            sessionDuration,
            RecommendationPriority.ExcludeOneShift1h,
            maxShiftHours: 1.0,
            allowExclusions: true,
            maxExcludedShiftHours: 0);

        result.AddOptions(priority4Options);

        // Приоритет 5: Все участвуют, сдвиг до 3 часов
        var priority5Options = FindOptionsWithPriority(
            playersList,
            searchStartTime,
            searchEndTime,
            timeStep,
            sessionDuration,
            RecommendationPriority.AllAttendShift3h,
            maxShiftHours: 3.0,
            allowExclusions: false,
            maxExcludedShiftHours: 0);

        result.AddOptions(priority5Options);

        // Приоритет 6: Один не участвует, сдвиг до 2 часов
        var priority6Options = FindOptionsWithPriority(
            playersList,
            searchStartTime,
            searchEndTime,
            timeStep,
            sessionDuration,
            RecommendationPriority.ExcludeOneShift2h,
            maxShiftHours: 2.0,
            allowExclusions: true,
            maxExcludedShiftHours: 0);

        result.AddOptions(priority6Options);

        return result;
    }

    /// <summary>
    /// Находит варианты планирования сессии с базовыми параметрами поиска.
    /// </summary>
    public RecommendationResult FindRecommendations(
        IEnumerable<PlayerAvailability> players,
        double sessionDurationHours)
    {
        return FindRecommendations(players, sessionDurationHours, searchWindowHours: 168, timeStepMinutes: 30);
    }

    /// <summary>
    /// Проверяет, может ли указанный игрок присутствовать в предложенное время
    /// с учетом допустимого сдвига от его предпочтений.
    /// </summary>
    public (bool CanAttend, double TimeShift) CheckPlayerAvailability(
        PlayerAvailability player,
        DateTime proposedStart,
        DateTime proposedEnd,
        double maxShiftHours)
    {
        var timeShift = CalculateTimeShift(player, proposedStart);

        // Проверяем сдвиг времени
        if (Math.Abs(timeShift) > maxShiftHours)
        {
            return (false, timeShift);
        }

        // Проверяем доступность в предложенное время
        var isAvailable = player.AvailableSlots.Any(slot =>
            slot.FullyCovers(proposedStart, proposedEnd));

        return (isAvailable, timeShift);
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
    /// Находит варианты с указанным приоритетом и параметрами.
    /// </summary>
    private List<RecommendationOption> FindOptionsWithPriority(
        List<PlayerAvailability> players,
        DateTime searchStartTime,
        DateTime searchEndTime,
        TimeSpan timeStep,
        TimeSpan sessionDuration,
        RecommendationPriority priority,
        double maxShiftHours,
        bool allowExclusions,
        double maxExcludedShiftHours)
    {
        var options = new List<RecommendationOption>();
        var maxOptionsPerPriority = 5; // Ограничиваем количество вариантов на приоритет

        for (var currentTime = searchStartTime; currentTime < searchEndTime; currentTime += timeStep)
        {
            var proposedEnd = currentTime + sessionDuration;

            // Проверяем каждый возможный вариант исключений
            var exclusionVariants = allowExclusions
                ? GenerateExclusionVariants(players.Count)
                : new List<List<int>> { new List<int>() };

            foreach (var excludedIndices in exclusionVariants)
            {
                var option = EvaluateTimeSlot(
                    players,
                    currentTime,
                    proposedEnd,
                    excludedIndices,
                    priority,
                    maxShiftHours,
                    maxExcludedShiftHours);

                if (option != null)
                {
                    options.Add(option);

                    if (options.Count >= maxOptionsPerPriority)
                    {
                        return options;
                    }
                }
            }

            // Если нашли варианты для этого времени с полным составом, не ищем с исключениями
            if (allowExclusions && options.Any(o => o.ExcludedPlayerIds.Count == 0))
            {
                continue;
            }
        }

        return options;
    }

    /// <summary>
    /// Генерирует варианты исключений игроков для перебора.
    /// </summary>
    private List<List<int>> GenerateExclusionVariants(int playerCount)
    {
        var variants = new List<List<int>>();

        // Вариант без исключений
        variants.Add(new List<int>());

        // Варианты с исключением одного игрока
        for (int i = 0; i < playerCount; i++)
        {
            variants.Add(new List<int> { i });
        }

        return variants;
    }

    /// <summary>
    /// Оценивает временной слот и создает вариант рекомендации если он подходит.
    /// </summary>
    private RecommendationOption EvaluateTimeSlot(
        List<PlayerAvailability> players,
        DateTime proposedStart,
        DateTime proposedEnd,
        List<int> excludedIndices,
        RecommendationPriority priority,
        double maxShiftHours,
        double maxExcludedShiftHours)
    {
        var option = new RecommendationOption
        {
            ProposedStartTime = proposedStart,
            ProposedEndTime = proposedEnd,
            Priority = priority,
            TotalPlayersCount = players.Count
        };

        var attendingCount = 0;
        var allAttendingWithinShift = true;

        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            var isExcluded = excludedIndices.Contains(i);

            if (isExcluded)
            {
                option.ExcludedPlayerIds.Add(player.PlayerId);
                continue;
            }

            var availabilityCheck = CheckPlayerAvailability(
                player,
                proposedStart,
                proposedEnd,
                maxShiftHours);

            if (!availabilityCheck.CanAttend)
            {
                allAttendingWithinShift = false;
                break;
            }

            option.PlayerTimeShifts[player.PlayerId] = availabilityCheck.TimeShift;
            attendingCount++;
        }

        if (!allAttendingWithinShift)
        {
            return null;
        }

        // Проверяем что хотя бы один игрок участвует
        if (attendingCount == 0)
        {
            return null;
        }

        // Для приоритетов с исключениями проверяем что действительно есть исключенные
        if (priority.ToString().Contains("ExcludeOne") && option.ExcludedPlayerIds.Count == 0)
        {
            return null;
        }

        // Для приоритетов без исключений проверяем что все участвуют
        if (priority.ToString().Contains("AllAttend") && option.ExcludedPlayerIds.Count > 0)
        {
            return null;
        }

        option.AttendingPlayersCount = attendingCount;

        return option;
    }
}