using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TgDataPlanner.Common;
using TgDataPlanner.Configuration;
using TgDataPlanner.Data;
using TgDataPlanner.Data.Entities;
using TgDataPlanner.Services;
using TgDataPlanner.Telegram.Menus;

namespace TgDataPlanner.Telegram.Handlers;

/// <summary>
/// Обработчик текстовых команд Telegram Bot.
/// Маршрутизирует входящие сообщения к соответствующим методам обработки команд.
/// Делегирует бизнес-логику сервисам планирования и рекомендаций.
/// </summary>
public class CommandHandler : BaseHandler
{
    private readonly SessionPlanningService _planningService;
    private readonly IRecommendationService _recommendationService;
    private readonly ILogger<CommandHandler> _logger;
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="CommandHandler"/>.
    /// </summary>
    /// <param name="config">Конфигурация приложения.</param>
    /// <param name="botClient">Клиент Telegram Bot API.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    /// <param name="db">Контекст базы данных</param>
    /// <param name="userService">Сервис управления пользователями.</param>
    /// <param name="schedulingService">Сервис расписания.</param>
    /// <param name="planningService">Сервис планирования игр.</param>
    /// <param name="recommendationService">Сервис рекомендаций.</param>
    public CommandHandler(
        IConfiguration config,
        ITelegramBotClient botClient,
        ILogger<CommandHandler> logger,
        AppDbContext db,
        UserService userService,
        SchedulingService schedulingService,
        SessionPlanningService planningService,
        IRecommendationService recommendationService)
        : base(config, botClient, logger, db, userService, schedulingService)
    {
        _planningService = planningService ?? throw new ArgumentNullException(nameof(planningService));
        _recommendationService = recommendationService ?? throw new ArgumentNullException(nameof(recommendationService));
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
        var text = message.Text.Trim().ToLowerInvariant();
        _logger.LogDebug(
            "Обработка команды '{Command}' от пользователя {UserId} в чате {ChatId}",
            text,
            userId,
            message.Chat.Id);
        var player = await GetOrCreatePlayerAsync(userId, message.From?.Username, ct);
        await TouchPlayerActivityAsync(userId, ct);
        if (await HandleStateBasedInputAsync(message, player, text, ct))
        {
            return;
        }
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
        if (player.CurrentState != PlayerState.AwaitingGroupName)
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
            case BotConstants.Commands.Start:
                await HandleStartCommandAsync(message, userId, ct);
                break;
            case BotConstants.Commands.Group:
                await HandleGroupCommandAsync(message, userId, ct);
                break;
            case BotConstants.Commands.DeleteGroup:
                await HandleDeleteGroupCommandAsync(message, userId, ct);
                break;
            case BotConstants.Commands.Join:
                await HandleJoinCommandAsync(message, ct);
                break;
            case BotConstants.Commands.Leave:
                await HandleLeaveCommandAsync(message, player, ct);
                break;
            case BotConstants.Commands.TimeZone:
                await HandleTimeZoneCommandAsync(message, ct);
                break;
            case BotConstants.Commands.Free:
                await HandleFreeCommandAsync(message, player, ct);
                break;
            case var _ when text.StartsWith(BotConstants.Commands.Plan):
                await HandlePlanCommandAsync(message, userId, ct);
                break;
            case BotConstants.Commands.Request:
                await HandleRequestCommandAsync(message, player, ct);
                break;
            case BotConstants.Commands.Status:
                await HandleStatusCommandAsync(message, player, ct);
                break;
            case BotConstants.Commands.Recommendations:
                await HandleRecommendationsCommandAsync(message, player, ct);
                break;
            case BotConstants.Commands.Cancel:
                await HandleCancelCommandAsync(message, player, ct);
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
            ? BotConstants.PlayerMessages.WelcomeAdmin
            : BotConstants.PlayerMessages.WelcomePlayer;
        var commands = new System.Text.StringBuilder();
        commands.AppendLine(BotConstants.Commands.CommandsList);
        if (isAdmin)
        {
            commands.AppendLine(BotConstants.Commands.AdminCommandsList);
            commands.AppendLine(BotConstants.Commands.ImportantNote);
        }
        commands.AppendLine(BotConstants.Commands.InDevelopment);
        await SendToGroupChatAsync(message.Chat.Id, welcomeText + commands, ct);
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
        await SetPlayerStateAsync(userId, PlayerState.AwaitingGroupName, ct);
        var keyboard = new InlineKeyboardMarkup((InlineKeyboardButton[])
        [
            InlineKeyboardButton.WithCallbackData(BotConstants.UiTexts.ButtonCancel, BotConstants.CallbackPrefixes.CancelAction)
        ]);
        await SendToGroupChatAsync(
            message.Chat.Id,
            BotConstants.CommandMessages.CreateGroupPrompt,
            keyboard,
            ct);
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
            await SendToGroupChatAsync(message.Chat.Id, BotConstants.CommandMessages.NoGroupsToDelete, ct);
            return;
        }
        var buttons = groups.Select(g =>
            (List<InlineKeyboardButton>) [InlineKeyboardButton.WithCallbackData($"🗑 {g.Name}", $"{BotConstants.CallbackPrefixes.ConfirmDeleteGroup}{g.Id}")]);
        await SendToGroupChatAsync(
            message.Chat.Id,
            BotConstants.CommandMessages.DeleteGroupPrompt,
            new InlineKeyboardMarkup(buttons),
            ct);
    }
    /// <summary>
    /// Обрабатывает команду /join (вступление в группу).
    /// </summary>
    private async Task HandleJoinCommandAsync(Message message, CancellationToken ct)
    {
        var groups = await Db.Groups.ToListAsync(ct);
        if (groups.Count == 0)
        {
            await SendToGroupChatAsync(message.Chat.Id, BotConstants.CommandMessages.NoGroupsToJoin, ct);
            return;
        }
        var buttons = groups.Select(g =>
        {
            if (g.Name != null)
                return (List<InlineKeyboardButton>) [InlineKeyboardButton.WithCallbackData(g.Name, $"{BotConstants.CallbackPrefixes.JoinGroup}{g.Id}")];
            return null;
        });
        await SendToGroupChatAsync(
            message.Chat.Id,
            BotConstants.CommandMessages.JoinGroupPrompt,
            new InlineKeyboardMarkup(buttons!),
            ct);
    }
    /// <summary>
    /// Обрабатывает команду /leave (выход из группы).
    /// </summary>
    private async Task HandleLeaveCommandAsync(Message message, Player player, CancellationToken ct)
    {
        if (!player.Groups.Any())
        {
            await SendToGroupChatAsync(message.Chat.Id, BotConstants.CommandMessages.NotInAnyGroup, ct);
            return;
        }
        var buttons = player.Groups.Select(g =>
            (List<InlineKeyboardButton>) [InlineKeyboardButton.WithCallbackData($"🚪 Покинуть {g.Name}", $"{BotConstants.CallbackPrefixes.LeaveGroup}{g.Id}")]);
        await SendToGroupChatAsync(
            message.Chat.Id,
            BotConstants.CommandMessages.LeaveGroupPrompt,
            new InlineKeyboardMarkup(buttons),
            ct);
    }
    /// <summary>
    /// Обрабатывает команду /timezone (настройка часового пояса).
    /// </summary>
    private async Task HandleTimeZoneCommandAsync(Message message, CancellationToken ct)
    {
        var keyboard = new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithCallbackData("UTC -1", $"{BotConstants.CallbackPrefixes.SetTimeZone}-1"),
                InlineKeyboardButton.WithCallbackData("UTC +0", $"{BotConstants.CallbackPrefixes.SetTimeZone}0"),
                InlineKeyboardButton.WithCallbackData("UTC +1", $"{BotConstants.CallbackPrefixes.SetTimeZone}1")
            ],
            [
                InlineKeyboardButton.WithCallbackData("UTC +2", $"{BotConstants.CallbackPrefixes.SetTimeZone}2"),
                InlineKeyboardButton.WithCallbackData("UTC +3 (МСК)", $"{BotConstants.CallbackPrefixes.SetTimeZone}3"),
                InlineKeyboardButton.WithCallbackData("UTC +4 (ИЖ)", $"{BotConstants.CallbackPrefixes.SetTimeZone}4")
            ],
            [
                InlineKeyboardButton.WithCallbackData("UTC +5", $"{BotConstants.CallbackPrefixes.SetTimeZone}5"),
                InlineKeyboardButton.WithCallbackData("UTC +6", $"{BotConstants.CallbackPrefixes.SetTimeZone}6"),
                InlineKeyboardButton.WithCallbackData("UTC +7", $"{BotConstants.CallbackPrefixes.SetTimeZone}7")
            ]
        ]);
        await SendToGroupChatAsync(
            message.Chat.Id,
            BotConstants.CommandMessages.TimeZonePrompt,
            keyboard,
            ct);
    }
    /// <summary>
    /// Обрабатывает команду /free (отметка свободного времени).
    /// </summary>
    private async Task HandleFreeCommandAsync(Message message, Player player, CancellationToken ct)
    {
        if (message.From is null)
            return;
        try
        {
            await SendToUserAsync(
                message.From.Id,
                BotConstants.CommandMessages.FreeTimePrompt,
                AvailabilityMenu.GetDateCalendar(player.TimeZoneOffset),
                ct);
            if (message.Chat.Type != ChatType.Private)
            {
                await SendToGroupChatAsync(
                    message.Chat.Id,
                    string.Format(BotConstants.CommandMessages.FreeTimeSentToPM, message.From.FirstName),
                    ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Не удалось отправить календарь пользователю {UserId} в ЛС",
                message.From.Id);
            await SendToGroupChatAsync(
                message.Chat.Id,
                string.Format(BotConstants.CommandMessages.FreeTimePmFailed, message.From.FirstName),
                ct);
        }
    }
    /// <summary>
    /// Обрабатывает команду /request (запрос свободного времени у игроков).
    /// </summary>
    private async Task HandleRequestCommandAsync(Message message, Player player, CancellationToken ct)
    {
        if (!IsAdmin(player.TelegramId))
            return;
        var groups = await Db.Groups.ToListAsync(ct);
        if (groups.Count == 0)
        {
            await SendToGroupChatAsync(message.Chat.Id, BotConstants.CommandMessages.NoGroupsForRequest, ct);
            return;
        }
        var buttons = groups.Select(g =>
        {
            if (g.Name != null)
                return (List<InlineKeyboardButton>) [InlineKeyboardButton.WithCallbackData(g.Name, $"{BotConstants.CallbackPrefixes.StartRequest}{g.Id}")];
            return null;
        });
        await SendToGroupChatAsync(
            message.Chat.Id,
            BotConstants.CommandMessages.RequestFreeTimePrompt,
            new InlineKeyboardMarkup(buttons!),
            ct);
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
            await SendToGroupChatAsync(message.Chat.Id, BotConstants.CommandMessages.NoGroupsForPlan, ct);
            return;
        }
        var buttons = groups.Select(g =>
        {
            if (g.Name != null)
                return (List<InlineKeyboardButton>) [InlineKeyboardButton.WithCallbackData(g.Name, $"{BotConstants.CallbackPrefixes.StartPlan}{g.Id}")];
            return null;
        });
        await SendToGroupChatAsync(
            message.Chat.Id,
            BotConstants.CommandMessages.PlanPrompt,
            new InlineKeyboardMarkup(buttons!),
            ct);
    }
    /// <summary>
    /// Обрабатывает команду /status (проверка статуса планирования).
    /// </summary>
    private async Task HandleStatusCommandAsync(Message message, Player player, CancellationToken ct)
    {
        var groups = player.Groups.ToList();
        if (groups.Count == 0)
        {
            await SendToGroupChatAsync(message.Chat.Id, BotConstants.CommandMessages.NoGroupsForStatus, ct);
            return;
        }
        var statusText = new System.Text.StringBuilder();
        statusText.AppendLine(BotConstants.CommandMessages.StatusTitle);
        foreach (var group in groups)
        {
            var freshGroup = await Db.Groups
                .Include(g => g.Players)
                .FirstOrDefaultAsync(g => g.Id == group.Id, ct);
            if (freshGroup == null)
                continue;
            statusText.AppendLine($"👥 **{freshGroup.Name}**");
            if (freshGroup.CurrentSessionUtc.HasValue)
            {
                var localTime = ConvertUtcToLocal(freshGroup.CurrentSessionUtc.Value, player.TimeZoneOffset);
                statusText.AppendLine(string.Format(BotConstants.CommandMessages.StatusSessionLine, localTime.ToString(BotConstants.DateFormats.FullLocalTimeFormat)));
                statusText.AppendLine(string.Format(BotConstants.CommandMessages.StatusStatusLine, freshGroup.SessionStatus));
                statusText.AppendLine(string.Format(BotConstants.CommandMessages.StatusConfirmedLine, freshGroup.ConfirmedPlayerIds.Count, freshGroup.Players.Count));
            }
            else
            {
                statusText.AppendLine(BotConstants.CommandMessages.StatusWaitingLine);
                statusText.AppendLine(string.Format(BotConstants.CommandMessages.StatusVotingLine, freshGroup.FinishedVotingPlayerIds.Count, freshGroup.Players.Count));
            }
            statusText.AppendLine();
        }
        await SendToGroupChatAsync(message.Chat.Id, statusText.ToString(), ct);
    }
    /// <summary>
    /// Обрабатывает команду /recommendations (ручной запуск рекомендаций).
    /// </summary>
    private async Task HandleRecommendationsCommandAsync(Message message, Player player, CancellationToken ct)
    {
        if (!IsAdmin(player.TelegramId))
        {
            await SendToGroupChatAsync(message.Chat.Id, BotConstants.CommandMessages.AdminOnlyRecommendations, ct);
            return;
        }
        var groups = await Db.Groups.ToListAsync(ct);
        if (groups.Count == 0)
        {
            await SendToGroupChatAsync(message.Chat.Id, BotConstants.CommandMessages.NoGroupsForRecommendations, ct);
            return;
        }
        var buttons = groups.Select(g =>
        {
            if (g.Name != null)
                return (List<InlineKeyboardButton>) [InlineKeyboardButton.WithCallbackData(g.Name, $"{BotConstants.CallbackPrefixes.StartPlan}{g.Id}")];
            return null;
        });
        await SendToGroupChatAsync(
            message.Chat.Id,
            BotConstants.CommandMessages.RecommendationsPrompt,
            new InlineKeyboardMarkup(buttons!),
            ct);
    }
    /// <summary>
    /// Обрабатывает команду /cancel (отмена сессии планирования).
    /// </summary>
    private async Task HandleCancelCommandAsync(Message message, Player player, CancellationToken ct)
    {
        if (!IsAdmin(player.TelegramId))
        {
            await SendToGroupChatAsync(message.Chat.Id, BotConstants.CommandMessages.AdminOnlyCancel, ct);
            return;
        }
        var groups = await Db.Groups
            .Where(g => g.SessionStatus == SessionStatus.Pending)
            .ToListAsync(ct);
        if (groups.Count == 0)
        {
            await SendToGroupChatAsync(message.Chat.Id, BotConstants.CommandMessages.NoActiveSessions, ct);
            return;
        }
        var buttons = groups.Select(g =>
            (List<InlineKeyboardButton>) [InlineKeyboardButton.WithCallbackData($"❌ {g.Name}", $"{BotConstants.CallbackPrefixes.ConfirmDeleteGroup}{g.Id}")]);
        await SendToGroupChatAsync(
            message.Chat.Id,
            BotConstants.CommandMessages.CancelSessionPrompt,
            new InlineKeyboardMarkup(buttons),
            ct);
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
            await SendToGroupChatAsync(message.Chat.Id, BotConstants.CommandMessages.GroupNameEmptyError, ct);
            return;
        }
        var newGroup = new Group
        {
            Name = groupName.Trim(),
            TelegramChatId = message.Chat.Id,
            CreatedAt = DateTime.UtcNow
        };
        await SetPlayerStateAsync(player.TelegramId, PlayerState.Idle, ct);
        Db.Groups.Add(newGroup);
        await Db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Админ {AdminId} создал группу '{GroupName}' в чате {ChatId}",
            player.TelegramId,
            groupName,
            message.Chat.Id);
        await SendToGroupChatAsync(message.Chat.Id, string.Format(BotConstants.AdminMessages.GroupCreated, groupName), ct);
    }
    /// <summary>
    /// Получает список групп для отображения в меню.
    /// </summary>
    protected async Task<List<Group>> GetGroupsAsync(CancellationToken ct) =>
        await Db.Groups.ToListAsync(ct);
    /// <summary>
    /// Получает группу по идентификатору.
    /// </summary>
    private async Task<Group?> GetGroupAsync(int groupId, CancellationToken ct) =>
        await Db.Groups.FindAsync([groupId], ct);
    /// <summary>
    /// Создаёт новую группу в базе данных.
    /// </summary>
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
