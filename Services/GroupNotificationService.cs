using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TgDataPlanner.Common;
using TgDataPlanner.Configuration;
using TgDataPlanner.Data.Entities;

namespace TgDataPlanner.Services;

/// <summary>
/// Сервис для отправки уведомлений игрокам и администраторам.
/// Инкапсулирует логику формирования и отправки сообщений.
/// </summary>
public class GroupNotificationService
{
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<GroupNotificationService> _logger;
    private readonly List<long> _adminIds;
    private readonly long _mainChatId;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="GroupNotificationService"/>.
    /// </summary>
    /// <param name="botClient">Клиент Telegram Bot API.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    /// <param name="adminIds">Список идентификаторов администраторов.</param>
    /// <param name="mainChatId">Идентификатор основного чата.</param>
    public GroupNotificationService(
        ITelegramBotClient botClient,
        ILogger<GroupNotificationService> logger,
        List<long> adminIds,
        long mainChatId)
    {
        _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adminIds = adminIds ?? new List<long>();
        _mainChatId = mainChatId;
    }

    /// <summary>
    /// Отправляет уведомление каждому игроку в группе (в личные сообщения).
    /// </summary>
    /// <param name="group">Группа, игрокам которой отправляется сообщение.</param>
    /// <param name="text">Текст уведомления в формате Markdown.</param>
    /// <param name="replyMarkup">Разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    public async Task NotifyAllInGroupAsync(
        Group group,
        string text,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        var users = group.Players.Select(p => p.TelegramId).ToList();
        foreach (var userId in users)
        {
            try
            {
                await _botClient.SendMessage(
                    chatId: userId,
                    text: text,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: replyMarkup,
                    cancellationToken: ct);
                _logger.LogDebug("Уведомление отправлено пользователю @{UserId}: {TextPreview}", userId, Truncate(text, 50));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось отправить уведомление пользователю {UserId}", userId);
            }
        }
    }

    /// <summary>
    /// Отправляет сообщение главному администратору.
    /// </summary>
    /// <param name="text">Текст сообщения в формате Markdown.</param>
    /// <param name="replyMarkup">Разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    public async Task<Message?> SendToMainAdminAsync(
        string text,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        var mainAdminId = _adminIds.FirstOrDefault();
        if (mainAdminId == 0)
        {
            _logger.LogWarning("Попытка отправить сообщение главному администратору, но AdminIds не настроен");
            return null;
        }

        _logger.LogDebug("Отправка сообщения главному администратору {AdminId}: {TextPreview}", mainAdminId, Truncate(text, 50));
        return await _botClient.SendMessage(
            chatId: mainAdminId,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
    }

    /// <summary>
    /// Отправляет системное уведомление в основной чат.
    /// </summary>
    /// <param name="text">Текст уведомления в формате Markdown.</param>
    /// <param name="replyMarkup">Разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    public async Task NotifyMainChatAsync(
        string text,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        if (_mainChatId == 0)
        {
            _logger.LogWarning("Попытка отправить уведомление в MainChat, но MainChatId не настроен");
            return;
        }

        _logger.LogDebug("Системное уведомление в основной чат: {TextPreview}", Truncate(text, 50));
        await _botClient.SendMessage(
            chatId: _mainChatId,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
    }

    /// <summary>
    /// Отправляет сообщение всем администраторам (в личные сообщения).
    /// </summary>
    /// <param name="text">Текст сообщения в формате Markdown.</param>
    /// <param name="replyMarkup">Разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    public async Task NotifyAllAdminsAsync(
        string text,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        if (_adminIds.Count == 0)
        {
            _logger.LogWarning("Попытка отправить сообщение всем администраторам, но AdminIds не настроен");
            return;
        }

        foreach (var adminId in _adminIds)
        {
            try
            {
                await _botClient.SendMessage(
                    chatId: adminId,
                    text: text,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: replyMarkup,
                    cancellationToken: ct);
                _logger.LogDebug("Уведомление отправлено администратору @{AdminId}: {TextPreview}", adminId, Truncate(text, 50));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось отправить уведомление администратору {AdminId}", adminId);
            }
        }
    }

    /// <summary>
    /// Отправляет сообщение конкретному пользователю.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="text">Текст сообщения в формате Markdown.</param>
    /// <param name="replyMarkup">Разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    public async Task<Message> SendToUserAsync(
        long telegramId,
        string text,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Отправка сообщения пользователю {TelegramId}: {TextPreview}", telegramId, Truncate(text, 50));
        return await _botClient.SendMessage(
            chatId: telegramId,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
    }

    /// <summary>
    /// Отправляет сообщение в чат группы.
    /// </summary>
    /// <param name="chatId">Идентификатор чата группы.</param>
    /// <param name="text">Текст сообщения в формате Markdown.</param>
    /// <param name="replyMarkup">Разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    public async Task<Message> SendToGroupChatAsync(
        long chatId,
        string text,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Отправка сообщения в чат группы {ChatId}: {TextPreview}", chatId, Truncate(text, 50));
        return await _botClient.SendMessage(
            chatId: chatId,
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
    public async Task AnswerCallbackAsync(
        CallbackQuery callbackQuery,
        string? message = null,
        bool showAlert = false,
        CancellationToken ct = default)
    {
        await _botClient.AnswerCallbackQuery(
            callbackQueryId: callbackQuery.Id,
            text: message,
            showAlert: showAlert,
            cancellationToken: ct);
    }

    /// <summary>
    /// Редактирует текст существующего сообщения.
    /// </summary>
    /// <param name="query">Запрос обратного вызова, содержащий сообщение.</param>
    /// <param name="text">Новый текст сообщения в формате Markdown.</param>
    /// <param name="replyMarkup">Новая разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    public async Task EditTextAsync(
        CallbackQuery query,
        string text,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        if (query.Message is null)
        {
            _logger.LogWarning("Попытка редактировать сообщение, но CallbackQuery.Message равен null");
            return;
        }

        _logger.LogDebug("Редактирование сообщения {MessageId} в чате {ChatId}", query.Message.MessageId, query.Message.Chat.Id);
        await _botClient.EditMessageText(
            chatId: query.Message.Chat.Id,
            messageId: query.Message.MessageId,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
    }

    /// <summary>
    /// Редактирует только клавиатуру сообщения.
    /// </summary>
    /// <param name="query">Запрос обратного вызова, содержащий сообщение.</param>
    /// <param name="replyMarkup">Новая разметка клавиатуры.</param>
    /// <param name="ct">Токен отмены операции.</param>
    public async Task EditReplyMarkupAsync(
        CallbackQuery query,
        InlineKeyboardMarkup? replyMarkup,
        CancellationToken ct = default)
    {
        if (query.Message is null)
        {
            _logger.LogWarning("Попытка редактировать клавиатуру, но CallbackQuery.Message равен null");
            return;
        }

        try
        {
            await _botClient.EditMessageReplyMarkup(
                chatId: query.Message.Chat.Id,
                messageId: query.Message.MessageId,
                replyMarkup: replyMarkup,
                cancellationToken: ct);
        }
        catch (ApiRequestException ex)
        {
            if (ex.ErrorCode == 400 && ex.Message.Contains("message is not modified"))
            {
                return;
            }
            throw;
        }
    }

    /// <summary>
    /// Обрезает строку до указанной длины для безопасного логирования.
    /// </summary>
    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    /// <summary>
    /// Создаёт клавиатуру для RSVP-ответов.
    /// </summary>
    /// <param name="groupId">Идентификатор группы.</param>
    public InlineKeyboardMarkup CreateRsvpKeyboard(int groupId) => new([
        [
            InlineKeyboardButton.WithCallbackData(BotConstants.UiTexts.ButtonRsvpYes, $"{BotConstants.CallbackPrefixes.RsvpYes}{groupId}"),
            InlineKeyboardButton.WithCallbackData(BotConstants.UiTexts.ButtonRsvpNo, $"{BotConstants.CallbackPrefixes.RsvpNo}{groupId}")
        ]
    ]);
}