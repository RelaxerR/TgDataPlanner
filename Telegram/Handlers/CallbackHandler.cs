using Telegram.Bot.Types;

namespace TgDataPlanner.Telegram.Handlers;

public class CallbackHandler
{
    public static async Task HandleAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        Console.WriteLine($"Нажата кнопка: {callbackQuery.Data}");
        // Тут будет логика кликов по часам и датам
        await Task.CompletedTask;
    }
}