using DefaultNamespace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TgDataPlanner.Data.Entities;
using TgDataPlanner.Telegram.Menus;

namespace TgDataPlanner.Telegram.Handlers;

public class CallbackHandler(
    ITelegramBotClient botClient,
    AppDbContext db,
    IConfiguration config,
    ILogger<CommandHandler> logger)
{
    public async Task HandleAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        logger.LogInformation("Нажата кнопка: {Data}", callbackQuery.Data);
        // Тут будет логика кликов по часам и датам
        await Task.CompletedTask;
        
        if (callbackQuery.Data == "cancel_action")
        {
            var userId = callbackQuery.From.Id;
            var player = await db.Players.FindAsync([userId], ct);

            if (player != null)
            {
                player.CurrentState = null; // Сбрасываем стейт
                await db.SaveChangesAsync(ct);
            }

            // Убираем кнопки из сообщения и пишем, что отменено
            await botClient.EditMessageText(
                chatId: callbackQuery.Message!.Chat.Id,
                messageId: callbackQuery.Message.MessageId,
                text: "🚫 Действие отменено.",
                cancellationToken: ct);
            
            // Важно: всегда отвечаем на CallbackQuery, чтобы убрать "часики" в ТГ
            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        }
        
        if (callbackQuery?.Data != null && callbackQuery.Data.StartsWith("set_tz_"))
        {
            var offsetString = callbackQuery.Data.Replace("set_tz_", "");
            if (int.TryParse(offsetString, out var offset))
            {
                var userId = callbackQuery.From.Id;
                var player = await db.Players.FindAsync([userId], ct);

                if (player != null)
                {
                    player.TimeZoneOffset = offset;
                    await db.SaveChangesAsync(ct);
            
                    await botClient.EditMessageText(
                        callbackQuery.Message!.Chat.Id,
                        callbackQuery.Message.MessageId,
                        $"✅ Ваш часовой пояс установлен: **UTC {(offset >= 0 ? "+" : "")}{offset}**",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: ct);
                }
            }
            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        }
        
        // Внутри HandleAsync

        // 1. Выбор конкретной даты
        if (callbackQuery.Data!.StartsWith("pick_date_"))
        {
            var dateStr = callbackQuery.Data.Replace("pick_date_", "");
            var date = DateTime.Parse(dateStr);
            var player = await db.Players.Include(p => p.Slots).FirstOrDefaultAsync(p => p.TelegramId == callbackQuery.From.Id, cancellationToken: ct);
    
            await botClient.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                $"🕒 Выберите время для **{date:dd.MM}**:",
                replyMarkup: AvailabilityMenu.GetTimeGrid(date, player!.Slots, player.TimeZoneOffset),
                cancellationToken: ct);
        }

        // 2. Переключение (Toggle) часа
        if (callbackQuery.Data!.StartsWith("toggle_time_"))
        {
            var parts = callbackQuery.Data.Split('_'); // toggle, time, date, hour
            var date = DateTime.Parse(parts[2]);
            var hour = int.Parse(parts[3]);

            var player = await db.Players.Include(p => p.Slots).FirstOrDefaultAsync(p => p.TelegramId == callbackQuery.From.Id, cancellationToken: ct);
            var slotTimeUtc = new DateTime(date.Year, date.Month, date.Day, hour % 24, 0, 0).AddHours(-player!.TimeZoneOffset);

            var existingSlot = player.Slots.FirstOrDefault(s => s.DateTimeUtc == slotTimeUtc);

            if (existingSlot != null) db.Slots.Remove(existingSlot);
            else db.Slots.Add(new AvailabilitySlot { PlayerId = player.TelegramId, DateTimeUtc = slotTimeUtc });

            await db.SaveChangesAsync(ct);

            // Обновляем только кнопки, текст не трогаем
            await botClient.EditMessageReplyMarkup(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                replyMarkup: AvailabilityMenu.GetTimeGrid(date, player.Slots, player.TimeZoneOffset),
                cancellationToken: ct);
        }
        
        // Обработка кнопки "Назад к датам"
        if (callbackQuery.Data == "back_to_dates")
        {
            var userId = callbackQuery.From.Id;
            var player = await db.Players.FindAsync(new object[] { userId }, ct);
    
            await botClient.EditMessageText(
                chatId: callbackQuery.Message!.Chat.Id,
                messageId: callbackQuery.Message.MessageId,
                text: "📅 **Выбор доступного времени**\nВыберите дату, чтобы отметить часы, когда вы свободны:",
                parseMode: ParseMode.Markdown,
                replyMarkup: AvailabilityMenu.GetDateCalendar(player?.TimeZoneOffset ?? 0),
                cancellationToken: ct);

            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
            return;
        }
    }
}