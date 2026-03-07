namespace TgDataPlanner.Common;

/// <summary>
/// Статистика по игрокам системы.
/// </summary>
public record PlayerStatistics
{
    /// <summary>
    /// Общее количество зарегистрированных игроков.
    /// </summary>
    public int TotalPlayers { get; init; }

    /// <summary>
    /// Количество игроков, заполнивших хотя бы один слот доступности.
    /// </summary>
    public int PlayersWithSlots { get; init; }

    /// <summary>
    /// Количество игроков, состоящих хотя бы в одной группе.
    /// </summary>
    public int PlayersInGroups { get; init; }

    /// <summary>
    /// Количество игроков, проявивших активность за последние 24 часа.
    /// </summary>
    public int ActiveLast24Hours { get; init; }

    /// <summary>
    /// Процент заполнения слотов (игроки со слотами / всего игроков).
    /// </summary>
    public double SlotFillRate
    {
        get => TotalPlayers > 0 ? (double)PlayersWithSlots / TotalPlayers : 0.0;
    }

    /// <summary>
    /// Процент игроков в группах.
    /// </summary>
    public double GroupJoinRate
    {
        get => TotalPlayers > 0 ? (double)PlayersInGroups / TotalPlayers : 0.0;
    }
}