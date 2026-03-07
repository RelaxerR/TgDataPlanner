using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TgDataPlanner.Data.Entities;

/// <summary>
/// Представляет слот доступности игрока — конкретный час, когда он свободен для игры.
/// Используется для построения расписания и поиска общих окон в группе.
/// </summary>
public sealed class AvailabilitySlot
{
    /// <summary>
    /// Уникальный идентификатор слота в базе данных.
    /// </summary>
    [Key]
    public int Id { get; init; }

    /// <summary>
    /// Идентификатор игрока, которому принадлежит слот (внешний ключ на <see cref="Player.TelegramId"/>).
    /// </summary>
    [Required]
    public long PlayerId { get; init; }

    /// <summary>
    /// Навигационное свойство для связи с игроком.
    /// </summary>
    [ForeignKey(nameof(PlayerId))]
    public Player Player { get; init; } = null!;

    /// <summary>
    /// Дата и время слота в формате UTC.
    /// Всегда хранится в UTC для корректной работы с часовыми поясами.
    /// </summary>
    /// <remarks>
    /// Минимальная гранулярность — 1 час. Время должно быть установлено на начало часа (мм=0, сс=0).
    /// </remarks>
    [Required]
    [Column(TypeName = "datetime")]
    public DateTime DateTimeUtc { get; init; }

    /// <summary>
    /// Флаг, указывающий, что слот является частью повторяющегося недельного расписания.
    /// Если <c>true</c>, слот автоматически применяется к соответствующему дню недели в будущих неделях.
    /// </summary>
    public bool IsWeeklyPermanent { get; init; }

    /// <summary>
    /// Дата и время создания записи в базе данных.
    /// Заполняется автоматически при добавлении.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="AvailabilitySlot"/>.
    /// </summary>
    public AvailabilitySlot() { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="AvailabilitySlot"/> с заданными параметрами.
    /// </summary>
    /// <param name="playerId">Идентификатор игрока.</param>
    /// <param name="dateTimeUtc">Дата и время слота в UTC.</param>
    /// <param name="isWeeklyPermanent">Флаг повторяемости слота.</param>
    public AvailabilitySlot(long playerId, DateTime dateTimeUtc, bool isWeeklyPermanent = false)
    {
        PlayerId = playerId;
        DateTimeUtc = NormalizeToHourStart(dateTimeUtc);
        IsWeeklyPermanent = isWeeklyPermanent;
    }

    /// <summary>
    /// Приводит дату и время к началу часа (обнуляет минуты, секунды, миллисекунды).
    /// </summary>
    /// <param name="dateTime">Исходная дата и время.</param>
    /// <returns>Дата и время, приведённая к началу часа.</returns>
    private static DateTime NormalizeToHourStart(DateTime dateTime) =>
        new(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Возвращает строковое представление слота для отладки.
    /// </summary>
    /// <returns>Строка формата "PlayerId: {Id}, Time: {DateTimeUtc:yyyy-MM-dd HH:mm}"</returns>
    public override string ToString() =>
        $"Slot[Player={PlayerId}, Time={DateTimeUtc:yyyy-MM-dd HH:mm}, Weekly={IsWeeklyPermanent}]";
}