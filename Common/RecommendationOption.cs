namespace TgDataPlanner.Common;
using System;
using System.Collections.Generic;

/// <summary>
/// Модель одного варианта рекомендации для планирования сессии.
/// </summary>
public class RecommendationOption
{
    /// <summary>
    /// Предлагаемое время начала сессии
    /// </summary>
    public DateTime ProposedStartTime { get; set; }

    /// <summary>
    /// Предлагаемое время окончания сессии
    /// </summary>
    public DateTime ProposedEndTime { get; set; }

    /// <summary>
    /// Приоритет данного варианта рекомендации
    /// </summary>
    public RecommendationPriority Priority { get; set; }

    /// <summary>
    /// Список идентификаторов игроков, которые не смогут присутствовать в этом варианте
    /// (пустой список означает, что все игроки участвуют)
    /// </summary>
    public List<long> ExcludedPlayerIds { get; set; } = new List<long>();

    /// <summary>
    /// Информация о сдвиге времени для каждого игрока относительно его исходных предпочтений.
    /// Ключ: PlayerId, Значение: сдвиг в часах (положительное - позже, отрицательное - раньше)
    /// </summary>
    public Dictionary<long, double> PlayerTimeShifts { get; set; } = new Dictionary<long, double>();

    /// <summary>
    /// Максимальный сдвиг времени среди всех участвующих игроков в этом варианте
    /// </summary>
    public double MaxTimeShift => PlayerTimeShifts.Count > 0 ? PlayerTimeShifts.Values.Max() : 0;

    /// <summary>
    /// Количество игроков, которые смогут присутствовать в этом варианте
    /// </summary>
    public int AttendingPlayersCount { get; set; }

    /// <summary>
    /// Общее количество игроков в сессии
    /// </summary>
    public int TotalPlayersCount { get; set; }

    /// <summary>
    /// Процент игроков, которые смогут присутствовать в этом варианте
    /// </summary>
    public double AttendancePercentage => TotalPlayersCount > 0
        ? (double)AttendingPlayersCount / TotalPlayersCount * 100
        : 0;

    /// <summary>
    /// Список имён игроков, которые смогут присутствовать в этом варианте
    /// </summary>
    public List<string> AttendingPlayerNames { get; set; } = new List<string>();

    /// <summary>
    /// Возвращает человекочитаемое описание приоритета
    /// </summary>
    public string GetPriorityDescription()
    {
        return Priority switch
        {
            RecommendationPriority.AllAttendShift1h => "Все участвуют, сдвиг до 1 часа",
            RecommendationPriority.ExcludeOneNoShift => "Один не участвует, без сдвига времени",
            RecommendationPriority.AllAttendShift2h => "Все участвуют, сдвиг до 2 часов",
            RecommendationPriority.ExcludeOneShift1h => "Один не участвует, сдвиг до 1 часа",
            RecommendationPriority.AllAttendShift3h => "Все участвуют, сдвиг до 3 часов",
            RecommendationPriority.ExcludeOneShift2h => "Один не участвует, сдвиг до 2 часов",
            _ => "Неизвестный приоритет"
        };
    }

    /// <summary>
    /// Возвращает человекочитаемое описание сдвигов для игроков
    /// </summary>
    public string GetShiftsDescription()
    {
        if (PlayerTimeShifts.Count == 0)
        {
            return "Без сдвигов";
        }

        var shifts = new List<string>();
        foreach (var shift in PlayerTimeShifts)
        {
            if (shift.Value == 0)
            {
                shifts.Add($"Игрок {shift.Key}: без сдвига");
            }
            else if (shift.Value > 0)
            {
                shifts.Add($"Игрок {shift.Key}: +{shift.Value:F1} ч.");
            }
            else
            {
                shifts.Add($"Игрок {shift.Key}: {shift.Value:F1} ч.");
            }
        }

        return string.Join(", ", shifts);
    }

    /// <summary>
    /// Возвращает список присутствующих игроков в формате Markdown
    /// </summary>
    public string GetAttendingPlayersMarkdown()
    {
        if (AttendingPlayerNames.Count == 0)
        {
            return "Нет участников";
        }
        // Экранируем спецсимволы Markdown в именах пользователей
        return string.Join(", ", AttendingPlayerNames.Select(name =>
        {
            var escapedName = name.Replace("_", "\\_").Replace("*", "\\*").Replace("`", "\\`").Replace("[", "\\[");
            return $"@{escapedName}";
        }));
    }
}