using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TgDataPlanner.AI;
using TgDataPlanner.Common;
using TgDataPlanner.Configuration;
using TgDataPlanner.Data;
using TgDataPlanner.Data.Entities;
using TgDataPlanner.Services;
using TgDataPlanner.Telegram.Menus;

namespace TgDataPlanner.Telegram.Handlers;

/// <summary>
/// Обработчик нажатий на inline-кнопки (CallbackQuery).
/// Маршрутизирует колбэки к соответствующим методам и управляет взаимодействием с пользователем.
/// Делегирует бизнес-логику сервисам планирования, RSVP и уведомлений.
/// </summary>
public class CallbackHandler : BaseHandler
{
    private readonly SessionPlanningService _planningService;
    private readonly RsvpService _rsvpService;
    private readonly GroupNotificationService _notificationService;
    private readonly ILogger<CallbackHandler> _logger;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="CallbackHandler"/>.
    /// </summary>
    /// <param name="config">Конфигурация приложения.</param>
    /// <param name="botClient">Клиент Telegram Bot API.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    /// <param name="db">Контекст базы данных.</param>
    /// <param name="userService">Сервис управления пользователями.</param>
    /// <param name="schedulingService">Сервис расписания.</param>
    /// <param name="planningService">Сервис планирования.</param>
    /// <param name="rsvpService">Сервис RSVP.</param>
    /// <param name="notificationService">Сервис уведомлений.</param>
    /// <param name="ollamaService">Сервис ИИ</param>
    public CallbackHandler(
        IConfiguration config,
        ITelegramBotClient botClient,
        ILogger<CallbackHandler> logger,
        AppDbContext db,
        UserService userService,
        SchedulingService schedulingService,
        SessionPlanningService planningService,
        RsvpService rsvpService,
        GroupNotificationService notificationService,
        OllamaService ollamaService)
        : base(config, botClient, logger, db, userService, schedulingService, ollamaService)
    {
        _planningService = planningService ?? throw new ArgumentNullException(nameof(planningService));
        _rsvpService = rsvpService ?? throw new ArgumentNullException(nameof(rsvpService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Обрабатывает запрос обратного вызова от Telegram.
    /// </summary>
    /// <param name="callbackQuery">Запрос обратного вызова.</param>
    /// <param name="ct">Токен отмены операции.</param>
    public async Task HandleAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        if (callbackQuery.Data is null)
        {
            _logger.LogWarning(BotConstants.CallbackHandlerLogs.CallbackNullData, callbackQuery.From.Id);
            await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
            return;
        }

        var userId = callbackQuery.From.Id;
        _logger.LogDebug(BotConstants.CallbackHandlerLogs.CallbackProcessingLog, callbackQuery.Data, userId);

        try
        {
            await RouteCallbackAsync(callbackQuery, userId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug(BotConstants.CallbackHandlerLogs.CallbackCancelled);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                BotConstants.CallbackHandlerLogs.CallbackError,
                callbackQuery.Data,
                userId);
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.GenericError, true, ct);
        }
    }

    /// <summary>
    /// Маршрутизирует callback-запрос к соответствующему обработчику.
    /// </summary>
    private async Task RouteCallbackAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var data = callbackQuery.Data;
        if (data is null)
        {
            _logger.LogError(BotConstants.CallbackHandlerLogs.CallbackRouteNullData, userId);
            return;
        }

        if (IsConfirmTimeCallback(data) && !IsAdmin(userId))
        {
            await HandleAdminOnlyActionAsync(callbackQuery, ct);
            return;
        }

        var player = await UserService.GetPlayerAsync(userId, ct);
        if (player == null)
        {
            Logger.LogWarning(BotConstants.CallbackHandlerLogs.PlayerNotFoundCallback, userId, data);
            return;
        }

        switch (data)
        {
            case BotConstants.CallbackPrefixes.CancelAction:
                await HandleCancelActionAsync(callbackQuery, userId, ct);
                break;
            case var _ when data.StartsWith(BotConstants.CallbackPrefixes.SetTimeZone):
                await HandleSetTimeZoneAsync(callbackQuery, userId, ct);
                break;
            case var _ when data.StartsWith(BotConstants.CallbackPrefixes.PickDate):
                await HandlePickDateAsync(callbackQuery, userId, ct);
                break;
            case var _ when data.StartsWith(BotConstants.CallbackPrefixes.ToggleTime):
                await HandleToggleTimeAsync(callbackQuery, userId, ct);
                break;
            case BotConstants.CallbackPrefixes.BackToDates:
                await HandleBackToDatesAsync(callbackQuery, userId, ct);
                break;
            case var _ when data.StartsWith(BotConstants.CallbackPrefixes.StartPlan):
                await HandleStartPlanningAsync(callbackQuery, userId, ct);
                break;
            case BotConstants.CallbackPrefixes.FinishVoting:
                await HandleFinishVotingAsync(callbackQuery, userId, ct);
                break;
            case var _ when data.StartsWith(BotConstants.CallbackPrefixes.JoinGroup):
                await HandleJoinGroupAsync(callbackQuery, userId, ct);
                break;
            case var _ when data.StartsWith(BotConstants.CallbackPrefixes.ConfirmDeleteGroup):
                await HandleConfirmDeleteGroupAsync(callbackQuery, userId, ct);
                break;
            case var _ when data.StartsWith(BotConstants.CallbackPrefixes.LeaveGroup):
                await HandleLeaveGroupAsync(callbackQuery, userId, ct);
                break;
            case var _ when data.StartsWith(BotConstants.CallbackPrefixes.ConfirmTime):
                await HandleConfirmTimeAsync(callbackQuery, player, ct);
                break;
            case var _ when data.StartsWith(BotConstants.CallbackPrefixes.RsvpYes):
                await HandleRsvpAsync(callbackQuery, userId, true, ct);
                break;
            case var _ when data.StartsWith(BotConstants.CallbackPrefixes.RsvpNo):
                await HandleRsvpAsync(callbackQuery, userId, false, ct);
                break;
            case var _ when data.StartsWith(BotConstants.CallbackPrefixes.StartRequest):
                await HandleStartRequestAsync(callbackQuery, userId, ct);
                break;
            case var _ when data.StartsWith(BotConstants.CallbackPrefixes.SelectRecommendation):
                await HandleSelectRecommendationAsync(callbackQuery, userId, ct);
                break;
            case var _ when data.StartsWith(BotConstants.CallbackPrefixes.ViewGroupMembers):
                await HandleViewGroupMembersAsync(callbackQuery, userId, ct);
                break;
            case var _ when data.StartsWith(BotConstants.CallbackPrefixes.ViewSessionInfo):
                await HandleViewSessionInfoAsync(callbackQuery, userId, ct);
                break;
            case BotConstants.CallbackPrefixes.StartMenuFree:
                await HandleStartMenuFreeAsync(callbackQuery, userId, ct);
                break;
            case BotConstants.CallbackPrefixes.StartMenuTimeZone:
                await HandleStartMenuTimeZoneAsync(callbackQuery, userId, ct);
                break;
            case BotConstants.CallbackPrefixes.StartMenuStatus:
                await HandleStartMenuStatusAsync(callbackQuery, userId, ct);
                break;
            case BotConstants.CallbackPrefixes.StartMenuJoin:
                await HandleStartMenuJoinAsync(callbackQuery, userId, ct);
                break;
            case BotConstants.CallbackPrefixes.StartMenuPlan:
                await HandleStartMenuPlanAsync(callbackQuery, userId, ct);
                break;
            case BotConstants.CallbackPrefixes.StartMenuHelp:
                await HandleStartMenuHelpAsync(callbackQuery, userId, ct);
                break;
            case BotConstants.CallbackPrefixes.ShowHelpMenu:
                await HandleShowHelpMenuAsync(callbackQuery, userId, ct);
                break;
            default:
                _logger.LogWarning(BotConstants.CallbackHandlerLogs.UnknownCallback, data);
                await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
                break;
        }
    }

    /// <summary>
    /// Проверяет, является ли колбэк подтверждением времени.
    /// </summary>
    private static bool IsConfirmTimeCallback(string data) =>
        data.StartsWith(BotConstants.CallbackPrefixes.ConfirmTime);

