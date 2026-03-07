using DefaultNamespace;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TgDataPlanner.Telegram.Handlers;

public class UpdateHandler(
    ILogger<UpdateHandler> logger,
    AppDbContext db,
    ITelegramBotClient botClient,
    CommandHandler commandHandler,
    CallbackHandler callbackHandler)
{
    public async Task HandleUpdateAsync(Update update, CancellationToken ct)
    {
        logger.LogInformation("Получено обновление типа: {Type}", update.Type);
        
        // Обработка сообщений (команд)
        if (update.Message is { Text: not null } message)
        {
            logger.LogInformation("Сообщение от {User} [{Id}]: {Text}", message.From?.Username, message.From?.Id, message.Text);
            
            await commandHandler.HandleAsync(message, ct);
            return;
        }

        // Обработка нажатий на кнопки (инлайн)
        if (update.CallbackQuery is not null)
        {
            await callbackHandler.HandleAsync(update.CallbackQuery, ct);
            return;
        }
    }
}
