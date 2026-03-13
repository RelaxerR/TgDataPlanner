using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TgDataPlanner.AI;
using TgDataPlanner.Common;
using TgDataPlanner.Data;
using TgDataPlanner.Data.Entities;
using TgDataPlanner.Services;
using TgDataPlanner.Configuration;

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
    private readonly ITelegramBotClient BotClient;

    /// <summary>
    /// Идентификатор основного чата для системных уведомлений.
    /// </summary>
    private readonly long MainChatId;

    /// <summary>
    /// Список идентификаторов администраторов для проверки прав доступа.
    /// </summary>
    protected readonly List<long> AdminIds;

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
    /// Сервис ИИ
    /// </summary>
    protected readonly OllamaService OllamaService;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="BaseHandler"/>.
    /// </summary>
    /// <param name="config">Конфигурация приложения.</param>
    /// <param name="botClient">Клиент Telegram Bot API.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    /// <param name="db">Контекст базы данных</param>
    /// <param name="userService">Сервис управления пользователями.</param>
    /// <param name="schedulingService">Сервис планирования.</param>
    /// <param name="ollamaService">Сервис ИИ</param>
    protected BaseHandler(
        IConfiguration config,
        ITelegramBotClient botClient,
        ILogger<BaseHandler> logger,
        AppDbContext db,
        UserService userService,
        SchedulingService schedulingService,
        OllamaService ollamaService)
    {
        BotClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Db = db ?? throw new ArgumentNullException(nameof(db));
        UserService = userService ?? throw new ArgumentNullException(nameof(userService));
        SchedulingService = schedulingService ?? throw new ArgumentNullException(nameof(schedulingService));
        OllamaService = ollamaService ?? throw new ArgumentNullException(nameof(ollamaService));

        if (!long.TryParse(config["TelegramBot:MainChatId"], out var mainChatId))
        {
            Logger.LogError(BotConstants.SystemMessages.MainChatIdParseFailed, config["TelegramBot:MainChatId"]);
        }
        MainChatId = mainChatId;

        // Парсим список администраторов через запятую
        AdminIds = [];
        var adminIdsConfig = config["TelegramBot:AdminIds"];
        if (!string.IsNullOrWhiteSpace(adminIdsConfig))
        {
            var adminIdStrings = adminIdsConfig.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var adminIdString in adminIdStrings)
            {
                if (long.TryParse(adminIdString.Trim(), out var adminId))
                {
                    AdminIds.Add(adminId);
                }
                else
                {
                    Logger.LogError(BotConstants.SystemMessages.AdminIdParseFailed, adminIdString);
                }
            }
        }
        else
        {
            Logger.LogError(BotConstants.SystemMessages.AdminIdsNotConfigured);
        }
    }

    #region Методы отправки сообщений пользователю

    /// <summary>
    /// Отправляет текстовое сообщение конкретному пользователю по его Telegram ID.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="text">Текст сообщения в формате Markdown.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача, представляющая отправленное сообщение.</returns>
    protected async Task<Message> SendToUserAsync(
        long telegramId,
        string text,
        CancellationToken ct = default)
    {
        return await SendToUserAsync(telegramId, text, null, ct);
    }

    /// <summary>
    /// Отправляет текстовое сообщение конкретному пользователю по его Telegram ID с клавиатурой.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="text">Текст сообщения в формате Markdown.</param>
    /// <param name="replyMarkup">Разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача, представляющая отправленное сообщение.</returns>
    protected async Task<Message> SendToUserAsync(
        long telegramId,
        string text,
        ReplyMarkup? replyMarkup,
        CancellationToken ct = default)
    {
        Logger.LogDebug(BotConstants.SystemMessages.SendMessageToUser, telegramId, Truncate(text, 50));
        return await BotClient.SendMessage(
            chatId: telegramId,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
    }

    #endregion

    #region Методы отправки сообщений в чат группы

    /// <summary>
    /// Отправляет текстовое сообщение в чат группы.
    /// </summary>
    /// <param name="chatId">Идентификатор чата группы.</param>
    /// <param name="text">Текст сообщения в формате Markdown.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача, представляющая отправленное сообщение.</returns>
    protected async Task<Message> SendToGroupChatAsync(
        long chatId,
        string text,
        CancellationToken ct = default)
    {
        return await SendToGroupChatAsync(chatId, text, null, ct);
    }

    /// <summary>
    /// Отправляет текстовое сообщение в чат группы с клавиатурой.
    /// </summary>
    /// <param name="chatId">Идентификатор чата группы.</param>
    /// <param name="text">Текст сообщения в формате Markdown.</param>
    /// <param name="replyMarkup">Разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача, представляющая отправленное сообщение.</returns>
    protected async Task<Message> SendToGroupChatAsync(
        long chatId,
        string text,
        ReplyMarkup? replyMarkup,
        CancellationToken ct = default)
    {
        Logger.LogDebug(BotConstants.SystemMessages.SendMessageToGroup, chatId, Truncate(text, 50));
        return await BotClient.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
    }

    #endregion

    #region Методы отправки сообщений всем игрокам в группе

    /// <summary>
    /// Отправляет уведомление каждому игроку в группе (в личные сообщения).
    /// </summary>
    /// <param name="group">Группа, игрокам которой отправляется сообщение.</param>
    /// <param name="text">Текст уведомления в формате Markdown.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения операции.</returns>
    protected async Task NotifyAllInGroupAsync(
        Group group,
        string text,
        CancellationToken ct = default)
    {
        await NotifyAllInGroupAsync(group, text, null, ct);
    }

    /// <summary>
    /// Отправляет уведомление каждому игроку в группе (в личные сообщения) с клавиатурой.
    /// </summary>
    /// <param name="group">Группа, игрокам которой отправляется сообщение.</param>
    /// <param name="text">Текст уведомления в формате Markdown.</param>
    /// <param name="replyMarkup">Разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения операции.</returns>
    private async Task NotifyAllInGroupAsync(
        Group group,
        string text,
        InlineKeyboardMarkup? replyMarkup,
        CancellationToken ct = default)
    {
        var users = group.Players.Select(p => p.TelegramId).ToList();
        foreach (var userId in users)
        {
            try
            {
                await BotClient.SendMessage(
                    chatId: userId,
                    text: text,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: replyMarkup,
                    cancellationToken: ct);
                Logger.LogDebug(BotConstants.SystemMessages.NotifyUser, userId, Truncate(text, 50));
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, BotConstants.SystemMessages.NotifyUserFailed, userId);
            }
        }
    }

    #endregion

    #region Методы отправки сообщений главному администратору

    /// <summary>
    /// Отправляет сообщение главному администратору (первому в списке AdminIds).
    /// </summary>
    /// <param name="text">Текст сообщения в формате Markdown.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача, представляющая отправленное сообщение.</returns>
    protected async Task<Message> SendToMainAdminAsync(
        string text,
        CancellationToken ct = default)
    {
        return await SendToMainAdminAsync(text, null, ct);
    }

    /// <summary>
    /// Отправляет сообщение главному администратору (первому в списке AdminIds) с клавиатурой.
    /// </summary>
    /// <param name="text">Текст сообщения в формате Markdown.</param>
    /// <param name="replyMarkup">Разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача, представляющая отправленное сообщение.</returns>
    private async Task<Message> SendToMainAdminAsync(
        string text,
        ReplyMarkup? replyMarkup,
        CancellationToken ct = default)
    {
        var mainAdminId = AdminIds.FirstOrDefault();
        if (mainAdminId == 0)
        {
            Logger.LogWarning(BotConstants.SystemMessages.MainAdminNotConfigured);
            return null!;
        }

        Logger.LogDebug(BotConstants.SystemMessages.SendMessageToMainAdmin, mainAdminId, Truncate(text, 50));
        return await BotClient.SendMessage(
            chatId: mainAdminId,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
    }

    #endregion

    #region Методы отправки сообщений всем администраторам

    /// <summary>
    /// Отправляет сообщение всем администраторам (в личные сообщения).
    /// </summary>
    /// <param name="text">Текст сообщения в формате Markdown.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения операции.</returns>
    protected async Task NotifyAllAdminsAsync(
        string text,
        CancellationToken ct = default)
    {
        await NotifyAllAdminsAsync(text, null, ct);
    }

    /// <summary>
    /// Отправляет сообщение всем администраторам (в личные сообщения) с клавиатурой.
    /// </summary>
    /// <param name="text">Текст сообщения в формате Markdown.</param>
    /// <param name="replyMarkup">Разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения операции.</returns>
    private async Task NotifyAllAdminsAsync(
        string text,
        ReplyMarkup? replyMarkup,
        CancellationToken ct = default)
    {
        if (AdminIds.Count == 0)
        {
            Logger.LogWarning(BotConstants.SystemMessages.MainAdminNotConfigured);
            return;
        }

        foreach (var adminId in AdminIds)
        {
            try
            {
                await BotClient.SendMessage(
                    chatId: adminId,
                    text: text,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: replyMarkup,
                    cancellationToken: ct);
                Logger.LogDebug(BotConstants.SystemMessages.NotifyAdmin, adminId, Truncate(text, 50));
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, BotConstants.SystemMessages.NotifyAdminFailed, adminId);
            }
        }
    }

    #endregion

    #region Методы отправки системных уведомлений в основной чат

    /// <summary>
    /// Отправляет системное уведомление в основной чат.
    /// </summary>
    /// <param name="text">Текст уведомления в формате Markdown.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения операции.</returns>
    protected async Task NotifyMainChatAsync(
        string text,
        CancellationToken ct = default)
    {
        await NotifyMainChatAsync(text, null, ct);
    }

    /// <summary>
    /// Отправляет системное уведомление в основной чат с клавиатурой.
    /// </summary>
    /// <param name="text">Текст уведомления в формате Markdown.</param>
    /// <param name="replyMarkup">Разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения операции.</returns>
    private async Task NotifyMainChatAsync(
        string text,
        ReplyMarkup? replyMarkup,
        CancellationToken ct = default)
    {
        if (MainChatId == 0)
        {
            Logger.LogWarning(BotConstants.SystemMessages.MainChatNotConfigured);
            return;
        }

        Logger.LogDebug(BotConstants.SystemMessages.NotifyMainChat, Truncate(text, 50));
        await BotClient.SendMessage(
            chatId: MainChatId,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
    }

    #endregion

    #region Методы редактирования сообщений

    /// <summary>
    /// Редактирует текст существующего сообщения (используется для кнопок).
    /// </summary>
    /// <param name="query">Запрос обратного вызова, содержащий сообщение.</param>
    /// <param name="text">Новый текст сообщения в формате Markdown.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения операции.</returns>
    protected async Task EditTextAsync(
        CallbackQuery query,
        string text,
        CancellationToken ct = default)
    {
        await EditTextAsync(query, text, null, ct);
    }

    /// <summary>
    /// Редактирует текст существующего сообщения с новой клавиатурой.
    /// </summary>
    /// <param name="query">Запрос обратного вызова, содержащий сообщение.</param>
    /// <param name="text">Новый текст сообщения в формате Markdown.</param>
    /// <param name="replyMarkup">Новая разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения операции.</returns>
    private async Task EditTextAsync(
        CallbackQuery query,
        string text,
        InlineKeyboardMarkup? replyMarkup,
        CancellationToken ct = default)
    {
        if (query.Message is null)
        {
            Logger.LogWarning(BotConstants.SystemMessages.EditMessageNull);
            return;
        }

        Logger.LogDebug(BotConstants.SystemMessages.EditMessage, query.Message.MessageId, query.Message.Chat.Id);
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
            Logger.LogWarning(BotConstants.SystemMessages.EditMarkupNull);
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
                return;
            }
            throw;
        }
    }

    #endregion

    #region Методы ответа на CallbackQuery

    /// <summary>
    /// Отвечает на CallbackQuery, убирая индикатор загрузки в Telegram.
    /// </summary>
    /// <param name="callbackQuery">Запрос обратного вызова.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения операции.</returns>
    protected async Task AnswerCallbackAsync(
        CallbackQuery callbackQuery,
        CancellationToken ct = default)
    {
        await AnswerCallbackAsync(callbackQuery, null, false, ct);
    }

    /// <summary>
    /// Отвечает на CallbackQuery с сообщением, убирая индикатор загрузки в Telegram.
    /// </summary>
    /// <param name="callbackQuery">Запрос обратного вызова.</param>
    /// <param name="message">Необязательное сообщение для пользователя.</param>
    /// <param name="showAlert">Показывать ли сообщение как всплывающее уведомление.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения операции.</returns>
    private async Task AnswerCallbackAsync(
        CallbackQuery callbackQuery,
        string? message,
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
        PlayerState state,
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
    protected bool IsAdmin(long userId) => AdminIds.Contains(userId) && AdminIds.Count > 0;

    /// <summary>
    /// Получает список администраторов, состоящих в группе.
    /// </summary>
    /// <param name="group">Группа для проверки.</param>
    /// <returns>Список игроков-администраторов в группе.</returns>
    protected List<Player> GetAdminsInGroup(Group group)
    {
        return group.Players.Where(p => AdminIds.Contains(p.TelegramId)).ToList();
    }

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