namespace TgDataPlanner.Services;

using System;
using System.Collections.Generic;
using Common;

/// <summary>
/// Модель данных о доступности игрока для сервиса рекомендаций.
/// </summary>
public class PlayerAvailability
{
    /// <summary>
    /// Идентификатор игрока (Telegram ChatId или UserId)
    /// </summary>
    public long PlayerId { get; init; }

    /// <summary>
    /// Имя игрока для отображения в рекомендациях
    /// </summary>
    public required string PlayerName { get; init; }

    /// <summary>
    /// Список доступных временных интервалов для игрока
    /// </summary>
    public List<TimeSlot> AvailableSlots { get; init; } = [];

    /// <summary>
    /// Оригинальное предпочтительное время начала (если указано игроком)
    /// </summary>
    public DateTime? PreferredStartTime { get; init; }
}

/// <summary>
/// Модель временного интервала доступности.
/// </summary>
public class TimeSlot
{
    /// <summary>
    /// Начало доступного интервала
    /// </summary>
    public DateTime Start { get; init; }

    /// <summary>
    /// Конец доступного интервала
    /// </summary>
    public DateTime End { get; init; }

    /// <summary>
    /// Проверяет, пересекается ли этот слот с указанным временным диапазоном
    /// </summary>
    public bool IntersectsWith(DateTime rangeStart, DateTime rangeEnd)
    {
        return Start <= rangeEnd && End >= rangeStart;
    }

    /// <summary>
    /// Проверяет, полностью ли покрывает этот слот указанный временной диапазон
    /// </summary>
    public bool FullyCovers(DateTime rangeStart, DateTime rangeEnd)
    {
        return Start <= rangeStart && End >= rangeEnd;
    }

    /// <summary>
    /// Возвращает длительность слота в часах
    /// </summary>
    public double DurationHours
    {
        get => (End - Start).TotalHours;
    }
}

/// <summary>
/// Интерфейс сервиса рекомендаций для планирования игровых сессий.
/// Используется когда прямое пересечение доступности всех игроков не найдено.
/// </summary>
public interface IRecommendationService
{
    /// <summary>
    /// Находит варианты планирования сессии с базовыми параметрами поиска.
    /// </summary>
    /// <param name="players">Список игроков с их доступностью</param>
    /// <param name="sessionDurationHours">Требуемая длительность сессии в часах</param>
    /// <returns>Результат с отсортированными вариантами рекомендаций</returns>
    RecommendationResult FindRecommendations(
        IEnumerable<PlayerAvailability> players,
        double sessionDurationHours);

    /// <summary>
    /// Проверяет, может ли указанный игрок присутствовать в предложенное время
    /// с учетом допустимого сдвига от его предпочтений.
    /// </summary>
    /// <param name="player">Данные игрока</param>
    /// <param name="proposedStart">Предлагаемое время начала</param>
    /// <param name="proposedEnd">Предлагаемое время окончания</param>
    /// <param name="maxShiftHours">Максимально допустимый сдвиг в часах</param>
    /// <returns>
    /// Tuple: Item1 - может ли присутствовать, Item2 - фактический сдвиг в часах
    /// </returns>
    (bool CanAttend, double TimeShift) CheckPlayerAvailability(
        PlayerAvailability player,
        DateTime proposedStart,
        DateTime proposedEnd,
        double maxShiftHours);

    /// <summary>
    /// Рассчитывает сдвиг времени для игрока относительно его предпочтений.
    /// </summary>
    /// <param name="player">Данные игрока</param>
    /// <param name="proposedStart">Предлагаемое время начала</param>
    /// <returns>Сдвиг в часах (положительное - позже, отрицательное - раньше)</returns>
    double CalculateTimeShift(PlayerAvailability player, DateTime proposedStart);
}