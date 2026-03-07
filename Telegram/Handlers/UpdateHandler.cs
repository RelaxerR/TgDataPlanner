using DefaultNamespace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TgDataPlanner.Services.Scheduling;

namespace TgDataPlanner.Telegram.Handlers;

public class UpdateHandler(
    IConfiguration config,
    ITelegramBotClient botClient,
    ILogger<CommandHandler> logger,
    AppDbContext db,
    SchedulingService schedulingService,
    CommandHandler commandHandler,
    CallbackHandler callbackHandler) : BaseHandler(config, botClient, logger, db, schedulingService)
{
    private readonly ILogger<CommandHandler> _logger = logger;
    
    public async Task HandleUpdateAsync(Update update, CancellationToken ct)
    {
        _logger.LogInformation("Получено обновление типа: {Type}", update.Type);
        _logger.LogInformation("ID чата: {ChatId}", update.Message.Chat.Id);
        
        // 1. Если это текстовое сообщение -> в CommandHandler
        if (update.Type == UpdateType.Message && update.Message?.Text != null)
        {
            await commandHandler.HandleAsync(update.Message, ct);
            return;
        }

        // 2. Если это нажатие кнопки -> в CallbackHandler
        if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
        {
            // ОШИБКА: Раньше тут мог вызываться _commandHandler случайно
            await callbackHandler.HandleAsync(update.CallbackQuery, ct);
            return;
        }
    }
}
