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
    /// Дата и время следующей запланированной сессии в UTC.
    /// null = сессия не назначена.
    /// </summary>
    public DateTime? CurrentSessionUtc { get; set; }

    /// <summary>
    /// Текущий статус сессии группы.
    /// </summary>
    /// <remarks>
    /// NoSession → Pending → Confirmed/Cancelled/Rescheduled
    /// </remarks>
    public SessionStatus SessionStatus { get; set; } = SessionStatus.NoSession;

    /// <summary>
    /// Список TelegramId игроков, подтвердивших участие (RSVP Yes).
    /// Сбрасывается при изменении времени сессии.
    /// </summary>
    public List<long> ConfirmedPlayerIds { get; init; } = [];

    /// <summary>
    /// Список TelegramId игроков, отказавшихся от участия (RSVP No).
    /// Сбрасывается при изменении времени сессии.
    /// </summary>
    public List<long> DeclinedPlayerIds { get; init; } = [];

    /// <summary>
    /// Список TelegramId игроков, нажавших кнопку "Завершить заполнение" при сборе расписания.
    /// Используется для отслеживания готовности группы к авто-планированию.
    /// </summary>
    public List<long> FinishedVotingPlayerIds { get; init; } = [];

    /// <summary>
    /// Список TelegramId администраторов, состоящих в данной группе.
    /// </summary>
    public List<long> AdminIds { get; init; } = [];

    /// <summary>
    /// Порядок выбора группы в интерфейсе (для сортировки при переключении).
    /// Меньшее значение означает более высокий приоритет отображения.
    /// </summary>
    public int SelectionOrder { get; init; }

    /// <summary>
    /// Дата и время последней проведённой игровой сессии.
    /// Используется для аналитики и рекомендаций по планированию.
    /// </summary>
    public DateTime? LastGameDate { get; init; }

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
    public Group()
    {
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Group"/> с заданными параметрами.
    /// </summary>
    /// <param name="name">Название группы.</param>
    /// <param name="telegramChatId">Идентификатор чата Telegram.</param>
    public Group(string name, long telegramChatId)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TelegramChatId = telegramChatId;
        SessionStatus = SessionStatus.NoSession;
    }

    /// <summary>
    /// Обновляет статус сессии и метаданные группы.
    /// </summary>
    /// <param name="newStatus">Новый статус сессии.</param>
    /// <param name="sessionUtc">Время сессии (опционально).</param>
    public void UpdateSessionStatus(SessionStatus newStatus, DateTime? sessionUtc = null)
    {
        SessionStatus = newStatus;
        CurrentSessionUtc = sessionUtc ?? CurrentSessionUtc;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Проверяет, находится ли группа в состоянии ожидания RSVP.
    /// </summary>
    public bool IsAwaitingRSVP() => SessionStatus == SessionStatus.Pending;

    /// <summary>
    /// Проверяет, подтверждена ли сессия.
    /// </summary>
    public bool IsSessionConfirmed() => SessionStatus == SessionStatus.Confirmed;

    /// <summary>
    /// Проверяет, есть ли у группы назначенная сессия.
    /// </summary>
    public bool HasSession() => CurrentSessionUtc.HasValue;

    /// <summary>
    /// Сбрасывает данные голосования при изменении состава группы или новом запросе.
    /// </summary>
    public void ResetVotingData()
    {
        FinishedVotingPlayerIds.Clear();
        ConfirmedPlayerIds.Clear();
        DeclinedPlayerIds.Clear();
        CurrentSessionUtc = null;
        SessionStatus = SessionStatus.NoSession;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Возвращает строковое представление группы для отладки.
    /// </summary>
    /// <returns>Строка формата "Group[{Id}]: {Name}"</returns>
    public override string ToString() =>
        $"Group[{Id}]: '{Name}' (Chat={TelegramChatId}, Players={Players.Count}, Status={SessionStatus}, Session={CurrentSessionUtc?.ToString("dd.MM HH:mm") ?? "null"})";
}