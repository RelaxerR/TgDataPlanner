using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TgDataPlanner.Data;
using TgDataPlanner.Data.Entities;
using TgDataPlanner.Services;

namespace TgDataPlanner.Telegram.Handlers;

/// <summary>
/// Базовый класс для обработчиков Telegram-событий.
/// Предоставляет общие методы для отправки сообщений, работы с конфигурацией и управления пользователями.
/// </summary>
public abstract class BaseHandler
{
    /// <summary>
    /// Клиент Telegram Bot API.
    /// </summary>
    protected readonly ITelegramBotClient BotClient;

    /// <summary>
    /// Идентификатор основного чата для системных уведомлений.
    /// </summary>
    protected readonly long MainChatId;

    /// <summary>
    /// Идентификатор администратора для проверки прав доступа.
    /// </summary>
    protected readonly long AdminId;

    /// <summary>
    /// Логгер для записи событий обработчика.
    /// </summary>
    protected readonly ILogger<BaseHandler> Logger;

    /// <summary>
    /// Ссылка на контекст базы данных для доступа к сущностям и сохранения изменений.
    /// </summary>
    protected readonly AppDbContext Db;

    /// <summary>
    /// Сервис управления пользователями для операций с игроками.
    /// </summary>
    protected readonly UserService UserService;

    /// <summary>
    /// Сервис планирования для поиска свободных временных окон.
    /// </summary>
    protected readonly SchedulingService SchedulingService;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="BaseHandler"/>.
    /// </summary>
    /// <param name="config">Конфигурация приложения.</param>
    /// <param name="botClient">Клиент Telegram Bot API.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    /// <param name="db">Контекст базы данных</param>
    /// <param name="userService">Сервис управления пользователями.</param>
    /// <param name="schedulingService">Сервис планирования.</param>
    protected BaseHandler(
        IConfiguration config,
        ITelegramBotClient botClient,
        ILogger<BaseHandler> logger,
        AppDbContext db,
        UserService userService,
        SchedulingService schedulingService)
    {
        BotClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Db = db ?? throw new ArgumentNullException(nameof(db));
        UserService = userService ?? throw new ArgumentNullException(nameof(userService));
        SchedulingService = schedulingService ?? throw new ArgumentNullException(nameof(schedulingService));

        if (!long.TryParse(config["TelegramBot:MainChatId"], out var mainChatId))
        {
            Logger.LogError("Не удалось распарсить MainChatId из конфигурации. Значение: {Value}", config["MainChatId"]);
        }
        MainChatId = mainChatId;

        if (!long.TryParse(config["TelegramBot:AdminId"], out var adminId))
        {
            Logger.LogError("Не удалось распарсить AdminId из конфигурации. Значение: {Value}", config["AdminId"]);
        }
        AdminId = adminId;
    }

    #region Методы отправки сообщений

    /// <summary>
    /// Отправляет текстовое сообщение в указанный чат.
    /// </summary>
    /// <param name="chatId">Идентификатор чата получателя.</param>
    /// <param name="text">Текст сообщения в формате Markdown.</param>
    /// <param name="replyMarkup">Необязательная разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача, представляющая отправленное сообщение.</returns>
    protected async Task<Message> SendTextAsync(
        long chatId,
        string text,
        ReplyMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        Logger.LogDebug("Отправка сообщения в чат {ChatId}: {TextPreview}", chatId, Truncate(text, 50));

        return await BotClient.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
    }

    /// <summary>
    /// Отправляет системное уведомление в основной чат.
    /// </summary>
    /// <param name="text">Текст уведомления.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения операции.</returns>
    protected async Task NotifyMainChatAsync(string text, CancellationToken ct = default)
    {
        if (MainChatId == 0)
        {
            Logger.LogWarning("Попытка отправить уведомление в MainChat, но MainChatId не настроен");
            return;
        }

        Logger.LogDebug("Системное уведомление в основной чат: {TextPreview}", Truncate(text, 50));

        await BotClient.SendMessage(
            chatId: MainChatId,
            text: $"🔔 {text}",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);
    }
    
