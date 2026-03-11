namespace TgDataPlanner.Common;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Результат работы сервиса рекомендаций.
/// Содержит отсортированный список вариантов рекомендаций по приоритету.
/// </summary>
public class RecommendationResult
{
    /// <summary>
    /// Флаг, указывающий были ли найдены какие-либо варианты рекомендаций
    /// </summary>
    public bool HasRecommendations { get; set; }

    /// <summary>
    /// Список вариантов рекомендаций, отсортированный по приоритету (от лучшего к худшему)
    /// </summary>
    public List<RecommendationOption> Options { get; set; } = new List<RecommendationOption>();

    /// <summary>
    /// Количество найденных вариантов
    /// </summary>
    public int OptionsCount => Options.Count;

    /// <summary>
    /// Возвращает лучший вариант рекомендации (с наивысшим приоритетом)
    /// </summary>
    public RecommendationOption GetBestOption()
    {
        return Options.FirstOrDefault();
    }

    /// <summary>
    /// Возвращает варианты рекомендаций указанного приоритета
    /// </summary>
    public List<RecommendationOption> GetOptionsByPriority(RecommendationPriority priority)
    {
        return Options.Where(o => o.Priority == priority).ToList();
    }

    /// <summary>
    /// Возвращает варианты, где участвуют все игроки (без исключений)
    /// </summary>
    public List<RecommendationOption> GetAllAttendOptions()
    {
        return Options.Where(o => o.ExcludedPlayerIds.Count == 0).ToList();
    }

    /// <summary>
    /// Возвращает варианты, где хотя бы один игрок исключен
    /// </summary>
    public List<RecommendationOption> GetExcludedPlayerOptions()
    {
        return Options.Where(o => o.ExcludedPlayerIds.Count > 0).ToList();
    }

    /// <summary>
    /// Добавляет вариант рекомендации в список и сортирует по приоритету
    /// </summary>
    public void AddOption(RecommendationOption option)
    {
        Options.Add(option);
        Options = Options.OrderBy(o => o.Priority).ThenBy(o => o.MaxTimeShift).ToList();
        HasRecommendations = Options.Count > 0;
    }

    /// <summary>
    /// Добавляет несколько вариантов рекомендаций и сортирует по приоритету
    /// </summary>
    public void AddOptions(IEnumerable<RecommendationOption> options)
    {
        Options.AddRange(options);
        Options = Options.OrderBy(o => o.Priority).ThenBy(o => o.MaxTimeShift).ToList();
        HasRecommendations = Options.Count > 0;
    }
}