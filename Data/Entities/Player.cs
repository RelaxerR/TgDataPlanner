using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace TgDataPlanner.Data.Entities;

/// <summary>
/// Представляет игрока (пользователя Telegram), участвующего в планировании игровых сессий.
/// Содержит настройки часового пояса, состояние и связи с группами и слотами доступности.
/// </summary>
public partial class Player
{
    /// <summary>
    /// Константы валидации и конфигурации.
    /// </summary>
    private static partial class Constraints
    {
        public const int UsernameMinLength = 1;
        public const int UsernameMaxLength = 32;
        public const int MinTimeZoneOffset = -12;
        public const int MaxTimeZoneOffset = +14;
        public static readonly Regex UsernamePattern = MyRegex();

        [GeneratedRegex(@"^[a-zA-Z0-9_]{3,32}$", RegexOptions.Compiled)]
        private static partial Regex MyRegex();
    }

    /// <summary>
    /// Уникальный идентификатор игрока — соответствует <see cref="Telegram.Bot.Types.User.Id"/>.
    /// Используется как первичный ключ в базе данных.
    /// </summary>
    [Key]
    public long TelegramId { get; init; }

    /// <summary>
    /// Имя пользователя Telegram (username) без символа '@'.
    /// Используется для отображения в интерфейсе бота.
    /// </summary>
    /// <remarks>
    /// Допускаются латинские буквы, цифры и подчёркивания. Длина: 3-32 символа.
    /// Если имя не задано, используется значение "Unknown".
    /// </remarks>
    [Required]
    [StringLength(Constraints.UsernameMaxLength, MinimumLength = Constraints.UsernameMinLength)]
    public string Username { get; set; } = "Unknown";

    /// <summary>
    /// Смещение часового пояса игрока относительно UTC в часах.
    /// Диапазон допустимых значений: от -12 до +14.
    /// </summary>
    /// <example>
    /// Москва: +3, Екатеринбург: +5, Нью-Йорк: -5
    /// </example>
    [Range(Constraints.MinTimeZoneOffset, Constraints.MaxTimeZoneOffset)]
    public int TimeZoneOffset { get; set; }

    /// <summary>
    /// Текущее состояние машины состояний игрока в диалоге с ботом.
    /// Используется для обработки многошаговых команд (например, создание группы).
    /// </summary>
    /// <remarks>
    /// Примеры значений: "AwaitingGroupName", "SelectingDate", null — нет активного состояния.
    /// </remarks>
    [StringLength(50)]
    public string? CurrentState { get; set; }

    /// <summary>
    /// Дата и время регистрации игрока в системе.
    /// </summary>
    public DateTime RegisteredAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Дата и время последнего взаимодействия с ботом.
    /// Обновляется при каждой обработке команды от игрока.
    /// </summary>
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Коллекция групп, в которых состоит игрок.
    /// Настраивается как связь многие-ко-многим с <see cref="Group"/>.
    /// </summary>
    public virtual List<Group> Groups { get; init; } = [];

    /// <summary>
    /// Коллекция слотов доступности игрока.
    /// Каждый слот представляет один час, когда игрок свободен.
    /// </summary>
    public virtual List<AvailabilitySlot> Slots { get; init; } = [];

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Player"/>.
    /// </summary>
    public Player() { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Player"/> с заданными параметрами.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="username">Имя пользователя (опционально).</param>
    /// <param name="timeZoneOffset">Смещение часового пояса (по умолчанию 0).</param>
    public Player(long telegramId, string? username = null, int timeZoneOffset = 0)
    {
        TelegramId = telegramId;
        Username = SanitizeUsername(username);
        TimeZoneOffset = ClampTimeZoneOffset(timeZoneOffset);
        RegisteredAt = DateTime.UtcNow;
        LastActivityAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Обновляет время последней активности игрока.
    /// Следует вызывать при каждой успешной обработке команды от пользователя.
    /// </summary>
    public void TouchActivity() => LastActivityAt = DateTime.UtcNow;

    /// <summary>
    /// Проверяет, состоит ли игрок в указанной группе.
    /// </summary>
    /// <param name="groupId">Идентификатор группы для проверки.</param>
    /// <returns>True, если игрок является участником группы.</returns>
    public bool IsMemberOfGroup(int groupId) =>
        Groups.Any(g => g.Id == groupId);

    /// <summary>
    /// Очищает и валидирует имя пользователя перед сохранением.
    /// </summary>
    /// <param name="username">Исходное имя пользователя.</param>
    /// <returns>Безопасное имя или "Unknown", если имя недопустимо.</returns>
    private static string SanitizeUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return "Unknown";

        // Удаляем ведущий '@', если присутствует
        var cleaned = username.TrimStart('@').Trim();

        // Проверяем соответствие шаблону
        return Constraints.UsernamePattern.IsMatch(cleaned) ? cleaned : "Unknown";
    }

    /// <summary>
    /// Ограничивает значение часового пояса допустимым диапазоном.
    /// </summary>
    /// <param name="offset">Исходное смещение.</param>
    /// <returns>Смещение в диапазоне [{Min}, {Max}].</returns>
    private static int ClampTimeZoneOffset(int offset) =>
        Math.Max(Constraints.MinTimeZoneOffset, Math.Min(Constraints.MaxTimeZoneOffset, offset));

    /// <summary>
    /// Возвращает строковое представление игрока для отладки.
    /// </summary>
    /// <returns>Строка формата "Player[{TelegramId}]: {Username}"</returns>
    public override string ToString() =>
        $"Player[{TelegramId}]: '{Username}' (TZ={TimeZoneOffset:+#;-#;0}, Groups={Groups.Count}, Slots={Slots.Count})";
}