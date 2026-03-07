using DefaultNamespace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using TgDataPlanner.Telegram.Handlers;

namespace TgDataPlanner.Telegram;

public class BotBackgroundService(ILogger<BotBackgroundService> logger, ITelegramBotClient botClient, IServiceProvider serviceProvider) : BackgroundService
{

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Инициализация бота...");
        
        var receiverOptions = new ReceiverOptions
        {
            DropPendingUpdates = true
        };

        // В новых версиях используются Func (Task), а не Action
        botClient.StartReceiving(
            updateHandler: OnUpdateInternal,
            errorHandler: OnErrorInternal, 
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken
        );

        logger.LogInformation("Бот успешно запущен и слушает сообщения.");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task OnUpdateInternal(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<Handlers.UpdateHandler>();
        await handler.HandleUpdateAsync(update, ct);
    }
    
    private Task OnErrorInternal(ITelegramBotClient bot, Exception ex, HandleErrorSource source, CancellationToken ct)
    {
        logger.LogError(ex, "Ошибка Telegram API из источника {Source}", source);
        return Task.CompletedTask;
    }
}