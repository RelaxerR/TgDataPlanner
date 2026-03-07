using DefaultNamespace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TgDataPlanner.Data;
using TgDataPlanner.Services;

namespace TgDataPlanner.Telegram.Handlers;

/// <summary>
/// Обработчик входящих обновлений Telegram Bot API.
/// Маршрутизирует обновления к соответствующим обработчикам команд и кнопок.
/// </summary>
public class UpdateHandler : BaseHandler
{
    private readonly CommandHandler _commandHandler;
    private readonly CallbackHandler _callbackHandler;
    private readonly ILogger<UpdateHandler> _logger;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UpdateHandler"/>.
    /// </summary>
    public UpdateHandler(
        IConfiguration config,
        ITelegramBotClient botClient,
        ILogger<UpdateHandler> logger,
        AppDbContext db,
        SchedulingService schedulingService,
        CommandHandler commandHandler,
        CallbackHandler callbackHandler)
        : base(config, botClient, logger, db, schedulingService)
    {
        _commandHandler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
        _callbackHandler = callbackHandler ?? throw new ArgumentNullException(nameof(callbackHandler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Обрабатывает входящее обновление от Telegram Bot API.
    /// </summary>
    /// <param name="update">Объект обновления.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения обработки.</returns>
    public async Task HandleUpdateAsync(Update? update, CancellationToken ct)
    {
        if (update is null)
        {
            _logger.LogWarning("Получено пустое обновление (null)");
            return;
        }

        _logger.LogDebug(
            "Получено обновление типа {UpdateType}, ChatId: {ChatId}, UserId: {UserId}",
            update.Type,
            update.Message?.Chat.Id ?? update.CallbackQuery?.Message?.Chat.Id,
            update.Message?.From?.Id ?? update.CallbackQuery?.From?.Id);

        switch (update.Type)
        {
            case UpdateType.Message when update.Message?.Text is not null:
                await HandleMessageAsync(update.Message, ct);
                break;

            case UpdateType.CallbackQuery when update.CallbackQuery is not null:
                await HandleCallbackQueryAsync(update.CallbackQuery, ct);
                break;

            case UpdateType.Unknown:
            case UpdateType.InlineQuery:
            case UpdateType.ChosenInlineResult:
            case UpdateType.EditedMessage:
            case UpdateType.ChannelPost:
            case UpdateType.EditedChannelPost:
            case UpdateType.ShippingQuery:
            case UpdateType.PreCheckoutQuery:
            case UpdateType.Poll:
            case UpdateType.PollAnswer:
            case UpdateType.MyChatMember:
            case UpdateType.ChatMember:
            case UpdateType.ChatJoinRequest:
            case UpdateType.MessageReaction:
            case UpdateType.MessageReactionCount:
            case UpdateType.ChatBoost:
            case UpdateType.RemovedChatBoost:
            case UpdateType.BusinessConnection:
            case UpdateType.BusinessMessage:
            case UpdateType.EditedBusinessMessage:
            case UpdateType.DeletedBusinessMessages:
            case UpdateType.PurchasedPaidMedia:
            default:
                _logger.LogDebug("Пропущено обновление типа {Type}: не поддерживается", update.Type);
                break;
        }
    }

    /// <summary>
    /// Обрабатывает текстовое сообщение, передавая его в CommandHandler.
    /// </summary>
    private async Task HandleMessageAsync(Message message, CancellationToken ct)
    {
        _logger.LogInformation(
            "Обработка команды от пользователя {UserId} в чате {ChatId}: {TextPreview}",
            message.From?.Id,
            message.Chat.Id,
            TruncateForLog(message.Text));

        await _commandHandler.HandleAsync(message, ct);
    }

    /// <summary>
    /// Обрабатывает нажатие кнопки, передавая его в CallbackHandler.
    /// </summary>
    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        _logger.LogInformation(
            "Обработка callback от пользователя {UserId}: {CallbackData}",
            callbackQuery.From?.Id,
            callbackQuery.Data);

        await _callbackHandler.HandleAsync(callbackQuery, ct);
    }

    /// <summary>
    /// Обрезает текст для безопасного логирования.
    /// </summary>
    private static string TruncateForLog(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty :
        text.Length > 100 ? text[..100] + "..." : text;
}