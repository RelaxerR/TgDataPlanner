using Telegram.Bot.Types;

namespace TgDataPlanner.Telegram.Handlers;

public class CommandHandler
{
    public static async Task HandleAsync(Message message, CancellationToken ct)
    {
        Console.WriteLine($"Команда: {message.Text}");
        // Тут будет логика обработки /start, /free и т.д.
        await Task.CompletedTask;
    }
}