    /// <summary>
    /// Отправляет уведомление каждому в группе
    /// </summary>
    /// <param name="group">Группа.</param>
    /// <param name="text">Текст уведомления.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения операции.</returns>
    protected async Task NotifyAllInGroupAsync(Group group, string text, CancellationToken ct = default)
    {
        var users = group.Players.Select(p => p.TelegramId).ToList();
        foreach (var user in users)
        {
            await BotClient.SendMessage(
                chatId: user,
                text: $"🔔 {text}",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
            
            Logger.LogDebug("Уведомление для [@{user}]: {TextPreview}", user, Truncate(text, 50));
        }
    }
    
    
    /// <summary>
    /// Отправляет уведомление каждому в группе
    /// </summary>
    /// <param name="group">Группа.</param>
    /// <param name="text">Текст уведомления.</param>
    /// <param name="replyMarkup">Необязательная новая разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения операции.</returns>
    protected async Task NotifyAllInGroupAsync(Group group, string text, InlineKeyboardMarkup? replyMarkup = null, CancellationToken ct = default)
    {
        var users = group.Players.Select(p => p.TelegramId).ToList();
        foreach (var user in users)
        {
            await BotClient.SendMessage(
                chatId: user,
                text: $"🔔 {text}",
                parseMode: ParseMode.Markdown,
                replyMarkup: replyMarkup,
                cancellationToken: ct);
            
            Logger.LogDebug("Уведомление для [@{user}]: {TextPreview}", user, Truncate(text, 50));
        }
    }

    /// <summary>
    /// Редактирует текст существующего сообщения (используется для кнопок).
    /// </summary>
    /// <param name="query">Запрос обратного вызова, содержащий сообщение.</param>
    /// <param name="text">Новый текст сообщения в формате Markdown.</param>
    /// <param name="replyMarkup">Необязательная новая разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения операции.</returns>
    protected async Task EditTextAsync(
        CallbackQuery query,
        string text,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        if (query.Message is null)
        {
            Logger.LogWarning("Попытка редактировать сообщение, но CallbackQuery.Message равен null");
            return;
        }

        Logger.LogDebug("Редактирование сообщения {MessageId} в чате {ChatId}", query.Message.MessageId, query.Message.Chat.Id);

        await BotClient.EditMessageText(
            chatId: query.Message.Chat.Id,
            messageId: query.Message.MessageId,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
    }

    /// <summary>
    /// Редактирует только клавиатуру сообщения (без изменения текста).
    /// </summary>
    /// <param name="query">Запрос обратного вызова, содержащий сообщение.</param>
    /// <param name="replyMarkup">Новая разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения операции.</returns>
    protected async Task EditReplyMarkupAsync(
        CallbackQuery query,
        InlineKeyboardMarkup? replyMarkup,
        CancellationToken ct = default)
    {
        if (query.Message is null)
        {
            Logger.LogWarning("Попытка редактировать клавиатуру, но CallbackQuery.Message равен null");
            return;
        }

        try
        {
            await BotClient.EditMessageReplyMarkup(
                chatId: query.Message.Chat.Id,
                messageId: query.Message.MessageId,
                replyMarkup: replyMarkup,
                cancellationToken: ct);
        }
        catch (ApiRequestException ex) 
        {
            // Игнорируем ошибку, если сообщение не изменилось
            if (ex.ErrorCode == 400 && ex.Message.Contains("message is not modified"))
            {
                return; // Просто выходим, не пишем в лог ошибку
            }
            throw; // Пробрасываем другие ошибки
        }
    }

    /// <summary>
    /// Отвечает на CallbackQuery, убирая индикатор загрузки в Telegram.
    /// </summary>
    /// <param name="callbackQuery">Запрос обратного вызова.</param>
    /// <param name="message">Необязательное сообщение для пользователя.</param>
    /// <param name="showAlert">Показывать ли сообщение как всплывающее уведомление.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения операции.</returns>
    protected async Task AnswerCallbackAsync(
        CallbackQuery callbackQuery,
        string? message = null,
        bool showAlert = false,
        CancellationToken ct = default)
    {
        await BotClient.AnswerCallbackQuery(
            callbackQueryId: callbackQuery.Id,
            text: message,
            showAlert: showAlert,
            cancellationToken: ct);
    }

    #endregion

    #region Методы работы с пользователями (делегируют UserService)

    /// <summary>
    /// Получает игрока по идентификатору Telegram или создаёт нового, если не найден.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="username">Имя пользователя (опционально).</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Объект игрока (существующий или ново созданный).</returns>
    protected Task<Player> GetOrCreatePlayerAsync(
        long telegramId,
        string? username = null,
        CancellationToken ct = default) =>
        UserService.GetOrCreatePlayerAsync(telegramId, username, ct);

    /// <summary>
    /// Получает игрока с загруженными связанными данными (группы, слоты).
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Объект игрока с загруженными коллекциями или null.</returns>
    protected Task<Player?> GetPlayerWithRelationsAsync(
        long telegramId,
        CancellationToken ct = default) =>
        UserService.GetPlayerWithRelationsAsync(telegramId, ct);

    /// <summary>
    /// Обновляет часовой пояс игрока.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="timeZoneOffset">Новое смещение часового пояса (UTC).</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>True, если обновление прошло успешно.</returns>
    protected Task<bool> UpdatePlayerTimeZoneAsync(
        long telegramId,
        int timeZoneOffset,
        CancellationToken ct = default) =>
        UserService.UpdateTimeZoneAsync(telegramId, timeZoneOffset, ct);

    /// <summary>
    /// Устанавливает или сбрасывает состояние машины состояний игрока.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="state">Новое состояние (null для сброса).</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>True, если состояние обновлено успешно.</returns>
    protected Task<bool> SetPlayerStateAsync(
        long telegramId,
        string? state,
        CancellationToken ct = default) =>
        UserService.SetPlayerStateAsync(telegramId, state, ct);

    /// <summary>
    /// Добавляет игрока в группу.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="groupId">Идентификатор группы.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>True, если игрок успешно добавлен; false, если уже состоит в группе.</returns>
    protected Task<bool> AddPlayerToGroupAsync(
        long telegramId,
        int groupId,
        CancellationToken ct = default) =>
        UserService.AddPlayerToGroupAsync(telegramId, groupId, ct);

    /// <summary>
    /// Удаляет игрока из группы.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="groupId">Идентификатор группы.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>True, если игрок успешно удалён из группы.</returns>
    protected Task<bool> RemovePlayerFromGroupAsync(
        long telegramId,
        int groupId,
        CancellationToken ct = default) =>
        UserService.RemovePlayerFromGroupAsync(telegramId, groupId, ct);

    /// <summary>
    /// Обновляет время последней активности игрока.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="ct">Токен отмены операции.</param>
    protected Task TouchPlayerActivityAsync(
        long telegramId,
        CancellationToken ct = default) =>
        UserService.TouchPlayerActivityAsync(telegramId, ct);

    /// <summary>
    /// Проверяет, является ли пользователь администратором.
    /// </summary>
    /// <param name="userId">Идентификатор пользователя Telegram.</param>
    /// <returns>True, если пользователь является администратором.</returns>
    protected bool IsAdmin(long userId) => userId == AdminId && AdminId != 0;

    #endregion

    #region Вспомогательные методы

    /// <summary>
    /// Обрезает строку до указанной длины для безопасного логирования.
    /// </summary>
    /// <param name="text">Исходная строка.</param>
    /// <param name="maxLength">Максимальная длина результата.</param>
    /// <returns>Обрезанная строка.</returns>
    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    /// <summary>
    /// Форматирует смещение часового пояса для отображения (например, +3, -5).
    /// </summary>
    /// <param name="offset">Смещение в часах.</param>
    /// <returns>Строка формата "+H" или "-H".</returns>
    protected static string FormatTimeZoneOffset(int offset) =>
        $"{(offset >= 0 ? "+" : "")}{offset}";

    /// <summary>
    /// Конвертирует время из UTC в локальное время пользователя.
    /// </summary>
    /// <param name="utcDateTime">Время в UTC.</param>
    /// <param name="timeZoneOffset">Смещение часового пояса пользователя.</param>
    /// <returns>Локальное время пользователя.</returns>
    protected static DateTime ConvertUtcToLocal(DateTime utcDateTime, int timeZoneOffset) =>
        utcDateTime.AddHours(timeZoneOffset);

    /// <summary>
    /// Конвертирует локальное время пользователя в UTC.
    /// </summary>
    /// <param name="localDateTime">Локальное время пользователя.</param>
    /// <param name="timeZoneOffset">Смещение часового пояса пользователя.</param>
    /// <returns>Время в UTC.</returns>
    protected static DateTime ConvertLocalToUtc(DateTime localDateTime, int timeZoneOffset) =>
        localDateTime.AddHours(-timeZoneOffset);

    #endregion
}