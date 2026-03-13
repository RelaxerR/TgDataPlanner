using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TgDataPlanner.AI;
using TgDataPlanner.Data;
using TgDataPlanner.Services;
using static TgDataPlanner.Configuration.BotConstants.SystemMessages;

namespace TgDataPlanner.Telegram.Handlers;

/// <summary>
/// Обработчик входящих обновлений Telegram Bot API.
/// Маршрутизирует обновления к соответствующим обработчикам команд и кнопок.
/// </summary>
[SuppressMessage("Usage", "CA2253:Named placeholders should not be numeric values")]
public class UpdateHandler : BaseHandler
{
    private readonly CommandHandler _commandHandler;
    private readonly CallbackHandler _callbackHandler;
    private readonly ILogger<UpdateHandler> _logger;

    /// <summary>
    /// Типы обновлений, которые поддерживаются обработчиком.
    /// </summary>
    private static class SupportedUpdateTypes
    {
        public static readonly HashSet<UpdateType> Values =
        [
            UpdateType.Message,
            UpdateType.CallbackQuery
        ];
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UpdateHandler"/>.
    /// </summary>
    /// <param name="config">Конфигурация приложения.</param>
    /// <param name="botClient">Клиент Telegram Bot API.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    /// <param name="db">Контекст базы данных</param>
    /// <param name="userService">Сервис управления пользователями.</param>
    /// <param name="schedulingService">Сервис планирования.</param>
    /// <param name="commandHandler">Обработчик текстовых команд.</param>
    /// <param name="callbackHandler">Обработчик нажатий на кнопки.</param>
    /// <param name="ollamaService">Сервис ИИ</param>
    public UpdateHandler(
        IConfiguration config,
        ITelegramBotClient botClient,
        ILogger<UpdateHandler> logger,
        AppDbContext db,
        UserService userService,
        SchedulingService schedulingService,
        CommandHandler commandHandler,
        CallbackHandler callbackHandler,
        OllamaService ollamaService)
        : base(config, botClient, logger, db, userService, schedulingService, ollamaService)
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
            _logger.LogWarning(UpdateReceivedNull);
            return;
        }

        var context = ExtractUpdateContext(update);
        LogUpdateReceived(update.Type, context);

        if (!SupportedUpdateTypes.Values.Contains(update.Type))
        {
            LogUpdateSkipped(update.Type);
            return;
        }

        try
        {
            await RouteUpdateAsync(update, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                UpdateError,
                update.Type);
        }
    }

    /// <summary>
    /// Извлекает контекст обновления (ChatId, UserId) для логирования.
    /// </summary>
    /// <param name="update">Объект обновления.</param>
    /// <returns>Кортеж с идентификаторами чата и пользователя.</returns>
    private static (long?, long?) ExtractUpdateContext(Update update) =>
        update.Type switch
        {
            UpdateType.Message when update.Message is not null =>
                (update.Message.Chat.Id, update.Message.From?.Id),
            UpdateType.CallbackQuery when update.CallbackQuery is not null =>
                (update.CallbackQuery.Message?.Chat.Id, update.CallbackQuery.From.Id),
            _ => (null, null)
        };

    /// <summary>
    /// Логирует факт получения обновления.
    /// </summary>
    private void LogUpdateReceived(UpdateType type, (long? ChatId, long? UserId) context) =>
        _logger.LogDebug(
            UpdateReceived,
            type, context.ChatId, context.UserId);

    /// <summary>
    /// Логирует пропуск неподдерживаемого типа обновления.
    /// </summary>
    private void LogUpdateSkipped(UpdateType type) =>
        _logger.LogDebug(UpdateSkipped, type);

    /// <summary>
    /// Маршрутизирует обновление к соответствующему обработчику.
    /// </summary>
    private async Task RouteUpdateAsync(Update update, CancellationToken ct)
    {
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
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    /// <summary>
    /// Обрабатывает текстовое сообщение, передавая его в CommandHandler.
    /// </summary>
    private async Task HandleMessageAsync(Message message, CancellationToken ct)
    {
        _logger.LogInformation(
            MessageProcessing,
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
            CallbackProcessing,
            callbackQuery.From.Id,
            callbackQuery.Data);
        await _callbackHandler.HandleAsync(callbackQuery, ct);
    }

    /// <summary>
    /// Обрезает текст для безопасного логирования.
    /// </summary>
    /// <param name="text">Исходный текст.</param>
    /// <param name="maxLength">Максимальная длина результата.</param>
    /// <returns>Обрезанная строка.</returns>
    private static string TruncateForLog(string? text, int maxLength = 100) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Length <= maxLength ? text : text[..maxLength] + "...";
}