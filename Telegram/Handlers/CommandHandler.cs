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

public class CommandHandler(
    IConfiguration config,
    ITelegramBotClient botClient,
    ILogger<CommandHandler> logger,
    AppDbContext db,
    SchedulingService schedulingService) : BaseHandler(config, botClient, logger, db, schedulingService)
{
    private readonly ILogger<CommandHandler> _logger = logger;
    private readonly AppDbContext _db = db;

    public async Task HandleAsync(Message message, CancellationToken ct)
    {
        var userId = message.From?.Id ?? 0;
        var text = message.Text ?? "";

        // Находим или создаем игрока в БД, чтобы проверить его стейт
        var player = await _db.Players.FindAsync([userId], ct);
        if (player == null)
        {
            player = new Player { TelegramId = userId, Username = message.From?.Username ?? "Unknown" };
            _db.Players.Add(player);
            await _db.SaveChangesAsync(ct);
        }

        // 1. Если пользователь в процессе создания группы
        if (player.CurrentState == "AwaitingGroupName")
        {
            await FinalizeGroupCreation(message, player, ct);
            return;
        }

        if (text == "/start")
        {
            var isAdmin = userId == AdminId;
            var welcomeText = isAdmin 
                ? "🧙 **Приветствую, Великий Мастер!**\nЯ твой верный помощник в планировании сессий." 
                : "🛡 **Привет, Искатель Приключений!**\nЯ помогу твоей группе собраться на следующую игру.";

            var commands = new System.Text.StringBuilder();
            commands.AppendLine("\n**Доступные команды:**");
            commands.AppendLine("📅 /free — Отметить свое свободное время (в личке)");
            commands.AppendLine("🌍 /timezone — Настроить свой часовой пояс");
            commands.AppendLine("👥 /join — Вступить в группу (вызывать в чате группы)");
    
            if (isAdmin)
            {
                commands.AppendLine("\n**Команды Мастера:**");
                commands.AppendLine("/group — Создать новую группу");
                commands.AppendLine("/delgroup — Удалить группу");
                commands.AppendLine("/plan — Найти идеальное время для игры");
                commands.AppendLine("/remind — Напомнить ленивым игрокам о заполнении");
            }

            commands.AppendLine("\n**В разработке:**");
            commands.AppendLine("⏳ _Авто-напоминания за 5ч и 1ч до игры_");
            commands.AppendLine("🤖 _Умные рекомендации при отсутствии окон_");
            commands.AppendLine("📊 _Статус заполнения времени группой_");

            await BotClient.SendMessage(
                chatId: message.Chat.Id,
                text: welcomeText + commands.ToString(),
                parseMode: ParseMode.Markdown,
                cancellationToken: ct
            );
            return;
        }
        
        // 2. Обработка команд
        if (text == "/group")
        {
            if (userId != AdminId) return;

            player.CurrentState = "AwaitingGroupName";
            await _db.SaveChangesAsync(ct);

            // Создаем кнопку отмены
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                InlineKeyboardButton.WithCallbackData("❌ Отмена", "cancel_action")
            });

            await BotClient.SendMessage(
                chatId: message.Chat.Id, 
                text: "📝 **Создание новой группы**\n\nВведите название для вашей D&D кампании:",
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: ct);
        }
        if (text == "/delgroup")
        {
            if (userId != AdminId) return;

            var groups = await _db.Groups.ToListAsync(ct);
            var buttons = groups.Select(g => 
                new[] { InlineKeyboardButton.WithCallbackData($"🗑 {g.Name}", $"confirm_delete_{g.Id}") });

            await BotClient.SendMessage(
                message.Chat.Id,
                "⚠️ **Удаление группы**\nВыберите группу, которую хотите расформировать:",
                replyMarkup: new InlineKeyboardMarkup(buttons),
                cancellationToken: ct);
        }

        
        if (text == "/join")
        {
            var groups = await _db.Groups.ToListAsync(ct);
            if (!groups.Any())
            {
                await BotClient.SendMessage(message.Chat.Id, "❌ Групп пока нет. Мастер должен создать их через /create_group", cancellationToken: ct);
                return;
            }

            var buttons = groups.Select(g => 
                new[] { InlineKeyboardButton.WithCallbackData(g.Name, $"join_group_{g.Id}") });

            await BotClient.SendMessage(
                chatId: message.Chat.Id,
                text: "📜 **Выберите группу для вступления:**",
                replyMarkup: new InlineKeyboardMarkup(buttons),
                cancellationToken: ct);
        }

        if (text == "/leave")
        {
            if (player == null || !player.Groups.Any())
            {
                await BotClient.SendMessage(message.Chat.Id, "🛡 Вы пока не состоите ни в одной группе.", cancellationToken: ct);
                return;
            }

            var buttons = player.Groups.Select(g => 
                new[] { InlineKeyboardButton.WithCallbackData($"🚪 Покинуть {g.Name}", $"leave_group_{g.Id}") });

            await BotClient.SendMessage(
                chatId: message.Chat.Id,
                text: "🏃 **Выход из группы**\nВыберите группу, которую хотите покинуть:",
                replyMarkup: new InlineKeyboardMarkup(buttons),
                cancellationToken: ct);
        }


        
        if (text == "/timezone")
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("UTC -1", "set_tz_-1"), InlineKeyboardButton.WithCallbackData("UTC +0", "set_tz_0"), InlineKeyboardButton.WithCallbackData("UTC +1", "set_tz_1") },
                new[] { InlineKeyboardButton.WithCallbackData("UTC +2", "set_tz_2"), InlineKeyboardButton.WithCallbackData("UTC +3 (МСК)", "set_tz_3"), InlineKeyboardButton.WithCallbackData("UTC +4 (ИЖ)", "set_tz_4") },
                new[] { InlineKeyboardButton.WithCallbackData("UTC +5", "set_tz_5"), InlineKeyboardButton.WithCallbackData("UTC +6", "set_tz_6"), InlineKeyboardButton.WithCallbackData("UTC +7", "set_tz_7") }
            });

            await BotClient.SendMessage(
                message.Chat.Id, 
                "🌍 **Настройка часового пояса**\n\nВыберите ваше смещение относительно UTC (например, для Москвы это +3):",
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: ct);
        }
        
        if (text == "/free")
        {
            try 
            {
                // Отправляем сообщение напрямую пользователю (по его TelegramId)
                await BotClient.SendMessage(
                    chatId: message.From!.Id, 
                    text: "📅 **Ваш личный календарь**\nВыберите дату, чтобы отметить свободные часы:",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: AvailabilityMenu.GetDateCalendar(player.TimeZoneOffset),
                    cancellationToken: ct);

                // Если команда была в группе, даем подтверждение там
                if (message.Chat.Type != ChatType.Private)
                {
                    await BotClient.SendMessage(message.Chat.Id, $"📩 {message.From.FirstName}, отправил календарь вам в личку!", cancellationToken: ct);
                }
            }
            catch (Exception ex)
            {
                // Если пользователь не заблокировал бота или еще не писал ему
                await BotClient.SendMessage(message.Chat.Id, $"❌ {message.From?.FirstName}, я не могу написать вам. Пожалуйста, начните со мной диалог в личке (@relaxerr_dnd_helper_bot).", cancellationToken: ct);
            }
        }
        
        // Внутри HandleAsync
        if (text.StartsWith("/plan"))
        {
            if (userId != AdminId) return;

            var groups = await _db.Groups.ToListAsync(ct);
            if (groups.Count == 0)
            {
                await BotClient.SendMessage(message.Chat.Id, "❌ Сначала создайте группу через /group", cancellationToken: ct);
                return;
            }

            var buttons = groups.Select(g => 
                new[] { InlineKeyboardButton.WithCallbackData(g.Name, $"start_plan_{g.Id}") });
    
            await BotClient.SendMessage(
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
        var group = await _db.Groups
            .Include(g => g.Players) // Загружаем список игроков группы
            .FirstOrDefaultAsync(g => g.TelegramChatId == chatId, ct);

        if (group == null)
        {
            await BotClient.SendMessage(
                chatId,
                "❌ В этом чате еще не создана D&D группа. Попросите мастера использовать /group",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
            return;
        }

        // 2. Проверяем, не в группе ли уже игрок
        if (group.Players.Any(p => p.TelegramId == player.TelegramId))
        {
            await BotClient.SendMessage(chatId, $"🛡 {player.Username}, вы уже состоите в группе '{group.Name}'!", cancellationToken: ct);
            return;
        }

        // 3. Добавляем игрока
        group.Players.Add(player);
        await _db.SaveChangesAsync(ct);

        await BotClient.SendMessage(chatId, $"🎲 Ура! {player.Username} присоединился к кампании '{group.Name}'!", cancellationToken: ct);
        _logger.LogInformation("Игрок {User} вступил в группу {Group}", player.Username, group.Name);
    }


    private async Task FinalizeGroupCreation(Message message, Player player, CancellationToken ct)
    {
        var groupName = message.Text?.Trim();

        if (string.IsNullOrWhiteSpace(groupName))
        {
            await BotClient.SendMessage(message.Chat.Id, "Название не может быть пустым. Введите еще раз:", cancellationToken: ct);
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
    
        _db.Groups.Add(newGroup);
        await _db.SaveChangesAsync(ct);

        await BotClient.SendMessage(message.Chat.Id, $"✅ Группа **{groupName}** успешно создана!", cancellationToken: ct);
        _logger.LogInformation("Админ {Id} создал группу {Name}", player.TelegramId, groupName);
    }
}