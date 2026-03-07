using DefaultNamespace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TgDataPlanner.Services.Scheduling;

namespace TgDataPlanner.Telegram.Handlers;

/// <summary>
/// Базовый класс для обработчиков Telegram-событий.
/// Предоставляет общие методы для отправки сообщений и работы с конфигурацией.
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
    /// Контекст базы данных для работы с сущностями.
    /// </summary>
    protected readonly AppDbContext Db;

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
    /// <param name="db">Контекст базы данных.</param>
    /// <param name="schedulingService">Сервис планирования.</param>
    protected BaseHandler(
        IConfiguration config,
        ITelegramBotClient botClient,
        ILogger<BaseHandler> logger,
        AppDbContext db,
        SchedulingService schedulingService)
    {
        BotClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Db = db ?? throw new ArgumentNullException(nameof(db));
        SchedulingService = schedulingService ?? throw new ArgumentNullException(nameof(schedulingService));

        if (!long.TryParse(config["MainChatId"], out var mainChatId))
        {
            Logger.LogError("Не удалось распарсить MainChatId из конфигурации. Значение: {Value}", config["MainChatId"]);
        }
        MainChatId = mainChatId;

        if (!long.TryParse(config["AdminId"], out var adminId))
        {
            Logger.LogError("Не удалось распарсить AdminId из конфигурации. Значение: {Value}", config["AdminId"]);
        }
        AdminId = adminId;
    }

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

    /// <summary>
    /// Проверяет, является ли пользователь администратором.
    /// </summary>
    /// <param name="userId">Идентификатор пользователя Telegram.</param>
    /// <returns>True, если пользователь является администратором.</returns>
    protected bool IsAdmin(long userId) => userId == AdminId && AdminId != 0;

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
}