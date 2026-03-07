using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TgDataPlanner.Data;
using TgDataPlanner.Data.Entities;
using TgDataPlanner.Services;
using TgDataPlanner.Telegram.Menus;

namespace TgDataPlanner.Telegram.Handlers;

/// <summary>
/// Обработчик текстовых команд Telegram Bot.
/// Маршрутизирует входящие сообщения к соответствующим методам обработки команд.
/// </summary>
public class CommandHandler : BaseHandler
{
    private readonly ILogger<CommandHandler> _logger;

    /// <summary>
    /// Префиксы команд для маршрутизации.
    /// </summary>
    private static class Commands
    {
        public const string Start = "/start";
        public const string Group = "/group";
        public const string DeleteGroup = "/delgroup";
        public const string Join = "/join";
        public const string Leave = "/leave";
        public const string TimeZone = "/timezone";
        public const string Free = "/free";
        public const string Plan = "/plan";
        public const string Remind = "/remind";
    }

    /// <summary>
    /// Состояния машины состояний игрока.
    /// </summary>
    private static class PlayerStates
    {
        public const string AwaitingGroupName = "AwaitingGroupName";
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="CommandHandler"/>.
    /// </summary>
    /// <param name="config">Конфигурация приложения.</param>
    /// <param name="botClient">Клиент Telegram Bot API.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    /// <param name="db">Контекст базы данных</param>
    /// <param name="userService">Сервис управления пользователями.</param>
    /// <param name="schedulingService">Сервис планирования.</param>
    public CommandHandler(
        IConfiguration config,
        ITelegramBotClient botClient,
        ILogger<CommandHandler> logger,
        AppDbContext db,
        UserService userService,
        SchedulingService schedulingService)
        : base(config, botClient, logger, db, userService, schedulingService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Обрабатывает входящее текстовое сообщение.
    /// </summary>
    /// <param name="message">Объект сообщения Telegram.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения обработки.</returns>
    public async Task HandleAsync(Message message, CancellationToken ct)
    {
        if (message.Text is null)
        {
            _logger.LogDebug("Получено сообщение без текста от пользователя {UserId}", message.From?.Id);
            return;
        }

        var userId = message.From?.Id ?? 0;
        var text = message.Text.Trim();

        _logger.LogDebug(
            "Обработка команды '{Command}' от пользователя {UserId} в чате {ChatId}",
            text, userId, message.Chat.Id);

        // Получаем или создаём игрока через UserService
        var player = await GetOrCreatePlayerAsync(userId, message.From?.Username, ct);

        // Обновляем время активности при любом взаимодействии
        await TouchPlayerActivityAsync(userId, ct);

        // Обработка состояний (state machine)
        if (await HandleStateBasedInputAsync(message, player, text, ct))
        {
            return;
        }

        // Маршрутизация команд
        await RouteCommandAsync(message, player, text, userId, ct);
    }

    /// <summary>
    /// Обрабатывает ввод, зависящий от текущего состояния игрока.
    /// </summary>
    /// <returns>True, если ввод был обработан как состояние.</returns>
    private async Task<bool> HandleStateBasedInputAsync(
        Message message,
        Player player,
        string text,
        CancellationToken ct)
    {
        if (player.CurrentState != PlayerStates.AwaitingGroupName)
            return false;

        await FinalizeGroupCreationAsync(message, player, text, ct);
        return true;
    }

    /// <summary>
    /// Маршрутизирует команду к соответствующему обработчику.
    /// </summary>
    private async Task RouteCommandAsync(
        Message message,
        Player player,
        string text,
        long userId,
        CancellationToken ct)
    {
        switch (text)
        {
            case Commands.Start:
                await HandleStartCommandAsync(message, userId, ct);
                break;

            case Commands.Group:
                await HandleGroupCommandAsync(message, userId, ct);
                break;

            case Commands.DeleteGroup:
                await HandleDeleteGroupCommandAsync(message, userId, ct);
                break;

            case Commands.Join:
                await HandleJoinCommandAsync(message, ct);
                break;

            case Commands.Leave:
                await HandleLeaveCommandAsync(message, player, ct);
                break;

            case Commands.TimeZone:
                await HandleTimeZoneCommandAsync(message, ct);
                break;

            case Commands.Free:
                await HandleFreeCommandAsync(message, player, ct);
                break;

            case var _ when text.StartsWith(Commands.Plan):
                await HandlePlanCommandAsync(message, userId, ct);
                break;

            default:
                _logger.LogDebug("Неизвестная команда '{Command}' от пользователя {UserId}", text, userId);
                break;
        }
    }

    #region Обработчики команд

    /// <summary>
    /// Обрабатывает команду /start.
    /// </summary>
    private async Task HandleStartCommandAsync(Message message, long userId, CancellationToken ct)
    {
        var isAdmin = IsAdmin(userId);
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

        await SendTextAsync(message.Chat.Id, welcomeText + commands, ct: ct);
    }

    /// <summary>
    /// Обрабатывает команду /group (создание группы).
    /// </summary>
    private async Task HandleGroupCommandAsync(Message message, long userId, CancellationToken ct)
    {
        if (!IsAdmin(userId))
        {
            _logger.LogWarning("Пользователь {UserId} без прав попытался создать группу", userId);
            return;
        }

        // Устанавливаем состояние через UserService
        await SetPlayerStateAsync(userId, PlayerStates.AwaitingGroupName, ct);

        var keyboard = new InlineKeyboardMarkup((InlineKeyboardButton[])[
            InlineKeyboardButton.WithCallbackData("❌ Отмена", "cancel_action")
        ]);

        await SendTextAsync(
            message.Chat.Id,
            "📝 **Создание новой группы**\n\nВведите название для вашей D&D кампании:",
            replyMarkup: keyboard,
            ct: ct);
    }

    /// <summary>
    /// Обрабатывает команду /delgroup (удаление группы).
    /// </summary>
    private async Task HandleDeleteGroupCommandAsync(Message message, long userId, CancellationToken ct)
    {
        if (!IsAdmin(userId))
            return;

        var groups = await Db.Groups.ToListAsync(ct);

        if (groups.Count == 0)
        {
            await SendTextAsync(message.Chat.Id, "ℹ️ Групп для удаления пока нет.", ct: ct);
            return;
        }

        var buttons = groups.Select(g =>
            (List<InlineKeyboardButton>)[InlineKeyboardButton.WithCallbackData($"🗑 {g.Name}", $"confirm_delete_{g.Id}")]);

        await SendTextAsync(
            message.Chat.Id,
            "⚠️ **Удаление группы**\nВыберите группу, которую хотите расформировать:",
            replyMarkup: new InlineKeyboardMarkup(buttons),
            ct: ct);
    }

    /// <summary>
    /// Обрабатывает команду /join (вступление в группу).
    /// </summary>
    private async Task HandleJoinCommandAsync(Message message, CancellationToken ct)
    {
        var groups = await Db.Groups.ToListAsync(ct);

        if (groups.Count == 0)
        {
            await SendTextAsync(message.Chat.Id, "❌ Групп пока нет. Мастер должен создать их через /group", ct: ct);
            return;
        }

        var buttons = groups.Select(g =>
        {
            if (g.Name != null)
                return (List<InlineKeyboardButton>) [InlineKeyboardButton.WithCallbackData(g.Name, $"join_group_{g.Id}")];
            return null;
        });

        await SendTextAsync(
            message.Chat.Id,
            "📜 **Выберите группу для вступления:**",
            replyMarkup: new InlineKeyboardMarkup(buttons!),
            ct: ct);
    }

    /// <summary>
    /// Обрабатывает команду /leave (выход из группы).
    /// </summary>
    private async Task HandleLeaveCommandAsync(Message message, Player player, CancellationToken ct)
    {
        if (!player.Groups.Any())
        {
            await SendTextAsync(message.Chat.Id, "🛡 Вы пока не состоите ни в одной группе.", ct: ct);
            return;
        }

        var buttons = player.Groups.Select(g =>
            (List<InlineKeyboardButton>)[InlineKeyboardButton.WithCallbackData($"🚪 Покинуть {g.Name}", $"leave_group_{g.Id}")]);

        await SendTextAsync(
            message.Chat.Id,
            "🏃 **Выход из группы**\nВыберите группу, которую хотите покинуть:",
            replyMarkup: new InlineKeyboardMarkup(buttons),
            ct: ct);
    }

    /// <summary>
    /// Обрабатывает команду /timezone (настройка часового пояса).
    /// </summary>
    private async Task HandleTimeZoneCommandAsync(Message message, CancellationToken ct)
    {
        var keyboard = new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithCallbackData("UTC -1", "set_tz_-1"),
                InlineKeyboardButton.WithCallbackData("UTC +0", "set_tz_0"),
                InlineKeyboardButton.WithCallbackData("UTC +1", "set_tz_1")
            ],
            [
                InlineKeyboardButton.WithCallbackData("UTC +2", "set_tz_2"),
                InlineKeyboardButton.WithCallbackData("UTC +3 (МСК)", "set_tz_3"),
                InlineKeyboardButton.WithCallbackData("UTC +4 (ИЖ)", "set_tz_4")
            ],
            [
                InlineKeyboardButton.WithCallbackData("UTC +5", "set_tz_5"),
                InlineKeyboardButton.WithCallbackData("UTC +6", "set_tz_6"),
                InlineKeyboardButton.WithCallbackData("UTC +7", "set_tz_7")
            ]
        ]);

