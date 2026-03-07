using DefaultNamespace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TgDataPlanner.Data.Entities;
using TgDataPlanner.Telegram.Menus;

namespace TgDataPlanner.Telegram.Handlers;

public class CommandHandler(
    ITelegramBotClient botClient,
    AppDbContext db,
    IConfiguration config,
    ILogger<CommandHandler> logger)
{
    private readonly long _adminId = long.Parse(config["AdminId"] ?? "0");

    public async Task HandleAsync(Message message, CancellationToken ct)
    {
        var userId = message.From?.Id ?? 0;
        var text = message.Text ?? "";

        // Находим или создаем игрока в БД, чтобы проверить его стейт
        var player = await db.Players.FindAsync([userId], ct);
        if (player == null)
        {
            player = new Player { TelegramId = userId, Username = message.From?.Username ?? "Unknown" };
            db.Players.Add(player);
            await db.SaveChangesAsync(ct);
        }

        // 1. Если пользователь в процессе создания группы
        if (player.CurrentState == "AwaitingGroupName")
        {
            await FinalizeGroupCreation(message, player, ct);
            return;
        }

        // 2. Обработка команд
        if (text == "/create_group")
        {
            if (userId != _adminId) return;

            player.CurrentState = "AwaitingGroupName";
            await db.SaveChangesAsync(ct);

            // Создаем кнопку отмены
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                InlineKeyboardButton.WithCallbackData("❌ Отмена", "cancel_action")
            });

            await botClient.SendMessage(
                chatId: message.Chat.Id, 
                text: "📝 **Создание новой группы**\n\nВведите название для вашей D&D кампании:",
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: ct);
        }
        
        if (text == "/join")
        {
            await HandleJoinGroup(message, player, ct);
        }
        
        if (text == "/timezone")
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("UTC -1", "set_tz_-1"), InlineKeyboardButton.WithCallbackData("UTC +0", "set_tz_0"), InlineKeyboardButton.WithCallbackData("UTC +1", "set_tz_1") },
                new[] { InlineKeyboardButton.WithCallbackData("UTC +2", "set_tz_2"), InlineKeyboardButton.WithCallbackData("UTC +3 (МСК)", "set_tz_3"), InlineKeyboardButton.WithCallbackData("UTC +4 (ИЖ)", "set_tz_4") },
                new[] { InlineKeyboardButton.WithCallbackData("UTC +5", "set_tz_5"), InlineKeyboardButton.WithCallbackData("UTC +6", "set_tz_6"), InlineKeyboardButton.WithCallbackData("UTC +7", "set_tz_7") }
            });

            await botClient.SendMessage(
                message.Chat.Id, 
                "🌍 **Настройка часового пояса**\n\nВыберите ваше смещение относительно UTC (например, для Москвы это +3):",
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: ct);
        }
        
        if (text == "/free")
        {
            await botClient.SendMessage(
                message.Chat.Id,
                "📅 **Выбор доступного времени**\nВыберите дату, чтобы отметить часы, когда вы свободны:",
                parseMode: ParseMode.Markdown,
                replyMarkup: AvailabilityMenu.GetDateCalendar(player.TimeZoneOffset),
                cancellationToken: ct);
        }
        
        // Внутри HandleAsync
        if (text.StartsWith("/plan"))
        {
            if (userId != _adminId) return;

            var groups = await db.Groups.ToListAsync(ct);
            if (groups.Count == 0)
            {
                await botClient.SendMessage(message.Chat.Id, "❌ Сначала создайте группу через /create_group", cancellationToken: ct);
                return;
            }

            var buttons = groups.Select(g => 
                new[] { InlineKeyboardButton.WithCallbackData(g.Name, $"start_plan_{g.Id}") });
    
            await botClient.SendMessage(
                message.Chat.Id,
                "🎯 **Запуск планирования**\nВыберите группу, для которой нужно найти время:",
                replyMarkup: new InlineKeyboardMarkup(buttons),
                cancellationToken: ct);
        }

    }
    
    private async Task HandleJoinGroup(Message message, Player player, CancellationToken ct)
    {
        // 1. Ищем группу, привязанную к этому чату
        var chatId = message.Chat.Id;
        var group = await db.Groups
            .Include(g => g.Players) // Загружаем список игроков группы
            .FirstOrDefaultAsync(g => g.TelegramChatId == chatId, ct);

        if (group == null)
        {
            await botClient.SendMessage(
                chatId,
                "❌ В этом чате еще не создана D&D группа. Попросите мастера использовать /create_group",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
            return;
        }

        // 2. Проверяем, не в группе ли уже игрок
        if (group.Players.Any(p => p.TelegramId == player.TelegramId))
        {
            await botClient.SendMessage(chatId, $"🛡 {player.Username}, вы уже состоите в группе '{group.Name}'!", cancellationToken: ct);
            return;
        }

        // 3. Добавляем игрока
        group.Players.Add(player);
        await db.SaveChangesAsync(ct);

        await botClient.SendMessage(chatId, $"🎲 Ура! {player.Username} присоединился к кампании '{group.Name}'!", cancellationToken: ct);
        logger.LogInformation("Игрок {User} вступил в группу {Group}", player.Username, group.Name);
    }


    private async Task FinalizeGroupCreation(Message message, Player player, CancellationToken ct)
    {
        var groupName = message.Text?.Trim();

        if (string.IsNullOrWhiteSpace(groupName))
        {
            await botClient.SendMessage(message.Chat.Id, "Название не может быть пустым. Введите еще раз:", cancellationToken: ct);
            return;
        }

        // Создаем группу
        var newGroup = new Group 
        { 
            Name = groupName, 
            TelegramChatId = message.Chat.Id 
        };

        // Сбрасываем стейт игрока
        player.CurrentState = null;
    
        db.Groups.Add(newGroup);
        await db.SaveChangesAsync(ct);

        await botClient.SendMessage(message.Chat.Id, $"✅ Группа **{groupName}** успешно создана!", cancellationToken: ct);
        logger.LogInformation("Админ {Id} создал группу {Name}", player.TelegramId, groupName);
    }
}