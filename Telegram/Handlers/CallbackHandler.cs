using DefaultNamespace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TgDataPlanner.Data.Entities;
using TgDataPlanner.Services.Scheduling;
using TgDataPlanner.Telegram.Menus;

namespace TgDataPlanner.Telegram.Handlers;

public class CallbackHandler(
    IConfiguration config,
    ITelegramBotClient botClient,
    ILogger<CommandHandler> logger,
    AppDbContext db,
    SchedulingService schedulingService) : BaseHandler(config, botClient, logger, db, schedulingService)
{
    private readonly IConfiguration _config = config;
    private readonly ILogger<CommandHandler> _logger = logger;
    private readonly AppDbContext _db = db;
    private readonly SchedulingService _schedulingService = schedulingService;
    
    public async Task HandleAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        var userId = callbackQuery.From.Id;
    
        if (!long.TryParse(_config["AdminId"], out var _adminId))
        {
            _logger.LogError("Не удалось получить AdminId из конфигурации");
        }
    
        // Пример защиты для критичных действий:
        if (callbackQuery.Data.StartsWith("confirm_time_") && userId != _adminId)
        {
            await BotClient.AnswerCallbackQuery(callbackQuery.Id, "Только админ может подтвердить время!", showAlert: true, cancellationToken: ct);
            return;
        }
        
        _logger.LogInformation("Нажата кнопка: {Data}", callbackQuery.Data);
        // Тут будет логика кликов по часам и датам
        await Task.CompletedTask;
        
        if (callbackQuery.Data == "cancel_action")
        {
            var player = await _db.Players.FindAsync([userId], ct);

            if (player != null)
            {
                player.CurrentState = null; // Сбрасываем стейт
                await _db.SaveChangesAsync(ct);
            }

            // Убираем кнопки из сообщения и пишем, что отменено
            await BotClient.EditMessageText(
                chatId: callbackQuery.Message!.Chat.Id,
                messageId: callbackQuery.Message.MessageId,
                text: "🚫 Действие отменено.",
                cancellationToken: ct);
            
            // Важно: всегда отвечаем на CallbackQuery, чтобы убрать "часики" в ТГ
            await BotClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        }
        
        if (callbackQuery?.Data != null && callbackQuery.Data.StartsWith("set_tz_"))
        {
            var offsetString = callbackQuery.Data.Replace("set_tz_", "");
            if (int.TryParse(offsetString, out var offset))
            {
                var player = await _db.Players.FindAsync([userId], ct);

                if (player != null)
                {
                    player.TimeZoneOffset = offset;
                    await _db.SaveChangesAsync(ct);
            
                    await BotClient.EditMessageText(
                        callbackQuery.Message!.Chat.Id,
                        callbackQuery.Message.MessageId,
                        $"✅ Ваш часовой пояс установлен: **UTC {(offset >= 0 ? "+" : "")}{offset}**",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: ct);
                }
            }
            await BotClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        }
        
        // Внутри HandleAsync

        // 1. Выбор конкретной даты
        if (callbackQuery.Data!.StartsWith("pick_date_"))
        {
            var dateStr = callbackQuery.Data.Replace("pick_date_", "");
            var date = DateTime.Parse(dateStr);
            var player = await _db.Players.Include(p => p.Slots).FirstOrDefaultAsync(p => p.TelegramId == callbackQuery.From.Id, cancellationToken: ct);
    
            await BotClient.EditMessageText(
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

            var player = await _db.Players.Include(p => p.Slots).FirstOrDefaultAsync(p => p.TelegramId == callbackQuery.From.Id, cancellationToken: ct);
            var slotTimeUtc = new DateTime(date.Year, date.Month, date.Day, hour % 24, 0, 0).AddHours(-player!.TimeZoneOffset);

            var existingSlot = player.Slots.FirstOrDefault(s => s.DateTimeUtc == slotTimeUtc);

            if (existingSlot != null) _db.Slots.Remove(existingSlot);
            else _db.Slots.Add(new AvailabilitySlot { PlayerId = player.TelegramId, DateTimeUtc = slotTimeUtc });

            await _db.SaveChangesAsync(ct);

            // Обновляем только кнопки, текст не трогаем
            await BotClient.EditMessageReplyMarkup(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                replyMarkup: AvailabilityMenu.GetTimeGrid(date, player.Slots, player.TimeZoneOffset),
                cancellationToken: ct);
        }
        
        // Обработка кнопки "Назад к датам"
        if (callbackQuery.Data == "back_to_dates")
        {
            var player = await _db.Players.FindAsync(new object[] { userId }, ct);
    
            await BotClient.EditMessageText(
                chatId: callbackQuery.Message!.Chat.Id,
                messageId: callbackQuery.Message.MessageId,
                text: "📅 **Выбор доступного времени**\nВыберите дату, чтобы отметить часы, когда вы свободны:",
                parseMode: ParseMode.Markdown,
                replyMarkup: AvailabilityMenu.GetDateCalendar(player?.TimeZoneOffset ?? 0),
                cancellationToken: ct);

            await BotClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
            return;
        }
        
        // Внутри HandleAsync (CallbackHandler)
        if (callbackQuery.Data!.StartsWith("start_plan_"))
        {
            var groupId = int.Parse(callbackQuery.Data.Replace("start_plan_", ""));
    
            // Ищем окна от 3 часов (пока хардкод, потом можно сделать ввод)
            var intersections = await _schedulingService.FindIntersections(groupId, 3);

            if (!intersections.Any())
            {
                // Убираем лишний символ "_" и "=" перед переменной
                await BotClient.EditMessageText(
                    chatId: callbackQuery.Message!.Chat.Id,
                    messageId: callbackQuery.Message.MessageId,
                    text: "😔 **Пересечений не найдено.** Все игроки заняты в разное время.\n\n*Запустить режим рекомендаций? (В разработке)*",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: ct);
                return;
            }

            var resultText = "🗓 **Найденные окна (Ваше время):**\n\n";
            var buttons = new List<InlineKeyboardButton[]>();

            foreach (var interval in intersections.Take(5)) // Показываем первые 5 вариантов
            {
                // Переводим в локальное время админа для отображения
                var admin = await _db.Players.FindAsync(new object[] { callbackQuery.From.Id }, ct);
                var localStart = interval.Start.AddHours(admin?.TimeZoneOffset ?? 0);
                var localEnd = interval.End.AddHours(admin?.TimeZoneOffset ?? 0);

                var timeStr = $"{localStart:dd.MM HH:mm} - {localEnd:HH:mm}";
                resultText += $"🔹 {timeStr}\n";
        
                buttons.Add(new[] { InlineKeyboardButton.WithCallbackData($"✅ {timeStr}", $"confirm_time_{groupId}_{interval.Start:yyyyMMddHH}") });
            }

            await BotClient.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                resultText,
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(buttons),
                cancellationToken: ct);
        }
        if (callbackQuery.Data == "finish_voting")
        {
            var player = await _db.Players
                .Include(p => p.Groups)
                .FirstOrDefaultAsync(p => p.TelegramId == userId, ct);

            if (player == null) return;

            var groupNames = player.Groups.Any() 
                ? string.Join(", ", player.Groups.Select(g => g.Name)) 
                : "Без группы";

            // Уведомляем игрока в ЛС
            await BotClient.EditMessageText(callbackQuery.Message!.Chat.Id, callbackQuery.Message.MessageId, "✅ Данные сохранены!");

            // Уведомление в ОБЩИЙ ЧАТ
            // await botClient.SendMessage(
            //     chatId: _mainChatId,
            //     text: $"🔔 **{player.Username}** [{groupNames}] завершил заполнение расписания!",
            //     cancellationToken: ct);

            await BotClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        }
        
        // Вступление
        if (callbackQuery.Data!.StartsWith("join_group_"))
        {
            var groupId = int.Parse(callbackQuery.Data.Replace("join_group_", ""));
            var player = await _db.Players.Include(p => p.Groups).FirstOrDefaultAsync(p => p.TelegramId == userId, ct);
            var group = await _db.Groups.FindAsync(new object[] { groupId }, ct);

            if (group != null && !player!.Groups.Any(g => g.Id == groupId))
            {
                player.Groups.Add(group);
                await _db.SaveChangesAsync(ct);
                await BotClient.EditMessageText(callbackQuery.Message!.Chat.Id, callbackQuery.Message.MessageId, $"⚔️ Вы вступили в группу **{group.Name}**!");
            }
        }

        // Удаление группы (админом)
        if (callbackQuery.Data!.StartsWith("confirm_delete_"))
        {
            var groupId = int.Parse(callbackQuery.Data.Replace("confirm_delete_", ""));
            var group = await _db.Groups.FindAsync(new object[] { groupId }, ct);
            if (group != null)
            {
                _db.Groups.Remove(group);
                await _db.SaveChangesAsync(ct);
                await BotClient.EditMessageText(callbackQuery.Message!.Chat.Id, callbackQuery.Message.MessageId, $"🗑 Группа **{group.Name}** удалена.");
            }
        }


    }
}