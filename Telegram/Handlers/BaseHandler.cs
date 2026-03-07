using DefaultNamespace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TgDataPlanner.Services.Scheduling;

namespace TgDataPlanner.Telegram.Handlers;

public abstract class BaseHandler
{
    protected readonly ITelegramBotClient BotClient;
    
    protected readonly long MainChatId;
    protected readonly long AdminId;

    protected BaseHandler(
        IConfiguration config,
        ITelegramBotClient botClient,
        ILogger<CommandHandler> logger,
        AppDbContext db,
        SchedulingService schedulingService)
    {
        BotClient = botClient;

        if (!long.TryParse(config["MainChatId"], out MainChatId))
        {
            logger.LogError("MainChatId is invalid");
        }
        if (!long.TryParse(config["AdminId"], out AdminId))
        {
            logger.LogError("AdminId is invalid");
        }
    }

    // Отправить сообщение в ЛС или конкретный чат
    protected async Task<Message> SendText(long chatId, string text, ReplyMarkup? replyMarkup = null, CancellationToken ct = default)
    {
        return await BotClient.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: ParseMode.Markdown, // Всегда используем Маркдаун по умолчанию
            replyMarkup: replyMarkup,
            cancellationToken: ct
        );
    }

    // Отправить системное уведомление в ОБЩИЙ ЧАТ
    protected async Task NotifyMainChat(string text, CancellationToken ct = default)
    {
        if (MainChatId == 0) return;
        
        await BotClient.SendMessage(
            chatId: MainChatId,
            text: $"🔔 {text}",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct
        );
    }

    // Быстрое редактирование текста (для кнопок)
    protected async Task EditText(CallbackQuery query, string text, InlineKeyboardMarkup? replyMarkup = null, CancellationToken ct = default)
    {
        await BotClient.EditMessageText(
            chatId: query.Message!.Chat.Id,
            messageId: query.Message.MessageId,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: replyMarkup,
            cancellationToken: ct
        );
    }
}