    /// <summary>
    /// Обрабатывает попытку выполнения админ-действия не-администратором.
    /// </summary>
    private async Task HandleAdminOnlyActionAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        _logger.LogWarning(
            BotConstants.CallbackHandlerLogs.AdminActionAttempt,
            callbackQuery.From.Id,
            callbackQuery.Data);
        await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.AdminMessages.AdminOnlyAction, true, ct);
    }

    /// <summary>
    /// Обрабатывает выбор админом конкретного времени для сессии и публикует анонс.
    /// </summary>
    private async Task HandleConfirmTimeAsync(CallbackQuery callbackQuery, Player player, CancellationToken ct)
    {
        var admin = await UserService.GetPlayerAsync(AdminIds.FirstOrDefault(), ct);
        if (player.TelegramId != admin?.TelegramId)
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.AdminMessages.AdminOnlyAction, true, ct);
            return;
        }

        if (!TryParseConfirmTimeCallback(callbackQuery.Data, out var groupId, out var sessionTimeUtc))
        {
            _logger.LogError(BotConstants.ErrorMessages.TimeParseError, callbackQuery.Data);
            return;
        }

        var group = await GetGroupAsync(groupId, ct);
        if (group == null)
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.GroupNotFound, true, ct);
            return;
        }

        SessionPlanningService.UpdateGroupSessionData(group, sessionTimeUtc);
        await Db.SaveChangesAsync(ct);

        await NotifyGroupAboutSessionAsync(group, admin, ct);
        await _notificationService.EditTextAsync(callbackQuery, FormatSessionConfirmationText(group.Name, sessionTimeUtc, admin), ct: ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Парсит данные колбэка подтверждения времени.
    /// </summary>
    private bool TryParseConfirmTimeCallback(string? data, out int groupId, out DateTime sessionTimeUtc)
    {
        groupId = 0;
        sessionTimeUtc = default;
        if (string.IsNullOrEmpty(data))
            return false;

        var dataParts = data.Split('_');
        if (dataParts.Length < 4 || !int.TryParse(dataParts[2], out groupId))
            return false;

        var timeRaw = dataParts[3];
        return DateTime.TryParseExact(timeRaw, BotConstants.DateFormats.DateTimeCallbackFormat, null, System.Globalization.DateTimeStyles.None, out sessionTimeUtc);
    }

    /// <summary>
    /// Форматирует текст подтверждения сессии для редактирования сообщения.
    /// </summary>
    private string FormatSessionConfirmationText(string? groupName, DateTime sessionTimeUtc, Player? admin)
    {
        var localTime = ConvertUtcToLocal(sessionTimeUtc, admin?.TimeZoneOffset ?? 0);
        var escapedName = BotConstants.TextHelpers.EscapeMarkdown(groupName ?? BotConstants.CallbackHandlerMessages.UnknownGroup);
        return string.Format(
            BotConstants.CallbackHandlerMessages.SessionConfirmationText,
            escapedName,
            localTime.ToString(BotConstants.DateFormats.LocalTimeFormat));
    }

    /// <summary>
    /// Отправляет уведомление всем игрокам группы о назначенной сессии.
    /// </summary>
    private async Task NotifyGroupAboutSessionAsync(Group group, Player? admin, CancellationToken ct)
    {
        var localTime = ConvertUtcToLocal(group.CurrentSessionUtc!.Value, admin?.TimeZoneOffset ?? 0);
        var rsvpKeyboard = GroupNotificationService.CreateRsvpKeyboard(group.Id);
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup);
        var dateStr = localTime.ToString(BotConstants.DateFormats.FullLocalTimeFormat);
        var timeStr = localTime.ToString("HH:mm");
        // Формируем список участников группы
        var playerList = string.Join("\n", group.Players.Select(p => $"• {p.GetMarkdownUsername()}"));
        var announcementText = string.Format(
            BotConstants.PlayerMessages.SessionAnnouncementWithPlayers,
            escapedGroupName,
            dateStr,
            timeStr,
            playerList);
        // Отправляем в группу
        await _notificationService.SendToGroupChatAsync(group.TelegramChatId, announcementText, rsvpKeyboard, ct);
        // Отправляем каждому игроку в ЛС с учётом его часового пояса
        foreach (var player in group.Players)
        {
            try
            {
                var playerLocalTime = ConvertUtcToLocal(group.CurrentSessionUtc.Value, player.TimeZoneOffset);
                var playerDateStr = playerLocalTime.ToString(BotConstants.DateFormats.FullLocalTimeFormat);
                var pmText = string.Format(
                    BotConstants.PlayerMessages.AutoSessionAnnouncementPM,
                    escapedGroupName,
                    playerDateStr,
                    playerLocalTime.ToString("HH:mm"),
                    timeStr,
                    BotConstants.DefaultSessionDurationHours);
                await _notificationService.SendToUserAsync(player.TelegramId, pmText, rsvpKeyboard, ct);
                _logger.LogDebug(BotConstants.CallbackHandlerLogs.RsvpSentDebug, player.TelegramId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, BotConstants.CallbackHandlerLogs.RsvpSendFailed, player.TelegramId);
            }
        }
    }

    /// <summary>
    /// Обрабатывает ответ игрока (подтверждение или отказ) на анонс сессии.
    /// </summary>
    private async Task HandleRsvpAsync(CallbackQuery callbackQuery, long userId, bool isComing, CancellationToken ct)
    {
        var prefix = isComing ? BotConstants.CallbackPrefixes.RsvpYes : BotConstants.CallbackPrefixes.RsvpNo;
        if (!TryParseGroupIdFromCallback(callbackQuery.Data, prefix, out var groupId))
        {
            _logger.LogWarning(BotConstants.CallbackHandlerLogs.RsvpInvalidGroupId, callbackQuery.Data);
            return;
        }

        var group = await GetGroupWithPlayersAsync(groupId, ct);
        var player = await Db.Players.FindAsync([userId], ct);
        if (group == null || player == null)
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.RsvpError, true, ct);
            return;
        }

        if (group.SessionStatus != SessionStatus.Pending)
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.PlayerMessages.RsvpStatusFixed, true, ct);
            return;
        }

        RsvpService.UpdatePlayerRsvpStatus(group, userId, isComing);
        await Db.SaveChangesAsync(ct);

        var participationRate = _rsvpService.CalculateParticipationRate(group);
        await LogRsvpStatusAsync(group, participationRate);
        await UpdateCallbackResponseAsync(callbackQuery, isComing, ct);

        if (_rsvpService.AreAllPlayersResponded(group))
        {
            _logger.LogInformation(BotConstants.CallbackHandlerLogs.AllPlayersResponded);
            await FinalizeSessionDecisionAsync(group, participationRate, ct);
        }
    }

    /// <summary>
    /// Парсит ID группы из колбэка RSVP.
    /// </summary>
    private bool TryParseGroupIdFromCallback(string? data, string prefix, out int groupId)
    {
        groupId = 0;
        if (string.IsNullOrEmpty(data))
            return false;

        var groupIdString = data.Replace(prefix, string.Empty);
        return int.TryParse(groupIdString, out groupId);
    }

    /// <summary>
    /// Логирует статус RSVP для группы.
    /// </summary>
    private Task LogRsvpStatusAsync(Group group, double participationRate)
    {
        var allPlayers = group.Players.DistinctBy(p => p.TelegramId).ToList();
        var totalPlayers = allPlayers.Count;
        var confirmedCount = group.ConfirmedPlayerIds.Count;
        var respondedCount = group.ConfirmedPlayerIds.Count + group.DeclinedPlayerIds.Count;

        _logger.LogInformation(
            BotConstants.CallbackHandlerLogs.RsvpStatusLog,
            group.Id,
            confirmedCount,
            totalPlayers,
            participationRate,
            respondedCount,
            totalPlayers);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Обновляет ответ на callback в зависимости от выбора игрока.
    /// </summary>
    private async Task UpdateCallbackResponseAsync(CallbackQuery callbackQuery, bool isComing, CancellationToken ct)
    {
        var responseText = isComing
            ? BotConstants.PlayerMessages.RsvpConfirmed
            : BotConstants.PlayerMessages.RsvpDeclined;

        await _notificationService.EditTextAsync(callbackQuery, responseText, ct: ct);
        await _notificationService.EditReplyMarkupAsync(callbackQuery, null, ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Принимает решение о проведении сессии на основе процента подтверждений и присутствия админов.
    /// </summary>
    private async Task FinalizeSessionDecisionAsync(Group group, double participationRate, CancellationToken ct)
    {
        var result = await _rsvpService.FinalizeSessionDecisionAsync(group, participationRate, ct);
        switch (result.Status)
        {
            case SessionStatus.Confirmed:
                await NotifyAboutSessionConfirmationAsync(group, result, ct);
                break;
            case SessionStatus.Rescheduled:
                await NotifyAboutSessionRescheduleAsync(group, result, ct);
                await AutoRunPlanningForGroupAsync(group, ct);
                break;
            case SessionStatus.Cancelled:
                await NotifyAboutSessionCancellationAsync(group, result, ct);
                break;
        }
    }

    /// <summary>
    /// Уведомляет о подтверждении сессии.
    /// </summary>
    private async Task NotifyAboutSessionConfirmationAsync(Group group, SessionFinalizationResult result, CancellationToken ct)
    {
        var adminInGroup = GetAdminsInGroup(group).FirstOrDefault();
        var localTime = ConvertUtcToLocal(group.CurrentSessionUtc!.Value, adminInGroup?.TimeZoneOffset ?? 0);
        // Формируем список участников
        var playerList = string.Join("\n", group.Players.Select(p => $"• {p.GetMarkdownUsername()}"));
        var adminNotice = GetAdminsInGroup(group).Count > 0
            ? BotConstants.CallbackHandlerMessages.AllAdminsCanAttend
            : BotConstants.CallbackHandlerMessages.NoAdminsInGroup;
        var notificationText = string.Format(
            BotConstants.AdminMessages.SessionConfirmedWithPlayers,
            BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup),
            localTime.ToString(BotConstants.DateFormats.FullLocalTimeFormat),
            localTime.ToString("HH:mm"),
            playerList,
            result.ConfirmedCount,
            result.TotalPlayers,
            result.ParticipationRate,
            adminNotice);
        await _notificationService.NotifyMainChatAsync(notificationText, ct: ct);
        // Отправляем подтверждение каждому игроку в ЛС
        foreach (var player in group.Players)
        {
            try
            {
                var playerLocalTime = ConvertUtcToLocal(group.CurrentSessionUtc.Value, player.TimeZoneOffset);
                var isConfirmed = group.ConfirmedPlayerIds.Contains(player.TelegramId);
                if (isConfirmed)
                {
                    var pmText = string.Format(
                        BotConstants.PlayerMessages.SessionConfirmedPM,
                        BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup),
                        playerLocalTime.ToString(BotConstants.DateFormats.FullLocalTimeFormat),
                        playerLocalTime.ToString("HH:mm"),
                        localTime.ToString("HH:mm"));
                    await _notificationService.SendToUserAsync(player.TelegramId, pmText, ct: ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось отправить подтверждение игроку {UserId}", player.TelegramId);
            }
        }
        _logger.LogInformation(BotConstants.CallbackHandlerLogs.SessionConfirmedLog, group.Name, result.ParticipationRate);
    }

    /// <summary>
    /// Уведомляет о перепланировании сессии.
    /// </summary>
    private async Task NotifyAboutSessionRescheduleAsync(Group group, SessionFinalizationResult result, CancellationToken ct)
    {
        var adminsText = string.Join(", ", result.CannotAttendAdmins.Select(a => a.GetMarkdownUsername()));
        var reason = string.Format(BotConstants.CallbackHandlerMessages.AdminsCannotAttend, adminsText);
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup);

        var notificationText = string.Format(
            BotConstants.AdminMessages.SessionRescheduled,
            escapedGroupName,
            reason,
            result.ConfirmedCount,
            result.TotalPlayers,
            result.ParticipationRate,
            BotConstants.ConfirmationThreshold);

        var retryKeyboard = new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData(BotConstants.UiTexts.ButtonRetryRequest, $"{BotConstants.CallbackPrefixes.StartRequest}{group.Id}")]
        ]);

        await _notificationService.SendToMainAdminAsync(notificationText, retryKeyboard, ct);
        await _notificationService.NotifyMainChatAsync(notificationText, ct: ct);
        _logger.LogInformation(BotConstants.CallbackHandlerLogs.SessionRescheduledLog, group.Name);
    }

    /// <summary>
    /// Уведомляет об отмене сессии.
    /// </summary>
    private async Task NotifyAboutSessionCancellationAsync(Group group, SessionFinalizationResult result, CancellationToken ct)
    {
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup);

        var notificationText = string.Format(
            BotConstants.CallbackHandlerMessages.SessionCancelledAdminText,
            escapedGroupName,
            result.ConfirmedCount,
            result.TotalPlayers,
            result.ParticipationRate,
            BotConstants.ConfirmationThreshold);

        var retryKeyboard = new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData(BotConstants.UiTexts.ButtonRetryRequest, $"{BotConstants.CallbackPrefixes.StartRequest}{group.Id}")]
        ]);

        await _notificationService.SendToMainAdminAsync(notificationText, retryKeyboard, ct);
        await _notificationService.NotifyMainChatAsync(
            string.Format(BotConstants.AdminMessages.SessionCancelled, result.ParticipationRate),
            ct: ct);

        _logger.LogInformation(BotConstants.CallbackHandlerLogs.SessionCancelledLog, group.Name, result.ParticipationRate);
    }

    /// <summary>
    /// Обрабатывает выбор варианта рекомендации администратором.
    /// </summary>
    private async Task HandleSelectRecommendationAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!IsAdmin(userId))
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.AdminMessages.AdminOnlySelectRecommendation, true, ct);
            return;
        }

        if (!TryParseRecommendationCallback(callbackQuery.Data, out var groupId, out var optionIndex))
        {
            _logger.LogWarning(BotConstants.CallbackHandlerLogs.RecommendationInvalidFormat, callbackQuery.Data);
            return;
        }

        var group = await GetGroupWithPlayersAsync(groupId, ct);
        if (group == null)
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.GroupNotFound, true, ct);
            return;
        }

        var recommendationResult = await _planningService.GetRecommendationsForGroupAsync(group, ct);
        if (!recommendationResult.HasRecommendations || optionIndex >= recommendationResult.Options.Count)
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.RecommendationUnavailable, true, ct);
            return;
        }

        var selectedOption = recommendationResult.Options[optionIndex];

        // Исправление: сохраняем UTC время как есть, без конвертации
        SessionPlanningService.UpdateGroupSessionData(group, selectedOption.ProposedStartTime);
        await Db.SaveChangesAsync(ct);

        await NotifyGroupAboutSelectedRecommendationAsync(group, selectedOption, optionIndex, ct);
        await _notificationService.EditTextAsync(callbackQuery, await FormatRecommendationSelectionText(group.Name, selectedOption, optionIndex, userId), ct: ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Парсит данные колбэка выбора рекомендации.
    /// </summary>
    private bool TryParseRecommendationCallback(string? data, out int groupId, out int optionIndex)
    {
        groupId = 0;
        optionIndex = -1;
        if (string.IsNullOrEmpty(data))
            return false;

        var dataParts = data.Split('_');
        if (dataParts.Length < 4)
            return false;

        return int.TryParse(dataParts[2], out groupId) && int.TryParse(dataParts[3], out optionIndex);
    }

    /// <summary>
    /// Форматирует текст выбора рекомендации для отображения.
    /// </summary>
    private async Task<string> FormatRecommendationSelectionText(string? groupName, RecommendationOption option, int optionIndex, long requestingUserId)
    {
        // Исправление: используем часовой пояс того, кто запросил рекомендацию
        var requestingPlayer = await UserService.GetPlayerAsync(requestingUserId, CancellationToken.None);
        var localStart = ConvertUtcToLocal(option.ProposedStartTime, requestingPlayer?.TimeZoneOffset ?? 0);

        BotConstants.TextHelpers.EscapeMarkdown(groupName ?? BotConstants.CallbackHandlerMessages.UnknownGroup);
        return string.Format(
            BotConstants.CallbackHandlerMessages.RecommendationSelectionText,
            optionIndex + 1,
            localStart.ToString(BotConstants.DateFormats.LocalTimeFormat));
    }

    /// <summary>
    /// Отправляет уведомление группе о выбранном варианте рекомендации.
    /// </summary>
    private async Task NotifyGroupAboutSelectedRecommendationAsync(Group group, RecommendationOption option, int optionIndex, CancellationToken ct)
    {
        // Исправление: используем часовой пояс первого админа в группе для консистентности
        var adminInGroup = GetAdminsInGroup(group).FirstOrDefault();
        var localStart = ConvertUtcToLocal(option.ProposedStartTime, adminInGroup?.TimeZoneOffset ?? 0);

        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup);
        var dateStr = localStart.ToString(BotConstants.DateFormats.FullLocalTimeFormat);
        var timeStr = localStart.ToString("HH:mm");
        FormatAttendingPlayersMarkdown(option.AttendingPlayerNames);

        var announcementText = string.Format(
            BotConstants.PlayerMessages.SelectedRecommendationAnnouncement,
            optionIndex + 1,
            escapedGroupName,
            dateStr,
            timeStr,
            option.GetPriorityDescription());

        await _notificationService.NotifyAllInGroupAsync(group, announcementText, GroupNotificationService.CreateRsvpKeyboard(group.Id), ct);
    }

    /// <summary>
    /// Форматирует список присутствующих игроков в Markdown.
    /// </summary>
    private static string FormatAttendingPlayersMarkdown(List<string> playerNames)
    {
        return playerNames.Count == 0 ? BotConstants.CallbackHandlerMessages.NoData : string.Join(", ", playerNames.Select(name => $"@{BotConstants.TextHelpers.EscapeMarkdown(name)}"));
    }

    /// <summary>
    /// Автоматически запускает поиск окон, выбирает ближайшее и отправляет запросы RSVP игрокам.
    /// </summary>
    private async Task AutoRunPlanningForGroupAsync(Group group, CancellationToken ct)
    {
        var result = await _planningService.AutoPlanGroupAsync(group.Id, ct);
        if (result is { Success: false })
        {
            _logger.LogWarning(BotConstants.CallbackHandlerLogs.AutoPlanFailed, group.Id, result.Message);
            return;
        }

        if (result is { HasIntersection: true })
        {
            await ScheduleSessionAndNotifyAsync(group, result.SelectedSlot!, ct);
        }
        else if (result is { HasRecommendations: true, BestOption: not null })
        {
            await SendBestRecommendationAsync(group, result.BestOption, ct);
        }
    }

    /// <summary>
    /// Планирует сессию на основе найденного слота и уведомляет игроков.
    /// </summary>
    private async Task ScheduleSessionAndNotifyAsync(Group group, DateTimeRange nearestSlot, CancellationToken ct)
    {
        var admin = await UserService.GetPlayerAsync(AdminIds.FirstOrDefault(), ct);
        var localStart = ConvertUtcToLocal(nearestSlot.Start, admin?.TimeZoneOffset ?? 0);
        ConvertUtcToLocal(nearestSlot.End, admin?.TimeZoneOffset ?? 0);

        // Исправление: сохраняем UTC время как есть
        SessionPlanningService.UpdateGroupSessionData(group, nearestSlot.Start);
        await Db.SaveChangesAsync(ct);

        _logger.LogInformation(BotConstants.CallbackHandlerLogs.AutoSlotSelected, group.Name, nearestSlot.Start);

        var announcementText = FormatAutoSessionAnnouncement(group, localStart, nearestSlot);
        var rsvpKeyboard = GroupNotificationService.CreateRsvpKeyboard(group.Id);

        await SendRsvpToAllPlayersAsync(group, announcementText, rsvpKeyboard, ct);
        await NotifyAdminAndMainChatAboutAutoPlanningAsync(group, localStart, ct);
    }

    /// <summary>
    /// Форматирует текст анонса авто-сессии.
    /// </summary>
    private static string FormatAutoSessionAnnouncement(Group group, DateTime localStart, DateTimeRange slot)
    {
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup);
        var dateStr = localStart.ToString(BotConstants.DateFormats.FullLocalTimeFormat);
        var timeStr = localStart.ToString("HH:mm");
        var duration = (slot.End - slot.Start).TotalHours;

        return string.Format(
            BotConstants.PlayerMessages.AutoSessionAnnouncement,
            escapedGroupName,
            dateStr,
            timeStr,
            duration);
    }

    /// <summary>
    /// Отправляет RSVP всем игрокам группы.
    /// </summary>
    private async Task SendRsvpToAllPlayersAsync(Group group, string announcementText, InlineKeyboardMarkup rsvpKeyboard, CancellationToken ct)
    {
        foreach (var player in group.Players)
        {
            try
            {
                await _notificationService.SendToUserAsync(player.TelegramId, announcementText, rsvpKeyboard, ct);
                _logger.LogDebug(BotConstants.CallbackHandlerLogs.RsvpSentDebug, player.TelegramId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, BotConstants.CallbackHandlerLogs.RsvpSendFailed, player.TelegramId);
            }
        }
    }

    /// <summary>
    /// Уведомляет админа и основной чат о завершении авто-планирования.
    /// </summary>
    private async Task NotifyAdminAndMainChatAboutAutoPlanningAsync(Group group, DateTime localStart, CancellationToken ct)
    {
        var admin = await UserService.GetPlayerAsync(AdminIds.FirstOrDefault(), ct);
        var adminText = string.Format(
            BotConstants.AdminMessages.AutoPlanningCompleted,
            localStart.ToString(BotConstants.DateFormats.LocalTimeFormat),
            group.Players.Count);

        await _notificationService.SendToUserAsync(admin?.TelegramId ?? 0, adminText, ct: ct);
        await _notificationService.NotifyMainChatAsync(
            string.Format(BotConstants.SystemNotifications.TimeAssigned, localStart.ToString(BotConstants.DateFormats.LocalTimeFormat)),
            ct: ct);

        _logger.LogInformation(BotConstants.CallbackHandlerLogs.AutoPlanResultsSent, group.Name);
    }

    /// <summary>
    /// Отправляет лучшую рекомендацию игрокам.
    /// </summary>
    private async Task SendBestRecommendationAsync(Group group, RecommendationOption bestOption, CancellationToken ct)
    {
        // Исправление: используем админа из группы для консистентности времени
        var adminInGroup = GetAdminsInGroup(group).FirstOrDefault();
        if (adminInGroup != null)
        {
            // Исправление: конвертируем UTC в локальное время админа группы
            var localStart = ConvertUtcToLocal(bestOption.ProposedStartTime, adminInGroup.TimeZoneOffset);
            ConvertUtcToLocal(bestOption.ProposedEndTime, adminInGroup.TimeZoneOffset);

            // Исправление: сохраняем UTC время как есть (без конвертации)
            SessionPlanningService.UpdateGroupSessionData(group, bestOption.ProposedStartTime);
            await Db.SaveChangesAsync(ct);

            var announcementText = FormatRecommendedSessionAnnouncement(group, bestOption, localStart);
            var rsvpKeyboard = GroupNotificationService.CreateRsvpKeyboard(group.Id);

            await SendRsvpToAllPlayersAsync(group, announcementText, rsvpKeyboard, ct);
            await NotifyAdminAboutRecommendationAsync(group, bestOption, localStart, ct);
        }
    }

    /// <summary>
    /// Форматирует текст анонса рекомендованной сессии.
    /// </summary>
    private static string FormatRecommendedSessionAnnouncement(Group group, RecommendationOption option, DateTime localStart)
    {
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup);
        var dateStr = localStart.ToString(BotConstants.DateFormats.FullLocalTimeFormat);
        var timeStr = localStart.ToString("HH:mm");
        var duration = (option.ProposedEndTime - option.ProposedStartTime).TotalHours;
        var attendingPlayersText = FormatAttendingPlayersMarkdown(option.AttendingPlayerNames);

        return string.Format(
            BotConstants.PlayerMessages.RecommendedSessionAnnouncement,
            escapedGroupName,
            dateStr,
            timeStr,
            duration,
            option.AttendingPlayersCount,
            option.TotalPlayersCount,
            attendingPlayersText);
    }

    /// <summary>
    /// Уведомляет админа о выбранной рекомендации.
    /// </summary>
    private async Task NotifyAdminAboutRecommendationAsync(Group group, RecommendationOption option, DateTime localStart, CancellationToken ct)
    {
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup);
        var attendingPlayersText = FormatAttendingPlayersMarkdown(option.AttendingPlayerNames);

        var adminText = string.Format(
            BotConstants.CallbackHandlerMessages.RecommendationsAdminText,
            escapedGroupName,
            localStart.ToString(BotConstants.DateFormats.LocalTimeFormat),
            option.AttendingPlayersCount,
            option.TotalPlayersCount,
            attendingPlayersText);

        await _notificationService.SendToMainAdminAsync(adminText, ct: ct);
        _logger.LogInformation(BotConstants.CallbackHandlerLogs.RecommendationsSent, group.Name);
    }

    /// <summary>
    /// Обрабатывает отмену текущего действия.
    /// </summary>
    private async Task HandleCancelActionAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        await SetPlayerStateAsync(userId, PlayerState.Idle, ct);
        await _notificationService.EditTextAsync(callbackQuery, BotConstants.PlayerMessages.ActionCancelled, null, ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Обрабатывает установку часового пояса пользователя.
    /// </summary>
    private async Task HandleSetTimeZoneAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!TryParseTimeZoneFromCallback(callbackQuery.Data, out var offset))
        {
            _logger.LogWarning(BotConstants.CallbackHandlerLogs.TimeZoneInvalidFormat, callbackQuery.Data);
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.TimeZoneFormatError, true, ct);
            return;
        }

        var success = await UpdatePlayerTimeZoneAsync(userId, offset, ct);
        if (!success)
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.TimeZoneError, true, ct);
            return;
        }

        _logger.LogInformation(BotConstants.CallbackHandlerLogs.TimeZoneSetLog, userId, offset);
        var formattedOffset = BotConstants.TextHelpers.FormatTimeZoneOffset(offset);
        var responseText = string.Format(BotConstants.PlayerMessages.TimeZoneSet, formattedOffset);

        await _notificationService.EditTextAsync(callbackQuery, responseText, ct: ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Парсит смещение часового пояса из колбэка.
    /// </summary>
    private bool TryParseTimeZoneFromCallback(string? data, out int offset)
    {
        offset = 0;
        if (string.IsNullOrEmpty(data))
            return false;

        var offsetString = data.Replace(BotConstants.CallbackPrefixes.SetTimeZone, string.Empty);
        return int.TryParse(offsetString, out offset);
    }

    /// <summary>
    /// Обрабатывает выбор даты для отметки свободного времени.
    /// </summary>
    private async Task HandlePickDateAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!TryParseDateFromCallback(callbackQuery.Data, out var selectedDate))
        {
            _logger.LogWarning(BotConstants.CallbackHandlerLogs.DateParseFailed, callbackQuery.Data);
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.DateParseError, true, ct);
            return;
        }

        var player = await GetPlayerWithSlotsAsync(userId, ct);
        if (player is null)
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.PlayerNotFound, true, ct);
            return;
        }

        var displayDate = selectedDate.ToString(BotConstants.DateFormats.DisplayFormat);
        var timeGrid = AvailabilityMenu.GetTimeGrid(selectedDate, player.Slots, player.TimeZoneOffset);
        var title = string.Format(BotConstants.PlayerMessages.TimeSelectionTitle, displayDate);

        await _notificationService.EditTextAsync(callbackQuery, title, timeGrid, ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Парсит дату из колбэка выбора даты.
    /// </summary>
    private bool TryParseDateFromCallback(string? data, out DateTime date)
    {
        date = default;
        if (string.IsNullOrEmpty(data))
            return false;

        var dateStr = data.Replace(BotConstants.CallbackPrefixes.PickDate, string.Empty);
        return DateTime.TryParse(dateStr, out date);
    }

    /// <summary>
    /// Обрабатывает переключение доступности для конкретного часа.
    /// </summary>
    private async Task HandleToggleTimeAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!TryParseToggleTimeCallback(callbackQuery.Data, out var date, out var hour))
        {
            _logger.LogWarning(BotConstants.CallbackHandlerLogs.ToggleTimeInvalidFormat, callbackQuery.Data);
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.CallbackFormatError, true, ct);
            return;
        }

        var player = await GetPlayerWithSlotsAsync(userId, ct);
        if (player is null)
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.PlayerNotFound, true, ct);
            return;
        }

        var slotTimeUtc = ConvertLocalToUtc(new DateTime(date.Year, date.Month, date.Day, hour % 24, 0, 0), player.TimeZoneOffset);
        await ToggleAvailabilitySlotAsync(player, slotTimeUtc, ct);
        await UpdateTimeGridAsync(callbackQuery, date, player, ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Парсит данные колбэка переключения времени.
    /// </summary>
    private bool TryParseToggleTimeCallback(string? data, out DateTime date, out int hour)
    {
        date = default;
        hour = 0;
        if (string.IsNullOrEmpty(data))
            return false;

        var parts = data.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            return false;

        return DateTime.TryParse(parts[2], out date) && int.TryParse(parts[3], out hour);
    }

    /// <summary>
    /// Переключает слот доступности игрока.
    /// </summary>
    private async Task ToggleAvailabilitySlotAsync(Player player, DateTime slotTimeUtc, CancellationToken ct)
    {
        var existingSlot = player.Slots.FirstOrDefault(s => s.DateTimeUtc == slotTimeUtc);
        if (existingSlot is not null)
        {
            Db.Slots.Remove(existingSlot);
            _logger.LogDebug(BotConstants.CallbackHandlerLogs.SlotRemoved, player.TelegramId, slotTimeUtc);
        }
        else
        {
            Db.Slots.Add(new AvailabilitySlot(player.TelegramId, slotTimeUtc));
            _logger.LogDebug(BotConstants.CallbackHandlerLogs.SlotAdded, player.TelegramId, slotTimeUtc);
        }

        await Db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Обновляет сетку времени в сообщении.
    /// </summary>
    private async Task UpdateTimeGridAsync(CallbackQuery callbackQuery, DateTime date, Player player, CancellationToken ct)
    {
        var timeGrid = AvailabilityMenu.GetTimeGrid(date, player.Slots, player.TimeZoneOffset);
        await _notificationService.EditReplyMarkupAsync(callbackQuery, timeGrid, ct);
    }

    /// <summary>
    /// Обрабатывает возврат к выбору дат из выбора времени.
    /// </summary>
    private async Task HandleBackToDatesAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var player = await UserService.GetPlayerAsync(userId, ct);
        var tzOffset = player?.TimeZoneOffset ?? 0;
        var dateCalendar = AvailabilityMenu.GetDateCalendar(tzOffset);

        await _notificationService.EditTextAsync(callbackQuery, BotConstants.PlayerMessages.CalendarTitle, dateCalendar, ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Обрабатывает завершение голосования по расписанию.
    /// </summary>
    private async Task HandleFinishVotingAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var player = await GetPlayerWithGroupsAsync(userId, ct);
        if (player is null)
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
            return;
        }

        var groupsToCheck = await LoadFreshGroupsAsync(player.Groups.Select(g => g.Id).ToList(), ct);
        var groupsUpdated = UpdateFinishedVotingForGroups(groupsToCheck, userId);
        if (groupsUpdated.Count > 0)
        {
            await Db.SaveChangesAsync(ct);
            _logger.LogInformation(BotConstants.CallbackHandlerLogs.VotingSaved, groupsUpdated.Count);
        }

        await _notificationService.EditTextAsync(callbackQuery, BotConstants.PlayerMessages.DataSaved, ct: ct);
        await _notificationService.NotifyMainChatAsync(string.Format(BotConstants.SystemNotifications.PlayerFinishedVoting, player.GetMarkdownUsername()), ct: ct);
        await CheckGroupsReadinessAsync(groupsToCheck, ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Получает игрока с загруженными группами.
    /// </summary>
    private async Task<Player?> GetPlayerWithGroupsAsync(long telegramId, CancellationToken ct) =>
        await Db.Players.Include(p => p.Groups).FirstOrDefaultAsync(p => p.TelegramId == telegramId, ct);

    /// <summary>
    /// Загружает актуальные данные групп из БД.
    /// </summary>
    private async Task<List<Group>> LoadFreshGroupsAsync(List<int> groupIds, CancellationToken ct)
    {
        var groups = new List<Group>();
        foreach (var groupId in groupIds)
        {
            var group = await Db.Groups
                .Include(g => g.Players)
                .AsSplitQuery()
                .FirstOrDefaultAsync(g => g.Id == groupId, ct);
            if (group != null)
                groups.Add(group);
        }
        return groups;
    }

    /// <summary>
    /// Обновляет статус завершения голосования для групп.
    /// </summary>
    private List<Group> UpdateFinishedVotingForGroups(List<Group> groups, long userId)
    {
        var updated = new List<Group>();
        foreach (var group in groups.Where(group => group.CurrentSessionUtc == null && !group.FinishedVotingPlayerIds.Contains(userId)))
        {
            group.FinishedVotingPlayerIds.Add(userId);
            updated.Add(group);
            _logger.LogDebug("Игрок {UserId} добавил себя в finished voting для группы {GroupName}", userId, group.Name);
        }
        return updated;
    }

    /// <summary>
    /// Проверяет готовность групп к планированию.
    /// </summary>
    private async Task CheckGroupsReadinessAsync(List<Group> groups, CancellationToken ct)
    {
        foreach (var group in groups)
        {
            if (await _rsvpService.AreAllPlayersFinishedVotingAsync(group, ct))
            {
                _logger.LogInformation(BotConstants.CallbackHandlerLogs.AllVotingFinished, group.Players.Count, group.Name);
                await ProcessReadyGroupAsync(group, ct);
            }
            else
            {
                LogWaitingForVotingAsync(group);
            }
        }
    }

    /// <summary>
    /// Обрабатывает группу, готовую к планированию.
    /// </summary>
    private async Task ProcessReadyGroupAsync(Group group, CancellationToken ct)
    {
        if (group.CurrentSessionUtc != null)
        {
            _logger.LogInformation(BotConstants.CallbackHandlerLogs.GroupHasSession, group.Name, group.CurrentSessionUtc);
            await CheckSessionAvailabilityAsync(group, ct);
        }
        else
        {
            _logger.LogInformation(BotConstants.CallbackHandlerLogs.GroupNoSession, group.Name);
            await AutoRunPlanningForGroupAsync(group, ct);
        }
    }

    /// <summary>
    /// Логирует ожидание завершения голосования.
    /// </summary>
    private void LogWaitingForVotingAsync(Group group)
    {
        var finishedCount = group.FinishedVotingPlayerIds.Count;
        var totalCount = group.Players.Count;
        _logger.LogInformation(BotConstants.CallbackHandlerLogs.WaitingForVoting, finishedCount, totalCount, group.Name);
    }

    /// <summary>
    /// Проверяет, могут ли игроки присутствовать на уже назначенной сессии.
    /// </summary>
    private async Task CheckSessionAvailabilityAsync(Group group, CancellationToken ct)
    {
        if (group.CurrentSessionUtc == null)
        {
            Logger.LogDebug(BotConstants.CallbackHandlerLogs.GroupNoAssignedSession, group.Name);
            return;
        }

        var result = await _planningService.CheckSessionAvailabilityAsync(group, ct);
        if (!result.HasSession)
            return;

        if (result.ShouldConfirm)
        {
            await ConfirmExistingSessionAsync(group, result, ct);
        }
        else if (result.ShouldReschedule)
        {
            await RescheduleExistingSessionAsync(group, result, ct);
        }
    }

    /// <summary>
    /// Подтверждает существующую сессию.
    /// </summary>
    private async Task ConfirmExistingSessionAsync(Group group, SessionAvailabilityResult result, CancellationToken ct)
    {
        group.SessionStatus = SessionStatus.Confirmed;
        await Db.SaveChangesAsync(ct);

        var attendanceText = result.CannotAttendPlayers.Any()
            ? string.Format(BotConstants.CallbackHandlerMessages.CannotAttendPlayers, string.Join(", ", result.CannotAttendPlayers.Select(p => p.GetMarkdownUsername())))
            : BotConstants.CallbackHandlerMessages.AllPlayersCanAttend;

        var admin = await UserService.GetPlayerAsync(AdminIds.FirstOrDefault(), ct);
        var localStart = ConvertUtcToLocal(group.CurrentSessionUtc!.Value, admin?.TimeZoneOffset ?? 0);
        var localTimeStr = localStart.ToString(BotConstants.DateFormats.FullLocalTimeFormat);
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup);

        var notificationText = string.Format(
            BotConstants.PlayerMessages.SessionStillValid,
            escapedGroupName,
            localTimeStr,
            result.CanAttendPlayers.Count,
            group.Players.Count,
            result.AttendanceRate,
            BotConstants.ConfirmationThreshold,
            attendanceText);

        await _notificationService.NotifyMainChatAsync(notificationText, ct: ct);
        await NotifyUnavailablePlayersAsync(result.CannotAttendPlayers, group, localStart, result.AttendanceRate, ct);
        Logger.LogInformation(BotConstants.CallbackHandlerLogs.SessionConfirmedRate, group.Name, result.AttendanceRate, result.CannotAttendPlayers.Count);
    }

    /// <summary>
    /// Уведомляет игроков, которые не могут присутствовать.
    /// </summary>
    private async Task NotifyUnavailablePlayersAsync(List<Player> cannotAttendPlayers, Group group, DateTime localStart, double attendanceRate, CancellationToken ct)
    {
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup);
        var localTimeStr = localStart.ToString(BotConstants.DateFormats.FullLocalTimeFormat);

        foreach (var player in cannotAttendPlayers)
        {
            await _notificationService.SendToUserAsync(
                player.TelegramId,
                string.Format(BotConstants.PlayerMessages.PlayerCannotAttendWarning, escapedGroupName, localTimeStr, attendanceRate),
                ct: ct);
        }
    }

    /// <summary>
    /// Запускает перепланирование существующей сессии.
    /// </summary>
    private async Task RescheduleExistingSessionAsync(Group group, SessionAvailabilityResult result, CancellationToken ct)
    {
        var reason = !result.AdminsCanAttend
            ? $"❌ **Администраторы не могут:** {string.Join(", ", GetAdminsInGroup(group).Where(a => !result.CanAttendPlayers.Contains(a)).Select(p => p.GetMarkdownUsername()))}"
            : $"❌ **Мало игроков:** {result.CanAttendPlayers.Count}/{group.Players.Count} ({result.AttendanceRate:P0})";

        SessionPlanningService.ResetGroupVotingData(group);
        group.SessionStatus = SessionStatus.Rescheduled;
        await Db.SaveChangesAsync(ct);

        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup);
        var notificationText = string.Format(
            BotConstants.PlayerMessages.NewPlanningRequired,
            escapedGroupName,
            reason,
            BotConstants.ConfirmationThreshold);

        await _notificationService.NotifyMainChatAsync(notificationText, ct: ct);
        Logger.LogInformation(
            BotConstants.CallbackHandlerLogs.SessionRescheduledReason,
            group.Name,
            !result.AdminsCanAttend ? BotConstants.CallbackHandlerMessages.RescheduleReasonAdmins : BotConstants.CallbackHandlerMessages.RescheduleReasonPlayers);

        await AutoRunPlanningForGroupAsync(group, ct);
    }

    /// <summary>
    /// Обрабатывает вступление пользователя в группу.
    /// </summary>
    private async Task HandleJoinGroupAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var user = await UserService.GetPlayerAsync(userId, ct);
        if (user is null)
        {
            Logger.LogError(BotConstants.CallbackHandlerLogs.PlayerNotFoundJoin, userId);
            return;
        }

        if (!TryParseGroupIdFromCallback(callbackQuery.Data, BotConstants.CallbackPrefixes.JoinGroup, out var groupId))
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.CallbackDataMissing, true, ct);
            return;
        }

        var success = await AddPlayerToGroupAsync(userId, groupId, ct);
        if (!success)
        {
            await HandleJoinFailureAsync(callbackQuery, groupId, ct);
            return;
        }

        await HandleJoinSuccessAsync(callbackQuery, user, groupId, ct);
    }

    /// <summary>
    /// Обрабатывает неудачную попытку вступления в группу.
    /// </summary>
    private async Task HandleJoinFailureAsync(CallbackQuery callbackQuery, int groupId, CancellationToken ct)
    {
        var group = await GetGroupAsync(groupId, ct);
        var message = group is not null
            ? string.Format(BotConstants.PlayerMessages.AlreadyInGroup, BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup))
            : BotConstants.ErrorMessages.GroupNotFound;

        await _notificationService.AnswerCallbackAsync(callbackQuery, message, true, ct);
    }

    /// <summary>
    /// Обрабатывает успешное вступление в группу.
    /// </summary>
    private async Task HandleJoinSuccessAsync(CallbackQuery callbackQuery, Player user, int groupId, CancellationToken ct)
    {
        var addedGroup = await GetGroupAsync(groupId, ct);
        if (addedGroup is not null)
        {
            _logger.LogInformation(BotConstants.CallbackHandlerLogs.PlayerJoinedGroupLog, user.TelegramId, groupId, addedGroup.Name);

            if (addedGroup.SessionStatus != SessionStatus.Pending || addedGroup.FinishedVotingPlayerIds.Count > 0)
            {
                await ResetGroupVotingOnNewPlayerAsync(addedGroup, user, ct);
            }

            var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(addedGroup.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup);
            await _notificationService.EditTextAsync(callbackQuery, string.Format(BotConstants.PlayerMessages.JoinedGroup, escapedGroupName), ct: ct);
            await _notificationService.NotifyMainChatAsync(string.Format(BotConstants.SystemNotifications.PlayerJoinedGroup, user.GetMarkdownUsername(), escapedGroupName), ct: ct);
        }

        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Сбрасывает голосование в группе при присоединении нового игрока.
    /// </summary>
    private async Task ResetGroupVotingOnNewPlayerAsync(Group group, Player user, CancellationToken ct)
    {
        SessionPlanningService.ResetGroupVotingData(group);
        await Db.SaveChangesAsync(ct);
        _logger.LogInformation(BotConstants.CallbackHandlerLogs.GroupVotingReset, group.Name, user.TelegramId);

        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup);
        await _notificationService.NotifyMainChatAsync(
            string.Format(BotConstants.SystemNotifications.GroupChanged, user.GetMarkdownUsername(), escapedGroupName),
            ct: ct);
    }

    /// <summary>
    /// Обрабатывает подтверждение удаления группы (админ).
    /// </summary>
    private async Task HandleConfirmDeleteGroupAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!IsAdmin(userId))
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.AdminMessages.AdminOnlyDeleteGroup, true, ct);
            return;
        }

        if (!TryParseGroupIdFromCallback(callbackQuery.Data, BotConstants.CallbackPrefixes.ConfirmDeleteGroup, out var groupId))
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.GroupNotFound, true, ct);
            return;
        }

        var group = await GetGroupAsync(groupId, ct);
        if (group is null)
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.GroupNotFound, true, ct);
            return;
        }

        Db.Groups.Remove(group);
        await Db.SaveChangesAsync(ct);
        _logger.LogInformation(BotConstants.CallbackHandlerLogs.AdminDeletedGroup, userId, groupId, group.Name);

        var escapedName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup);
        await _notificationService.EditTextAsync(callbackQuery, string.Format(BotConstants.AdminMessages.GroupDeleted, escapedName), ct: ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Обрабатывает выход пользователя из группы.
    /// </summary>
    private async Task HandleLeaveGroupAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!TryParseGroupIdFromCallback(callbackQuery.Data, BotConstants.CallbackPrefixes.LeaveGroup, out var groupId))
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
            return;
        }

        var success = await RemovePlayerFromGroupAsync(userId, groupId, ct);
        if (!success)
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.PlayerMessages.NotInGroup, ct: ct);
            return;
        }

        var group = await GetGroupAsync(groupId, ct);
        if (group is not null)
        {
            _logger.LogInformation(BotConstants.CallbackHandlerLogs.PlayerLeftGroup, userId, groupId, group.Name);
            var escapedName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup);
            await _notificationService.EditTextAsync(callbackQuery, string.Format(BotConstants.PlayerMessages.LeftGroup, escapedName), ct: ct);
        }

        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Отправляет запрос на ввод свободного времени для новой игровой сессии.
    /// </summary>
    private async Task HandleStartRequestAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!IsAdmin(userId))
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.AdminMessages.AdminOnlyRequestFreeTime, true, ct);
            return;
        }

        if (!TryParseGroupIdFromCallback(callbackQuery.Data, BotConstants.CallbackPrefixes.StartRequest, out var groupId))
        {
            _logger.LogWarning(BotConstants.CallbackHandlerLogs.FreeTimeRequestInvalidFormat, callbackQuery.Data);
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.CallbackDataMissing, true, ct);
            return;
        }

        var group = await GetGroupWithPlayersAsync(groupId, ct);
        if (group is null)
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.GroupNotFound, true, ct);
            return;
        }

        await ResetGroupVotingDataForRequestAsync(group, ct);
        await NotifyGroupAboutFreeTimeRequestAsync(group, ct);
        await RespondToAdminAboutRequestAsync(callbackQuery, group, ct);
        await NotifyMainChatAboutRequestAsync(group, ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, string.Format(BotConstants.PlayerMessages.RequestSentCallbackResponse, group.Name), ct: ct);
    }

    /// <summary>
    /// Сбрасывает данные голосования для нового запроса.
    /// </summary>
    private async Task ResetGroupVotingDataForRequestAsync(Group group, CancellationToken ct)
    {
        var hadSession = group.CurrentSessionUtc.HasValue;
        group.CurrentSessionUtc = null;
        group.FinishedVotingPlayerIds.Clear();
        group.ConfirmedPlayerIds.Clear();
        group.DeclinedPlayerIds.Clear();
        group.SessionStatus = SessionStatus.NoSession;
        await Db.SaveChangesAsync(ct);

        _logger.LogInformation(
            BotConstants.CallbackHandlerLogs.VotingDataReset,
            group.Name,
            group.CurrentSessionUtc,
            group.FinishedVotingPlayerIds.Count,
            hadSession);
    }

    /// <summary>
    /// Уведомляет группу о запросе свободного времени.
    /// </summary>
    private async Task NotifyGroupAboutFreeTimeRequestAsync(Group group, CancellationToken ct)
    {
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup);
        var requestMessage = string.Format(BotConstants.PlayerMessages.FreeTimeRequest, escapedGroupName);
        await _notificationService.NotifyAllInGroupAsync(group, requestMessage, ct: ct);
        _logger.LogInformation(BotConstants.CallbackHandlerLogs.FreeTimeRequestSent, group.Name);
    }

    /// <summary>
    /// Отвечает админу о результате отправки запроса.
    /// </summary>
    private async Task RespondToAdminAboutRequestAsync(CallbackQuery callbackQuery, Group group, CancellationToken ct)
    {
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup);
        var responseText = string.Format(BotConstants.PlayerMessages.RequestSentToGroup, escapedGroupName);
        await _notificationService.EditTextAsync(callbackQuery, responseText, ct: ct);
    }

    /// <summary>
    /// Уведомляет основной чат о запросе свободного времени.
    /// </summary>
    private async Task NotifyMainChatAboutRequestAsync(Group group, CancellationToken ct)
    {
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup);
        var mainChatText = string.Format(BotConstants.SystemNotifications.FreeTimeRequested, escapedGroupName, "Игроки, проверьте чат группы и заполните расписание!");
        await _notificationService.NotifyMainChatAsync(mainChatText, ct: ct);
    }

    /// <summary>
    /// Обрабатывает запуск поиска свободных окон для группы.
    /// </summary>
    private async Task HandleStartPlanningAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!IsAdmin(userId))
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.AdminMessages.AdminOnlyPlanning, true, ct);
            return;
        }

        if (!TryParseGroupIdFromCallback(callbackQuery.Data, BotConstants.CallbackPrefixes.StartPlan, out var groupId))
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.CallbackDataMissing, true, ct);
            return;
        }

        _logger.LogInformation(BotConstants.CallbackHandlerLogs.PlanningStart, groupId, BotConstants.MinPlanningDurationHours);
        var intersections = await SchedulingService.FindIntersectionsAsync(groupId, BotConstants.MinPlanningDurationHours);
        if (intersections.Count == 0)
        {
            await HandleNoIntersectionsInPlanningAsync(callbackQuery, groupId, ct);
            return;
        }

        await ShowPlanningResultsAsync(callbackQuery, groupId, intersections, ct);
    }

    /// <summary>
    /// Обрабатывает отсутствие пересечений при планировании.
    /// </summary>
    private async Task HandleNoIntersectionsInPlanningAsync(CallbackQuery callbackQuery, int groupId, CancellationToken ct)
    {
        var group = await GetGroupWithPlayersAsync(groupId, ct);
        if (group != null)
        {
            var recommendationResult = await _planningService.GetRecommendationsForGroupAsync(group, ct);
            if (recommendationResult.HasRecommendations)
            {
                await ShowRecommendationsAsync(callbackQuery, group, recommendationResult, ct);
                return;
            }
        }

        await _notificationService.EditTextAsync(callbackQuery, BotConstants.AdminMessages.NoIntersectionsFound, ct: ct);
    }

    /// <summary>
    /// Показывает результаты планирования администратору.
    /// </summary>
    private async Task ShowPlanningResultsAsync(CallbackQuery callbackQuery, int groupId, List<DateTimeRange> intersections, CancellationToken ct)
    {
        var resultText = BotConstants.CallbackHandlerMessages.PlanningResultsTitle;
        var buttons = new List<InlineKeyboardButton[]>();

        foreach (var interval in intersections.Take(BotConstants.MaxPlanningResultsToShow))
        {
            var admin = await UserService.GetPlayerAsync(AdminIds.FirstOrDefault(), ct);
            var localStart = ConvertUtcToLocal(interval.Start, admin?.TimeZoneOffset ?? 0);
            var localEnd = ConvertUtcToLocal(interval.End, admin?.TimeZoneOffset ?? 0);
            var timeStr = $"{localStart:dd.MM HH:mm} - {localEnd:HH:mm}";

            resultText += string.Format(BotConstants.CallbackHandlerMessages.PlanningResultLine, timeStr);
            buttons.Add([
                InlineKeyboardButton.WithCallbackData(
                    $"✅ {timeStr}",
                    $"{BotConstants.CallbackPrefixes.ConfirmTime}{groupId}_{interval.Start:yyyyMMddHH}")
            ]);
        }

        await _notificationService.EditTextAsync(callbackQuery, resultText, new InlineKeyboardMarkup(buttons), ct);
    }

    /// <summary>
    /// Показывает рекомендации администратору.
    /// </summary>
    private async Task ShowRecommendationsAsync(CallbackQuery callbackQuery, Group group, RecommendationResult result, CancellationToken ct)
    {
        var resultText = string.Format(BotConstants.AdminMessages.RecommendationsTitle, BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup), result.OptionsCount) + "\n";
        var buttons = new List<InlineKeyboardButton[]>();
        var admin = await UserService.GetPlayerAsync(AdminIds.FirstOrDefault(), ct);

        foreach (var option in result.Options.Take(BotConstants.MaxPlanningResultsToShow))
        {
            var localStart = ConvertUtcToLocal(option.ProposedStartTime, admin?.TimeZoneOffset ?? 0);
            var localEnd = ConvertUtcToLocal(option.ProposedEndTime, admin?.TimeZoneOffset ?? 0);
            var timeStr = $"{localStart:dd.MM HH:mm} - {localEnd:HH:mm}";
            var index = result.Options.IndexOf(option);

            resultText += string.Format(
                BotConstants.CallbackHandlerMessages.RecommendationOptionLine,
                index + 1,
                timeStr,
                option.AttendingPlayersCount,
                option.TotalPlayersCount,
                option.GetAttendingPlayersMarkdown());

            buttons.Add([
                InlineKeyboardButton.WithCallbackData(
                    $"✅ Вариант #{index + 1}",
                    $"{BotConstants.CallbackPrefixes.SelectRecommendation}{group.Id}_{index}")
            ]);
        }

        await _notificationService.EditTextAsync(callbackQuery, resultText, new InlineKeyboardMarkup(buttons), ct);
    }
    
    // Добавьте новые методы обработки:
    /// <summary>
    /// Обрабатывает просмотр состава группы.
    /// </summary>
    private async Task HandleViewGroupMembersAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!IsAdmin(userId))
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.AdminMessages.AdminOnlyViewMembers, true, ct);
            return;
        }
        if (!TryParseGroupIdFromCallback(callbackQuery.Data, BotConstants.CallbackPrefixes.ViewGroupMembers, out var groupId))
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.CallbackDataMissing, true, ct);
            return;
        }
        var group = await GetGroupWithPlayersAsync(groupId, ct);
        if (group == null)
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.GroupNotFound, true, ct);
            return;
        }
        var playerList = string.Join("\n", group.Players.Select(p => $"• {p.GetMarkdownUsername()}"));
        var adminIdsInGroup = GetAdminsInGroup(group);
        var adminList = adminIdsInGroup.Count > 0
            ? string.Join("\n", adminIdsInGroup.Select(p => $"• {p.GetMarkdownUsername()}"))
            : BotConstants.AdminMessages.NoAdminsInGroup;
        var messageText = string.Format(
            BotConstants.AdminMessages.GroupMembersTitle,
            BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup),
            group.Players.Count,
            playerList,
            adminIdsInGroup.Count,
            adminList);
        await _notificationService.EditTextAsync(callbackQuery, messageText, ct: ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
        _logger.LogInformation(BotConstants.CallbackHandlerLogs.MembersViewed, userId, group.Name);
    }
    
    /// <summary>
    /// Обрабатывает просмотр информации о сессии.
    /// </summary>
    private async Task HandleViewSessionInfoAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!TryParseGroupIdFromCallback(callbackQuery.Data, BotConstants.CallbackPrefixes.ViewSessionInfo, out var groupId))
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.CallbackDataMissing, true, ct);
            return;
        }
        var group = await GetGroupWithPlayersAsync(groupId, ct);
        var player = await UserService.GetPlayerAsync(userId, ct);
        if (group == null || player == null)
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.GroupNotFound, true, ct);
            return;
        }
        if (!group.CurrentSessionUtc.HasValue)
        {
            var text = string.Format(
                BotConstants.AdminMessages.NoSessionScheduled,
                BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup));
            await _notificationService.EditTextAsync(callbackQuery, text, ct: ct);
            await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
            return;
        }
        var localTime = ConvertUtcToLocal(group.CurrentSessionUtc.Value, player.TimeZoneOffset);
        var duration = BotConstants.DefaultSessionDurationHours;
        var confirmedList = group.Players
            .Where(p => group.ConfirmedPlayerIds.Contains(p.TelegramId))
            .Select(p => $"• {p.GetMarkdownUsername()}")
            .ToList();
        var declinedList = group.Players
            .Where(p => group.DeclinedPlayerIds.Contains(p.TelegramId))
            .Select(p => $"• {p.GetMarkdownUsername()}")
            .ToList();
        var messageText = string.Format(
            BotConstants.AdminMessages.SessionInfoTitle,
            BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? BotConstants.CallbackHandlerMessages.UnknownGroup),
            localTime.ToString(BotConstants.DateFormats.FullLocalTimeFormat),
            duration,
            group.SessionStatus,
            group.ConfirmedPlayerIds.Count,
            group.Players.Count,
            confirmedList.Count > 0 ? string.Join("\n", confirmedList) : BotConstants.CallbackHandlerMessages.NoData,
            declinedList.Count,
            group.Players.Count,
            declinedList.Count > 0 ? string.Join("\n", declinedList) : BotConstants.CallbackHandlerMessages.NoData);
        await _notificationService.EditTextAsync(callbackQuery, messageText, ct: ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
        _logger.LogInformation(BotConstants.CallbackHandlerLogs.SessionInfoViewed, userId, group.Name);
    }

    /// <summary>
    /// Обрабатывает кнопку "Моё время" из меню /start.
    /// </summary>
    private async Task HandleStartMenuFreeAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var player = await GetPlayerWithSlotsAsync(userId, ct);
        if (player is null)
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.PlayerNotFound, true, ct);
            return;
        }

        try
        {
            await _notificationService.SendToUserAsync(
                userId,
                BotConstants.CommandMessages.FreeTimePrompt,
                AvailabilityMenu.GetDateCalendar(player.TimeZoneOffset),
                ct);

            await _notificationService.EditTextAsync(callbackQuery, "📩 Отправил календарь вам в личку!", ct: ct);
            await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);

            _logger.LogInformation(BotConstants.CallbackHandlerLogs.StartMenuFreeTime, userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось отправить календарь пользователю {UserId}", userId);
            await _notificationService.EditTextAsync(callbackQuery, BotConstants.CommandMessages.FreeTimePmFailed, ct: ct);
            await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
        }
    }

    /// <summary>
    /// Обрабатывает кнопку "Часовой пояс" из меню /start.
    /// </summary>
    private async Task HandleStartMenuTimeZoneAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
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

        await _notificationService.EditTextAsync(callbackQuery, BotConstants.CommandMessages.TimeZonePrompt, keyboard, ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);

        _logger.LogInformation(BotConstants.CallbackHandlerLogs.StartMenuTimeZone, userId);
    }

    /// <summary>
    /// Обрабатывает кнопку "Статус" из меню /start.
    /// </summary>
    private async Task HandleStartMenuStatusAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var player = await GetPlayerWithGroupsAsync(userId, ct);
        if (player is null || !player.Groups.Any())
        {
            await _notificationService.EditTextAsync(callbackQuery, BotConstants.CommandMessages.NoGroupsForStatus, ct: ct);
            await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
            return;
        }

        var groups = player.Groups.ToList();
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

        await _notificationService.EditTextAsync(callbackQuery, statusText.ToString(), ct: ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);

        _logger.LogInformation(BotConstants.CallbackHandlerLogs.StartMenuStatus, userId);
    }

    /// <summary>
    /// Обрабатывает кнопку "Группы" из меню /start (для админов).
    /// </summary>
    private async Task HandleStartMenuJoinAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!IsAdmin(userId))
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.AdminMessages.AdminOnlyAction, true, ct);
            return;
        }

        var groups = await Db.Groups.ToListAsync(ct);
        if (groups.Count == 0)
        {
            await _notificationService.EditTextAsync(callbackQuery, BotConstants.CommandMessages.NoGroupsToJoin, ct: ct);
            await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
            return;
        }

        var buttons = groups.Select(g =>
        {
            if (g.Name != null)
                return (List<InlineKeyboardButton>)[InlineKeyboardButton.WithCallbackData(g.Name, $"{BotConstants.CallbackPrefixes.JoinGroup}{g.Id}")];
            return null;
        });

        await _notificationService.EditTextAsync(callbackQuery, BotConstants.CommandMessages.JoinGroupPrompt, new InlineKeyboardMarkup(buttons!), ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);

        _logger.LogInformation(BotConstants.CallbackHandlerLogs.StartMenuJoin, userId);
    }

    /// <summary>
    /// Обрабатывает кнопку "Планирование" из меню /start (для админов).
    /// </summary>
    private async Task HandleStartMenuPlanAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!IsAdmin(userId))
        {
            await _notificationService.AnswerCallbackAsync(callbackQuery, BotConstants.AdminMessages.AdminOnlyPlanning, true, ct);
            return;
        }

        var groups = await Db.Groups.ToListAsync(ct);
        if (groups.Count == 0)
        {
            await _notificationService.EditTextAsync(callbackQuery, BotConstants.CommandMessages.NoGroupsForPlan, ct: ct);
            await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);
            return;
        }

        var buttons = groups.Select(g =>
        {
            if (g.Name != null)
                return (List<InlineKeyboardButton>)[InlineKeyboardButton.WithCallbackData(g.Name, $"{BotConstants.CallbackPrefixes.StartPlan}{g.Id}")];
            return null;
        });

        await _notificationService.EditTextAsync(callbackQuery, BotConstants.CommandMessages.PlanPrompt, new InlineKeyboardMarkup(buttons!), ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);

        _logger.LogInformation(BotConstants.CallbackHandlerLogs.StartMenuPlan, userId);
    }

    /// <summary>
    /// Обрабатывает кнопку "Помощь" из меню /start.
    /// </summary>
    private async Task HandleStartMenuHelpAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var isAdmin = IsAdmin(userId);
        var commands = new System.Text.StringBuilder();
        commands.AppendLine(BotConstants.Commands.CommandsList);

        if (isAdmin)
        {
            commands.AppendLine(BotConstants.Commands.AdminCommandsList);
            commands.AppendLine(BotConstants.Commands.ImportantNote);
        }

        commands.AppendLine(BotConstants.Commands.InDevelopment);

        var keyboard = CreateStartCommandKeyboard(isAdmin);

        await _notificationService.EditTextAsync(callbackQuery, BotConstants.PlayerMessages.WelcomePlayer + commands, keyboard, ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);

        _logger.LogInformation(BotConstants.CallbackHandlerLogs.StartMenuHelp, userId);
    }

    /// <summary>
    /// Создаёт клавиатуру для меню /start.
    /// </summary>
    private static InlineKeyboardMarkup CreateStartCommandKeyboard(bool isAdmin)
    {
        var buttons = new List<InlineKeyboardButton[]>
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(BotConstants.UiTexts.ButtonFreeTime, BotConstants.CallbackPrefixes.StartMenuFree),
                InlineKeyboardButton.WithCallbackData(BotConstants.UiTexts.ButtonTimeZone, BotConstants.CallbackPrefixes.StartMenuTimeZone),
                InlineKeyboardButton.WithCallbackData(BotConstants.UiTexts.ButtonStatus, BotConstants.CallbackPrefixes.StartMenuStatus)
            }
        };

        if (isAdmin)
        {
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData("👥 Группы", BotConstants.CallbackPrefixes.StartMenuJoin),
                InlineKeyboardButton.WithCallbackData("📅 Планирование", BotConstants.CallbackPrefixes.StartMenuPlan)
            });
        }

        buttons.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(BotConstants.UiTexts.ButtonHelp, BotConstants.CallbackPrefixes.StartMenuHelp)
        });

        return new InlineKeyboardMarkup(buttons);
    }

    /// <summary>
    /// Обрабатывает кнопку показа меню помощи.
    /// </summary>
    private async Task HandleShowHelpMenuAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var isAdmin = IsAdmin(userId);
        var keyboard = CreateHelpKeyboard(isAdmin);

        await _notificationService.EditTextAsync(callbackQuery, BotConstants.PlayerMessages.HelpText, keyboard, ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);

        _logger.LogInformation(BotConstants.CallbackHandlerLogs.HelpMenuShown, userId);
    }

    /// <summary>
    /// Показывает меню помощи после завершения действия.
    /// </summary>
    private async Task ShowHelpMenuAfterActionAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var isAdmin = IsAdmin(userId);
        var keyboard = CreateHelpKeyboard(isAdmin);

        await _notificationService.EditTextAsync(callbackQuery, BotConstants.PlayerMessages.HelpText, keyboard, ct);
        await _notificationService.AnswerCallbackAsync(callbackQuery, ct: ct);

        _logger.LogInformation(BotConstants.CallbackHandlerLogs.ActionCompletedShowHelp, userId);
    }

    /// <summary>
    /// Создаёт клавиатуру для меню помощи.
    /// </summary>
    private static InlineKeyboardMarkup CreateHelpKeyboard(bool isAdmin)
    {
        var buttons = new List<InlineKeyboardButton[]>
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(BotConstants.UiTexts.ButtonFreeTime, BotConstants.CallbackPrefixes.StartMenuFree),
                InlineKeyboardButton.WithCallbackData(BotConstants.UiTexts.ButtonTimeZone, BotConstants.CallbackPrefixes.StartMenuTimeZone),
                InlineKeyboardButton.WithCallbackData(BotConstants.UiTexts.ButtonStatus, BotConstants.CallbackPrefixes.StartMenuStatus)
            }
        };

        if (isAdmin)
        {
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData("👥 Группы", BotConstants.CallbackPrefixes.StartMenuJoin),
                InlineKeyboardButton.WithCallbackData("📅 Планирование", BotConstants.CallbackPrefixes.StartMenuPlan)
            });
        }

        return new InlineKeyboardMarkup(buttons);
    }

    /// <summary>
    /// Получает группу с загруженными игроками.
    /// </summary>
    private async Task<Group?> GetGroupWithPlayersAsync(int groupId, CancellationToken ct) =>
        await Db.Groups.Include(g => g.Players).FirstOrDefaultAsync(g => g.Id == groupId, ct);

    /// <summary>
    /// Получает игрока с загруженными слотами доступности.
    /// </summary>
    private async Task<Player?> GetPlayerWithSlotsAsync(long telegramId, CancellationToken ct) =>
        await Db.Players.Include(p => p.Slots).FirstOrDefaultAsync(p => p.TelegramId == telegramId, ct);

    /// <summary>
    /// Получает группу по идентификатору.
    /// </summary>
    private async Task<Group?> GetGroupAsync(int groupId, CancellationToken ct) =>
        await Db.Groups.FindAsync([groupId], ct);
}