        await SendTextAsync(
            message.Chat.Id,
            "🌍 **Настройка часового пояса**\n\nВыберите ваше смещение относительно UTC (например, для Москвы это +3):",
            replyMarkup: keyboard,
            ct: ct);
    }

    /// <summary>
    /// Обрабатывает команду /free (отметка свободного времени).
    /// </summary>
    private async Task HandleFreeCommandAsync(Message message, Player player, CancellationToken ct)
    {
        try
        {
            await SendTextAsync(
                message.From!.Id,
                "📅 **Ваш личный календарь**\nВыберите дату, чтобы отметить свободные часы:",
                replyMarkup: AvailabilityMenu.GetDateCalendar(player.TimeZoneOffset),
                ct: ct);

            if (message.Chat.Type != ChatType.Private)
            {
                await SendTextAsync(
                    message.Chat.Id,
                    $"📩 {message.From.FirstName}, отправил календарь вам в личку!",
                    ct: ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Не удалось отправить календарь пользователю {UserId} в ЛС",
                message.From?.Id);

            await SendTextAsync(
                message.Chat.Id,
                $"❌ {message.From?.FirstName}, я не могу написать вам. Пожалуйста, начните со мной диалог в личке.",
                ct: ct);
        }
    }

    /// <summary>
    /// Обрабатывает команду /plan (поиск свободного времени для группы).
    /// </summary>
    private async Task HandlePlanCommandAsync(Message message, long userId, CancellationToken ct)
    {
        if (!IsAdmin(userId))
            return;

        var groups = await Db.Groups.ToListAsync(ct);

        if (groups.Count == 0)
        {
            await SendTextAsync(message.Chat.Id, "❌ Сначала создайте группу через /group", ct: ct);
            return;
        }

        var buttons = groups.Select(g =>
        {
            if (g.Name != null)
                return (List<InlineKeyboardButton>) [InlineKeyboardButton.WithCallbackData(g.Name, $"start_plan_{g.Id}")];
            return null;
        });

        await SendTextAsync(
            message.Chat.Id,
            "🎯 **Запуск планирования**\nВыберите группу, для которой нужно найти время:",
            replyMarkup: new InlineKeyboardMarkup(buttons!),
            ct: ct);
    }

    #endregion

    #region Вспомогательные методы

    /// <summary>
    /// Завершает создание группы после ввода названия.
    /// </summary>
    private async Task FinalizeGroupCreationAsync(Message message, Player player, string groupName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            await SendTextAsync(message.Chat.Id, "⚠️ Название не может быть пустым. Введите ещё раз:", ct: ct);
            return;
        }

        var newGroup = new Group
        {
            Name = groupName.Trim(),
            TelegramChatId = message.Chat.Id,
            CreatedAt = DateTime.UtcNow
        };

        // Сбрасываем состояние через UserService
        await SetPlayerStateAsync(player.TelegramId, null, ct);

        Db.Groups.Add(newGroup);
        await Db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Админ {AdminId} создал группу '{GroupName}' в чате {ChatId}",
            player.TelegramId, groupName, message.Chat.Id);

        await SendTextAsync(message.Chat.Id, $"✅ Группа **{groupName}** успешно создана!", ct: ct);
    }

    /// <summary>
    /// Получает список групп для отображения в меню.
    /// </summary>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Список групп.</returns>
    protected async Task<List<Group>> GetGroupsAsync(CancellationToken ct) =>
        await Db.Groups.ToListAsync(ct);

    /// <summary>
    /// Получает группу по идентификатору.
    /// </summary>
    /// <param name="groupId">Идентификатор группы.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Объект группы или null.</returns>
    private async Task<Group?> GetGroupAsync(int groupId, CancellationToken ct) =>
        await Db.Groups.FindAsync([groupId], ct);

    /// <summary>
    /// Создаёт новую группу в базе данных.
    /// </summary>
    /// <param name="name">Название группы.</param>
    /// <param name="telegramChatId">Идентификатор чата Telegram.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Созданная группа.</returns>
    protected async Task<Group> CreateGroupAsync(string name, long telegramChatId, CancellationToken ct)
    {
        var group = new Group(name, telegramChatId);
        Db.Groups.Add(group);
        await Db.SaveChangesAsync(ct);
        return group;
    }

    /// <summary>
    /// Удаляет группу из базы данных.
    /// </summary>
    /// <param name="groupId">Идентификатор группы.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>True, если группа удалена.</returns>
    protected async Task<bool> DeleteGroupAsync(int groupId, CancellationToken ct)
    {
        var group = await GetGroupAsync(groupId, ct);
        if (group is null)
            return false;

        Db.Groups.Remove(group);
        await Db.SaveChangesAsync(ct);
        return true;
    }

    #endregion
}