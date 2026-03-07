using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace TgDataPlanner.Telegram.Handlers;

public class UpdateHandler(ILogger<UpdateHandler> logger, CommandHandler commandHandler, CallbackHandler callbackHandler)
{
    private readonly CommandHandler _commandHandler = commandHandler;
    private readonly CallbackHandler _callbackHandler = callbackHandler;

    public async Task HandleUpdateAsync(Update update, CancellationToken ct)
    {
        logger.LogInformation("Получено обновление типа: {Type}", update.Type);
        
        // Обработка сообщений (команд)
        if (update.Message is { Text: not null } message)
        {
            logger.LogInformation("Сообщение от {User}: {Text}", message.From?.Username, message.Text);
            
            await CommandHandler.HandleAsync(message, ct);
            return;
        }

        // Обработка нажатий на кнопки (инлайн)
        if (update.CallbackQuery is not null)
        {
            await CallbackHandler.HandleAsync(update.CallbackQuery, ct);
            return;
        }
    }
}
