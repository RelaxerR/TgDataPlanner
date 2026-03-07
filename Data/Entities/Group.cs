using System.ComponentModel.DataAnnotations;
using TgDataPlanner.Common;

namespace TgDataPlanner.Data.Entities;

/// <summary>
/// Представляет игровую группу (кампанию) для планирования сессий.
/// Содержит список игроков и настройки состояния группы.
/// </summary>
public sealed class Group
{
    /// <summary>
    /// Уникальный идентификатор группы в базе данных.
    /// </summary>
    [Key]
    public int Id { get; init; }

    /// <summary>
    /// Отображаемое название группы (например, "Кампания Драконьего Копья").
    /// </summary>
    /// <remarks>
    /// Может быть пустым, но не <c>null</c>. Максимальная длина — 100 символов.
    /// </remarks>
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Название группы должно содержать от 1 до 100 символов")]
    public string? Name { get; init; } = string.Empty;

    /// <summary>
    /// Идентификатор чата Telegram, к которому привязана группа.
    /// Используется для отправки уведомлений и команд в нужный чат.
    /// </summary>
    [Required]
    public long TelegramChatId { get; init; }

    /// <summary>
    /// Текущее состояние группы в жизненном цикле планирования.
    /// По умолчанию — <see cref="GroupState.Idle"/>.
    /// </summary>
    public GroupState State { get; set; } = GroupState.Idle;

    /// <summary>
    /// Порядок выбора группы в интерфейсе (для сортировки при переключении).
    /// Меньшее значение означает более высокий приоритет отображения.
    /// </summary>
    public int SelectionOrder { get; init; }

    /// <summary>
    /// Дата и время последней проведённой игровой сессии.
    /// Используется для аналитики и рекомендаций по планированию.
    /// </summary>
    public DateTime? LastGameDate { get; set; }

    /// <summary>
    /// Дата и время создания записи группы.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Дата и время последнего обновления записи.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Коллекция игроков, состоящих в данной группе.
    /// Настраивается как связь многие-ко-многим с <see cref="Player"/>.
    /// </summary>
    public List<Player> Players { get; init; } = [];

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Group"/>.
    /// </summary>
    public Group() { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Group"/> с заданными параметрами.
    /// </summary>
    /// <param name="name">Название группы.</param>
    /// <param name="telegramChatId">Идентификатор чата Telegram.</param>
    public Group(string name, long telegramChatId)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TelegramChatId = telegramChatId;
    }

    /// <summary>
    /// Обновляет метаданные группы: состояние, дату последней игры и время обновления.
    /// </summary>
    /// <param name="newState">Новое состояние группы.</param>
    /// <param name="lastGameDate">Дата последней игры (опционально).</param>
    public void UpdateMetadata(GroupState newState, DateTime? lastGameDate = null)
    {
        State = newState;
        LastGameDate = lastGameDate ?? LastGameDate;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Возвращает строковое представление группы для отладки.
    /// </summary>
    /// <returns>Строка формата "Group[{Id}]: {Name}"</returns>
    public override string ToString() =>
        $"Group[{Id}]: '{Name}' (Chat={TelegramChatId}, Players={Players.Count}, State={State})";
}