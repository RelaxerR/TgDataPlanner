using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
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
/// Делегирует бизнес-логику сервисам планирования и рекомендаций.
/// </summary>
public class CallbackHandler : BaseHandler
{
#region Поля и зависимости

    private readonly SessionPlanningService _planningService;
    private readonly IRecommendationService _recommendationService;
    private readonly ILogger<CallbackHandler> _logger;

#endregion
#region Конструктор

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
    /// <param name="recommendationService">Сервис рекомендаций.</param>
    public CallbackHandler(
        IConfiguration config,
        ITelegramBotClient botClient,
        ILogger<CallbackHandler> logger,
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

#endregion
#region Публичный API

    /// <summary>
    /// Обрабатывает запрос обратного вызова от Telegram.
    /// </summary>
    /// <param name="callbackQuery">Запрос обратного вызова.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача выполнения обработки.</returns>
    public async Task HandleAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        if (callbackQuery.Data is null)
        {
            _logger.LogWarning("Получен CallbackQuery без данных от пользователя {UserId}", callbackQuery.From?.Id);
            await AnswerCallbackAsync(callbackQuery, ct);
            return;
        }
        var userId = callbackQuery.From.Id;
        _logger.LogDebug("Обработка callback '{Data}' от пользователя {UserId}", callbackQuery.Data, userId);
        try
        {
            await RouteCallbackAsync(callbackQuery, userId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Обработка callback прервана из-за отмены операции");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Необработанное исключение при обработке callback '{Data}' от пользователя {UserId}",
                callbackQuery.Data,
                userId);
            await AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.GenericError, true, ct);
        }
    }

#endregion
#region Маршрутизация колбэков

    /// <summary>
    /// Маршрутизирует callback-запрос к соответствующему обработчику.
    /// </summary>
    private async Task RouteCallbackAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var data = callbackQuery.Data;
        if (data is null)
        {
            _logger.LogError("Попытка маршрутизации callback без данных от пользователя {UserId}", userId);
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
            Logger.LogWarning("Игрок с TelegramId {UserId} не найден при обработке callback. Действие: {Data}", userId, data);
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
            default:
                _logger.LogWarning("Неизвестный callback-запрос: {Data}", data);
                await AnswerCallbackAsync(callbackQuery, ct);
                break;
        }
    }
    /// <summary>
    /// Проверяет, является ли колбэк подтверждением времени.
    /// </summary>
    private static bool IsConfirmTimeCallback(string data) =>
        data.StartsWith(BotConstants.CallbackPrefixes.ConfirmTime);

#endregion
#region Обработчики колбэков: Админ-действия

    /// <summary>
    /// Обрабатывает попытку выполнения админ-действия не-администратором.
    /// </summary>
    private async Task HandleAdminOnlyActionAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        _logger.LogWarning(
            "Пользователь {UserId} попытался выполнить админ-действие: {Data}",
            callbackQuery.From.Id,
            callbackQuery.Data);
        await AnswerCallbackAsync(callbackQuery, BotConstants.AdminMessages.AdminOnlyAction, true, ct);
    }
    /// <summary>
    /// Обрабатывает выбор админом конкретного времени для сессии и публикует анонс.
    /// </summary>
    private async Task HandleConfirmTimeAsync(CallbackQuery callbackQuery, Player player, CancellationToken ct)
    {
        var admin = await UserService.GetPlayerAsync(AdminIds.FirstOrDefault(), ct);
        if (player.TelegramId != admin?.TelegramId)
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.AdminMessages.AdminOnlyAction, true, ct);
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
            await AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.GroupNotFound, true, ct);
            return;
        }
        await UpdateGroupSessionAsync(group, sessionTimeUtc, ct);
        await NotifyGroupAboutSessionAsync(group, admin, ct);
        await EditTextAsync(callbackQuery, FormatSessionConfirmationText(group.Name, sessionTimeUtc, admin), ct);
        await AnswerCallbackAsync(callbackQuery, ct);
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
    /// Обновляет данные сессии в группе.
    /// </summary>
    private async Task UpdateGroupSessionAsync(Group group, DateTime sessionTimeUtc, CancellationToken ct)
    {
        group.CurrentSessionUtc = sessionTimeUtc;
        group.ConfirmedPlayerIds.Clear();
        group.DeclinedPlayerIds.Clear();
        group.SessionStatus = SessionStatus.Pending;
        await Db.SaveChangesAsync(ct);
        _logger.LogInformation("Мастер {UserId} назначил сессию для группы {GroupName} на {Time} UTC",
            group.Players.FirstOrDefault()?.TelegramId,
            group.Name,
            sessionTimeUtc);
    }
    /// <summary>
    /// Форматирует текст подтверждения сессии для редактирования сообщения.
    /// </summary>
    private string FormatSessionConfirmationText(string? groupName, DateTime sessionTimeUtc, Player? admin)
    {
        var localTime = ConvertUtcToLocal(sessionTimeUtc, admin?.TimeZoneOffset ?? 0);
        var escapedName = BotConstants.TextHelpers.EscapeMarkdown(groupName ?? "Неизвестно");
        return $"✅ Сессия для **{escapedName}** назначена на {localTime:dd.MM HH:mm}";
    }
    /// <summary>
    /// Отправляет уведомление всем игрокам группы о назначенной сессии.
    /// </summary>
    private async Task NotifyGroupAboutSessionAsync(Group group, Player? admin, CancellationToken ct)
    {
        var localTime = ConvertUtcToLocal(group.CurrentSessionUtc!.Value, admin?.TimeZoneOffset ?? 0);
        var rsvpKeyboard = CreateRsvpKeyboard(group.Id);
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
        var dateStr = localTime.ToString(BotConstants.DateFormats.FullLocalTimeFormat);
        var timeStr = localTime.ToString("HH:mm");
        var announcementText = string.Format(
            BotConstants.PlayerMessages.SessionAnnouncement,
            escapedGroupName,
            dateStr,
            timeStr);
        await NotifyAllInGroupAsync(group, announcementText, rsvpKeyboard, ct);
    }
    /// <summary>
    /// Создаёт клавиатуру для RSVP-ответов.
    /// </summary>
    private InlineKeyboardMarkup CreateRsvpKeyboard(int groupId) => new([
        [
            InlineKeyboardButton.WithCallbackData(BotConstants.UiTexts.ButtonRsvpYes, $"{BotConstants.CallbackPrefixes.RsvpYes}{groupId}"),
            InlineKeyboardButton.WithCallbackData(BotConstants.UiTexts.ButtonRsvpNo, $"{BotConstants.CallbackPrefixes.RsvpNo}{groupId}")
        ]
    ]);

#endregion
#region Обработчики колбэков: RSVP

    /// <summary>
    /// Обрабатывает ответ игрока (подтверждение или отказ) на анонс сессии.
    /// </summary>
    private async Task HandleRsvpAsync(CallbackQuery callbackQuery, long userId, bool isComing, CancellationToken ct)
    {
        var prefix = isComing ? BotConstants.CallbackPrefixes.RsvpYes : BotConstants.CallbackPrefixes.RsvpNo;
        if (!TryParseGroupIdFromCallback(callbackQuery.Data, prefix, out var groupId))
        {
            _logger.LogWarning("Неверный ID группы в RSVP callback: {Data}", callbackQuery.Data);
            return;
        }
        var group = await GetGroupWithPlayersAsync(groupId, ct);
        var player = await Db.Players.FindAsync([userId], ct);
        if (group == null || player == null)
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.RsvpError, true, ct);
            return;
        }
        if (group.SessionStatus != SessionStatus.Pending)
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.PlayerMessages.RsvpStatusFixed, true, ct);
            return;
        }
        UpdatePlayerRsvpStatus(group, userId, isComing);
        await Db.SaveChangesAsync(ct);
        var participationRate = CalculateParticipationRate(group);
        await LogRsvpStatusAsync(group, participationRate);
        await UpdateCallbackResponseAsync(callbackQuery, isComing, ct);
        if (AreAllPlayersResponded(group))
        {
            _logger.LogInformation("Все игроки ответили, запускаем финализацию");
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
    /// Обновляет статус RSVP игрока в группе.
    /// </summary>
    private void UpdatePlayerRsvpStatus(Group group, long userId, bool isComing)
    {
        if (isComing)
        {
            if (!group.ConfirmedPlayerIds.Contains(userId))
                group.ConfirmedPlayerIds.Add(userId);
            group.DeclinedPlayerIds.Remove(userId);
        }
        else
        {
            group.ConfirmedPlayerIds.Remove(userId);
            if (!group.DeclinedPlayerIds.Contains(userId))
                group.DeclinedPlayerIds.Add(userId);
        }
    }
    /// <summary>
    /// Рассчитывает процент участия в сессии.
    /// </summary>
    private double CalculateParticipationRate(Group group)
    {
        var allPlayers = GetTargetPlayers(group);
        var totalPlayers = allPlayers.Count;
        var confirmedCount = group.ConfirmedPlayerIds.Count;
        return totalPlayers > 0 ? (double)confirmedCount / totalPlayers : 0;
    }
    /// <summary>
    /// Логирует статус RSVP для группы.
    /// </summary>
    private async Task LogRsvpStatusAsync(Group group, double participationRate)
    {
        var allPlayers = GetTargetPlayers(group);
        var totalPlayers = allPlayers.Count;
        var confirmedCount = group.ConfirmedPlayerIds.Count;
        var respondedCount = group.ConfirmedPlayerIds.Count + group.DeclinedPlayerIds.Count;
        _logger.LogInformation(
            "RSVP Группа {GroupId}: Подтвердили {ConfirmedCount}/{TotalPlayers} ({ParticipationRate:P1}). Ответили {RespondedCount}/{TotalPlayers}",
            group.Id,
            confirmedCount,
            totalPlayers,
            participationRate,
            respondedCount,
            totalPlayers);
    }
    /// <summary>
    /// Обновляет ответ на callback в зависимости от выбора игрока.
    /// </summary>
    private async Task UpdateCallbackResponseAsync(CallbackQuery callbackQuery, bool isComing, CancellationToken ct)
    {
        var responseText = isComing
            ? BotConstants.PlayerMessages.RsvpConfirmed
            : BotConstants.PlayerMessages.RsvpDeclined;
        await EditTextAsync(callbackQuery, responseText, ct);
        await EditReplyMarkupAsync(callbackQuery, null, ct);
        await AnswerCallbackAsync(callbackQuery, ct);
    }
    /// <summary>
    /// Проверяет, ответили ли все игроки группы.
    /// </summary>
    private bool AreAllPlayersResponded(Group group)
    {
        var allPlayers = GetTargetPlayers(group);
        var totalPlayers = allPlayers.Count;
        var respondedCount = group.ConfirmedPlayerIds.Count + group.DeclinedPlayerIds.Count;
        return respondedCount >= totalPlayers && totalPlayers > 0;
    }

#endregion
#region Обработчики колбэков: Рекомендации

    /// <summary>
    /// Обрабатывает выбор варианта рекомендации администратором.
    /// </summary>
    private async Task HandleSelectRecommendationAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!IsAdmin(userId))
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.AdminMessages.AdminOnlySelectRecommendation, true, ct);
            return;
        }
        if (!TryParseRecommendationCallback(callbackQuery.Data, out var groupId, out var optionIndex))
        {
            _logger.LogWarning("Неверный формат callback выбора рекомендации: {Data}", callbackQuery.Data);
            return;
        }
        var group = await GetGroupWithPlayersAsync(groupId, ct);
        if (group == null)
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.GroupNotFound, true, ct);
            return;
        }
        var recommendationResult = await GetRecommendationsForGroupAsync(group, ct);
        if (!recommendationResult.HasRecommendations || optionIndex >= recommendationResult.Options.Count)
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.RecommendationUnavailable, true, ct);
            return;
        }
        var selectedOption = recommendationResult.Options[optionIndex];
        await UpdateGroupWithSelectedRecommendationAsync(group, selectedOption, ct);
        await NotifyGroupAboutSelectedRecommendationAsync(group, selectedOption, optionIndex, ct);
        await EditTextAsync(callbackQuery, await FormatRecommendationSelectionText(group.Name, selectedOption, optionIndex), ct);
        await AnswerCallbackAsync(callbackQuery, ct);
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
    /// Получает рекомендации для группы.
    /// </summary>
    private async Task<RecommendationResult> GetRecommendationsForGroupAsync(Group group, CancellationToken ct)
    {
        var playersAvailability = await BuildPlayersAvailabilityAsync(group, ct);
        return _recommendationService.FindRecommendations(playersAvailability, BotConstants.MinPlanningDurationHours);
    }
    /// <summary>
    /// Обновляет группу выбранным вариантом рекомендации.
    /// </summary>
    private async Task UpdateGroupWithSelectedRecommendationAsync(Group group, RecommendationOption option, CancellationToken ct)
    {
        group.CurrentSessionUtc = option.ProposedStartTime;
        group.ConfirmedPlayerIds.Clear();
        group.DeclinedPlayerIds.Clear();
        group.SessionStatus = SessionStatus.Pending;
        await Db.SaveChangesAsync(ct);
    }
    /// <summary>
    /// Форматирует текст выбора рекомендации для отображения.
    /// </summary>
    private async Task<string> FormatRecommendationSelectionText(string? groupName, RecommendationOption option, int optionIndex)
    {
        var admin = AdminIds.FirstOrDefault();
        var adminPlayer = await UserService.GetPlayerAsync(admin, CancellationToken.None);
        var localStart = ConvertUtcToLocal(option.ProposedStartTime, adminPlayer?.TimeZoneOffset ?? 0);
        var escapedName = BotConstants.TextHelpers.EscapeMarkdown(groupName ?? "Неизвестно");
        return $"✅ Выбран вариант #{optionIndex + 1}: {localStart:dd.MM HH:mm}";
    }
    /// <summary>
    /// Отправляет уведомление группе о выбранном варианте рекомендации.
    /// </summary>
    private async Task NotifyGroupAboutSelectedRecommendationAsync(Group group, RecommendationOption option, int optionIndex, CancellationToken ct)
    {
        var admin = AdminIds.FirstOrDefault();
        var adminPlayer = await UserService.GetPlayerAsync(admin, CancellationToken.None);
        var localStart = ConvertUtcToLocal(option.ProposedStartTime, adminPlayer?.TimeZoneOffset ?? 0);
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
        var dateStr = localStart.ToString(BotConstants.DateFormats.FullLocalTimeFormat);
        var timeStr = localStart.ToString("HH:mm");
        var duration = (option.ProposedEndTime - option.ProposedStartTime).TotalHours;
        var attendingPlayersText = FormatAttendingPlayersMarkdown(option.AttendingPlayerNames);
        var announcementText = string.Format(
            BotConstants.PlayerMessages.SelectedRecommendationAnnouncement,
            optionIndex + 1,
            escapedGroupName,
            dateStr,
            timeStr,
            option.GetPriorityDescription(),
            option.AttendingPlayersCount,
            option.TotalPlayersCount,
            attendingPlayersText);
        await NotifyAllInGroupAsync(group, announcementText, CreateRsvpKeyboard(group.Id), ct);
    }
    /// <summary>
    /// Форматирует список присутствующих игроков в Markdown.
    /// </summary>
    private string FormatAttendingPlayersMarkdown(List<string> playerNames)
    {
        if (playerNames == null || playerNames.Count == 0)
            return "Нет данных";
        return string.Join(", ", playerNames.Select(name => $"@{BotConstants.TextHelpers.EscapeMarkdown(name)}"));
    }

#endregion
#region Вспомогательные методы

    /// <summary>
    /// Получает группу с загруженными игроками.
    /// </summary>
    private async Task<Group?> GetGroupWithPlayersAsync(int groupId, CancellationToken ct) =>
        await Db.Groups.Include(g => g.Players).FirstOrDefaultAsync(g => g.Id == groupId, ct);
    /// <summary>
    /// Возвращает список всех игроков группы для планирования и голосования.
    /// </summary>
    private List<Player> GetTargetPlayers(Group group) =>
        group.Players.DistinctBy(p => p.TelegramId).ToList();
    /// <summary>
    /// Строит список доступности игроков для сервиса рекомендаций.
    /// </summary>
    private async Task<List<PlayerAvailability>> BuildPlayersAvailabilityAsync(Group group, CancellationToken ct)
    {
        var playersAvailability = new List<PlayerAvailability>();
        foreach (var player in group.Players)
        {
            var slots = await Db.Slots
                .Where(s => s.PlayerId == player.TelegramId)
                .OrderBy(s => s.DateTimeUtc)
                .ToListAsync(ct);
            var availableSlots = slots.Select(s => new TimeSlot
            {
                Start = s.DateTimeUtc,
                End = s.DateTimeUtc.AddHours(1)
            }).ToList();
            playersAvailability.Add(new PlayerAvailability
            {
                PlayerId = player.TelegramId,
                PlayerName = player.Username ?? $"Игрок {player.TelegramId}",
                AvailableSlots = availableSlots,
                PreferredStartTime = availableSlots.FirstOrDefault()?.Start,
                PreferredEndTime = availableSlots.LastOrDefault()?.End
            });
        }
        _logger.LogInformation("Построена доступность для {Count} игроков группы {GroupName}",
            playersAvailability.Count,
            group.Name);
        return playersAvailability;
    }

#endregion
#region Методы финализации сессии

    /// <summary>
    /// Принимает решение о проведении сессии на основе процента подтверждений и присутствия админов.
    /// </summary>
    private async Task FinalizeSessionDecisionAsync(Group group, double participationRate, CancellationToken ct)
    {
        var allPlayers = GetTargetPlayers(group);
        var totalPlayers = allPlayers.Count;
        var confirmedCount = group.ConfirmedPlayerIds.Count;
        var adminsCanAttend = await CheckAdminsAvailabilityAsync(group, group.CurrentSessionUtc!.Value, ct);
        if (participationRate >= BotConstants.ConfirmationThreshold && adminsCanAttend.CanAttend)
        {
            await ConfirmSessionAsync(group, participationRate, confirmedCount, totalPlayers, ct);
        }
        else if (!adminsCanAttend.CanAttend)
        {
            await RescheduleSessionAsync(group, confirmedCount, totalPlayers, participationRate, adminsCanAttend.CannotAttendAdmins, ct);
        }
        else
        {
            await CancelSessionAsync(group, confirmedCount, totalPlayers, participationRate, ct);
        }
    }
    /// <summary>
    /// Проверяет доступность администраторов на сессии.
    /// </summary>
    private async Task<(bool CanAttend, List<Player> CannotAttendAdmins)> CheckAdminsAvailabilityAsync(Group group, DateTime sessionStart, CancellationToken ct)
    {
        var adminsInGroup = GetAdminsInGroup(group);
        var cannotAttend = new List<Player>();
        var sessionEnd = sessionStart.AddHours(BotConstants.DefaultSessionDurationHours);
        foreach (var admin in adminsInGroup)
        {
            var adminSlots = await Db.Slots.Where(s => s.PlayerId == admin.TelegramId).ToListAsync(ct);
            var canAttend = adminSlots.Any(s => s.DateTimeUtc <= sessionStart && s.DateTimeUtc.AddHours(1) > sessionStart);
            if (!canAttend)
                cannotAttend.Add(admin);
        }
        return (cannotAttend.Count == 0, cannotAttend);
    }
    /// <summary>
    /// Подтверждает сессию и отправляет уведомления.
    /// </summary>
    private async Task ConfirmSessionAsync(Group group, double participationRate, int confirmedCount, int totalPlayers, CancellationToken ct)
    {
        group.SessionStatus = SessionStatus.Confirmed;
        await Db.SaveChangesAsync(ct);
        var adminNotice = GetAdminsInGroup(group).Count > 0
            ? "✅ Все администраторы могут присутствовать"
            : "ℹ️ В группе нет администраторов";
        var localTime = ConvertUtcToLocal(group.CurrentSessionUtc!.Value, 3);
        var notificationText = string.Format(
            BotConstants.AdminMessages.SessionConfirmed,
            BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно"),
            localTime.ToString(BotConstants.DateFormats.FullLocalTimeFormat),
            confirmedCount,
            totalPlayers,
            participationRate,
            adminNotice);
        await NotifyMainChatAsync(notificationText, ct);
        _logger.LogInformation("✅ Сессия группы {GroupName} подтверждена ({Rate:P1})", group.Name, participationRate);
    }
    /// <summary>
    /// Запускает перепланирование сессии.
    /// </summary>
    private async Task RescheduleSessionAsync(Group group, int confirmedCount, int totalPlayers, double participationRate, List<Player> cannotAttendAdmins, CancellationToken ct)
    {
        group.SessionStatus = SessionStatus.Rescheduled;
        ResetGroupVotingData(group);
        await Db.SaveChangesAsync(ct);
        var adminsText = string.Join(", ", cannotAttendAdmins.Select(a => a.GetMarkdownUsername()));
        var reason = $"❌ **Администраторы не могут:** {adminsText}";
        await NotifyAdminAndMainChatAboutRescheduleAsync(group, confirmedCount, totalPlayers, participationRate, reason, ct);
        _logger.LogInformation("⚠️ Сессия группы {GroupName} перепланирована — админы не могут присутствовать", group.Name);
        await AutoRunPlanningForGroupAsync(group, ct);
    }
    /// <summary>
    /// Отменяет сессию из-за недостатка игроков.
    /// </summary>
    private async Task CancelSessionAsync(Group group, int confirmedCount, int totalPlayers, double participationRate, CancellationToken ct)
    {
        group.SessionStatus = SessionStatus.Cancelled;
        group.CurrentSessionUtc = null;
        await Db.SaveChangesAsync(ct);
        await NotifyAdminAboutCancellationAsync(group, confirmedCount, totalPlayers, participationRate, ct);
        await NotifyMainChatAsync(
            string.Format(BotConstants.AdminMessages.SessionCancelled, participationRate),
            ct);
        _logger.LogInformation("❌ Сессия группы {GroupName} отменена ({Rate:P1})", group.Name, participationRate);
    }
    /// <summary>
    /// Сбрасывает данные голосования в группе.
    /// </summary>
    private void ResetGroupVotingData(Group group)
    {
        group.CurrentSessionUtc = null;
        group.ConfirmedPlayerIds.Clear();
        group.DeclinedPlayerIds.Clear();
        group.FinishedVotingPlayerIds.Clear();
    }
    /// <summary>
    /// Отправляет уведомления админу и в основной чат о перепланировании.
    /// </summary>
    private async Task NotifyAdminAndMainChatAboutRescheduleAsync(Group group, int confirmedCount, int totalPlayers, double participationRate, string reason, CancellationToken ct)
    {
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
        var notificationText = string.Format(
            BotConstants.AdminMessages.SessionRescheduled,
            escapedGroupName,
            reason,
            confirmedCount,
            totalPlayers,
            participationRate,
            BotConstants.ConfirmationThreshold);
        var retryKeyboard = new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData(BotConstants.UiTexts.ButtonRetryRequest, $"{BotConstants.CallbackPrefixes.StartRequest}{group.Id}")]
        ]);
        await SendToMainAdminAsync(notificationText, retryKeyboard, ct);
        await NotifyMainChatAsync(notificationText, ct);
    }
    /// <summary>
    /// Отправляет уведомление админу об отмене сессии.
    /// </summary>
    private async Task NotifyAdminAboutCancellationAsync(Group group, int confirmedCount, int totalPlayers, double participationRate, CancellationToken ct)
    {
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
        var notificationText = $"⚠️ **Сессия отменена**\n👥 Группа: **{escapedGroupName}**\n✅ Подтвердили: {confirmedCount}/{totalPlayers} ({participationRate:P0})\n🎯 Требуется: {BotConstants.ConfirmationThreshold:P0}\nЗапустить повторный запрос свободного времени?";
        var retryKeyboard = new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData(BotConstants.UiTexts.ButtonRetryRequest, $"{BotConstants.CallbackPrefixes.StartRequest}{group.Id}")]
        ]);
        await SendToMainAdminAsync(notificationText, retryKeyboard, ct);
    }

#endregion
#region Авто-планирование

    /// <summary>
    /// Автоматически запускает поиск окон, выбирает ближайшее и отправляет запросы RSVP игрокам.
    /// </summary>
    private async Task AutoRunPlanningForGroupAsync(Group group, CancellationToken ct)
    {
        var freshGroup = await LoadFreshGroupAsync(group.Id, ct);
        if (freshGroup == null)
        {
            _logger.LogWarning("Группа {GroupId} не найдена для авто-планирования", group.Id);
            return;
        }
        LogPlayerSlotsForDebug(freshGroup);
        var intersections = await SchedulingService.FindIntersectionsAsync(freshGroup.Id, BotConstants.MinPlanningDurationHours);
        if (intersections.Count == 0)
        {
            await HandleNoIntersectionsAsync(freshGroup, ct);
            return;
        }
        var nearestSlot = intersections.OrderBy(i => i.Start).First();
        await ScheduleSessionAndNotifyAsync(freshGroup, nearestSlot, ct);
    }
    /// <summary>
    /// Загружает актуальные данные группы из БД.
    /// </summary>
    private async Task<Group?> LoadFreshGroupAsync(int groupId, CancellationToken ct) =>
        await Db.Groups
            .Include(g => g.Players)
            .ThenInclude(p => p.Slots)
            .AsSplitQuery()
            .FirstOrDefaultAsync(g => g.Id == groupId, ct);
    /// <summary>
    /// Логирует количество слотов у каждого игрока для отладки.
    /// </summary>
    private void LogPlayerSlotsForDebug(Group group)
    {
        foreach (var player in group.Players)
        {
            _logger.LogDebug("Игрок {Username} (ID={TelegramId}) имеет {SlotsCount} слотов",
                player.Username,
                player.TelegramId,
                player.Slots?.Count ?? 0);
        }
    }
    /// <summary>
    /// Планирует сессию на основе найденного слота и уведомляет игроков.
    /// </summary>
    private async Task ScheduleSessionAndNotifyAsync(Group group, DateTimeRange nearestSlot, CancellationToken ct)
    {
        var admin = await UserService.GetPlayerAsync(AdminIds.FirstOrDefault(), ct);
        var localStart = ConvertUtcToLocal(nearestSlot.Start, admin?.TimeZoneOffset ?? 0);
        var localEnd = ConvertUtcToLocal(nearestSlot.End, admin?.TimeZoneOffset ?? 0);
        UpdateGroupSessionData(group, nearestSlot.Start);
        await Db.SaveChangesAsync(ct);
        _logger.LogInformation("✅ Авто-выбор времени для {GroupName}: {StartTime} UTC", group.Name, nearestSlot.Start);
        var announcementText = FormatAutoSessionAnnouncement(group, localStart, localEnd, nearestSlot);
        var rsvpKeyboard = CreateRsvpKeyboard(group.Id);
        await SendRsvpToAllPlayersAsync(group, announcementText, rsvpKeyboard, ct);
        await NotifyAdminAndMainChatAboutAutoPlanningAsync(group, localStart, ct);
    }
    /// <summary>
    /// Обновляет данные сессии в группе.
    /// </summary>
    private void UpdateGroupSessionData(Group group, DateTime sessionStartUtc)
    {
        group.CurrentSessionUtc = sessionStartUtc;
        group.ConfirmedPlayerIds.Clear();
        group.DeclinedPlayerIds.Clear();
        group.SessionStatus = SessionStatus.Pending;
    }
    /// <summary>
    /// Форматирует текст анонса авто-сессии.
    /// </summary>
    private string FormatAutoSessionAnnouncement(Group group, DateTime localStart, DateTime localEnd, DateTimeRange slot)
    {
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
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
                await SendToUserAsync(player.TelegramId, announcementText, rsvpKeyboard, ct);
                _logger.LogDebug("RSVP отправлен игроку {PlayerId}", player.TelegramId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось отправить RSVP игроку {PlayerId}", player.TelegramId);
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
        await SendToUserAsync(admin?.TelegramId ?? 0, adminText, ct);
        await NotifyMainChatAsync(
            string.Format(BotConstants.SystemNotifications.TimeAssigned, localStart.ToString(BotConstants.DateFormats.LocalTimeFormat)),
            ct);
        _logger.LogInformation("✅ Результаты авто-планирования отправлены для группы {GroupName}", group.Name);
    }
    /// <summary>
    /// Обрабатывает ситуацию, когда пересечения не найдены — запускает рекомендации.
    /// </summary>
    private async Task HandleNoIntersectionsAsync(Group group, CancellationToken ct)
    {
        var admin = await UserService.GetPlayerAsync(AdminIds.FirstOrDefault(), ct);
        if (admin == null)
        {
            _logger.LogError("Не удалось найти Администратора для отправки уведомлений");
            return;
        }
        try
        {
            var recommendationResult = await GetRecommendationsForGroupAsync(group, ct);
            if (!recommendationResult.HasRecommendations)
            {
                await NotifyAboutNoRecommendationsAsync(group, admin, ct);
                return;
            }
            await SendBestRecommendationAsync(group, recommendationResult, admin, ct);
        }
        catch (Exception ex)
        {
            await NotifyAboutRecommendationErrorAsync(group, admin, ex, ct);
        }
    }
    /// <summary>
    /// Уведомляет об отсутствии рекомендаций.
    /// </summary>
    private async Task NotifyAboutNoRecommendationsAsync(Group group, Player admin, CancellationToken ct)
    {
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
        await SendToUserAsync(
            admin.TelegramId,
            string.Format(BotConstants.AdminMessages.NoRecommendationsFound, escapedGroupName),
            ct);
        await NotifyMainChatAsync(
            string.Format(BotConstants.SystemNotifications.NoTimeFound, escapedGroupName),
            ct);
        await NotifyAllInGroupAsync(
            group,
            "⚠️ **Не найдено общего времени**\nК сожалению, не удалось подобрать время, когда все свободны.\nМастер получит рекомендации по оптимизации расписания.",
            ct);
    }
    /// <summary>
    /// Отправляет лучшую рекомендацию игрокам.
    /// </summary>
    private async Task SendBestRecommendationAsync(Group group, RecommendationResult result, Player admin, CancellationToken ct)
    {
        var bestOption = result.GetBestOption();
        var localStart = ConvertUtcToLocal(bestOption.ProposedStartTime, admin.TimeZoneOffset);
        var localEnd = ConvertUtcToLocal(bestOption.ProposedEndTime, admin.TimeZoneOffset);
        UpdateGroupSessionData(group, bestOption.ProposedStartTime);
        await Db.SaveChangesAsync(ct);
        var announcementText = FormatRecommendedSessionAnnouncement(group, bestOption, localStart, localEnd);
        var rsvpKeyboard = CreateRsvpKeyboard(group.Id);
        await SendRsvpToAllPlayersAsync(group, announcementText, rsvpKeyboard, ct);
        await NotifyAdminAboutRecommendationAsync(group, bestOption, localStart, ct);
    }
    /// <summary>
    /// Форматирует текст анонса рекомендованной сессии.
    /// </summary>
    private string FormatRecommendedSessionAnnouncement(Group group, RecommendationOption option, DateTime localStart, DateTime localEnd)
    {
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
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
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
        var attendingPlayersText = FormatAttendingPlayersMarkdown(option.AttendingPlayerNames);
        var adminText = $"📊 **Рекомендации для {escapedGroupName}**\n✅ Выбран лучший вариант: **{localStart:dd.MM HH:mm}**\n👥 Участвуют: {option.AttendingPlayersCount}/{option.TotalPlayersCount}\n✅ Свободны: {attendingPlayersText}";
        await SendToMainAdminAsync(adminText, ct);
        _logger.LogInformation("Рекомендации успешно отправлены для группы {GroupName}", group.Name);
    }
    /// <summary>
    /// Уведомляет об ошибке при получении рекомендаций.
    /// </summary>
    private async Task NotifyAboutRecommendationErrorAsync(Group group, Player admin, Exception ex, CancellationToken ct)
    {
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
        var escapedError = BotConstants.TextHelpers.EscapeMarkdown(ex.Message);
        await SendToUserAsync(
            admin.TelegramId,
            string.Format(BotConstants.AdminMessages.PlanningError, escapedGroupName, escapedError),
            ct);
        _logger.LogError(ex, "Критическая ошибка в HandleNoIntersectionsAsync для группы {GroupName}", group.Name);
    }

#endregion
#region Обработчики остальных колбэков

    /// <summary>
    /// Обрабатывает отмену текущего действия.
    /// </summary>
    private async Task HandleCancelActionAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        await SetPlayerStateAsync(userId, PlayerState.Idle, ct);
        await EditTextAsync(callbackQuery, BotConstants.PlayerMessages.ActionCancelled, null, ct);
        await AnswerCallbackAsync(callbackQuery, ct);
    }
    /// <summary>
    /// Обрабатывает установку часового пояса пользователя.
    /// </summary>
    private async Task HandleSetTimeZoneAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!TryParseTimeZoneFromCallback(callbackQuery.Data, out var offset))
        {
            _logger.LogWarning("Неверный формат часового пояса в callback: {Data}", callbackQuery.Data);
            await AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.TimeZoneFormatError, true, ct);
            return;
        }
        var success = await UpdatePlayerTimeZoneAsync(userId, offset, ct);
        if (!success)
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.TimeZoneError, true, ct);
            return;
        }
        _logger.LogInformation("Пользователь {UserId} установил часовой пояс: UTC{Offset:+#;-#;0}", userId, offset);
        var formattedOffset = BotConstants.TextHelpers.FormatTimeZoneOffset(offset);
        var responseText = string.Format(BotConstants.PlayerMessages.TimeZoneSet, formattedOffset);
        await EditTextAsync(callbackQuery, responseText, ct);
        await AnswerCallbackAsync(callbackQuery, ct);
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
            _logger.LogWarning("Не удалось распарсить дату из callback: {Data}", callbackQuery.Data);
            await AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.DateParseError, true, ct);
            return;
        }
        var player = await GetPlayerWithSlotsAsync(userId, ct);
        if (player is null)
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.PlayerNotFound, true, ct);
            return;
        }
        var displayDate = selectedDate.ToString(BotConstants.DateFormats.DisplayFormat);
        var timeGrid = AvailabilityMenu.GetTimeGrid(selectedDate, player.Slots, player.TimeZoneOffset);
        var title = string.Format(BotConstants.PlayerMessages.TimeSelectionTitle, displayDate);
        await EditTextAsync(callbackQuery, title, timeGrid, ct);
        await AnswerCallbackAsync(callbackQuery, ct);
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
            _logger.LogWarning("Неверный формат toggle_time callback: {Data}", callbackQuery.Data);
            await AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.CallbackFormatError, true, ct);
            return;
        }
        var player = await GetPlayerWithSlotsAsync(userId, ct);
        if (player is null)
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.PlayerNotFound, true, ct);
            return;
        }
        var slotTimeUtc = ConvertLocalToUtc(new DateTime(date.Year, date.Month, date.Day, hour % 24, 0, 0), player.TimeZoneOffset);
        await ToggleAvailabilitySlotAsync(player, slotTimeUtc, ct);
        await UpdateTimeGridAsync(callbackQuery, date, player, ct);
        await AnswerCallbackAsync(callbackQuery, ct);
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
            _logger.LogDebug("Удалён слот для пользователя {UserId} на {SlotTimeUtc}", player.TelegramId, slotTimeUtc);
        }
        else
        {
            Db.Slots.Add(new AvailabilitySlot(player.TelegramId, slotTimeUtc));
            _logger.LogDebug("Добавлен слот для пользователя {UserId} на {SlotTimeUtc}", player.TelegramId, slotTimeUtc);
        }
        await Db.SaveChangesAsync(ct);
    }
    /// <summary>
    /// Обновляет сетку времени в сообщении.
    /// </summary>
    private async Task UpdateTimeGridAsync(CallbackQuery callbackQuery, DateTime date, Player player, CancellationToken ct)
    {
        var timeGrid = AvailabilityMenu.GetTimeGrid(date, player.Slots, player.TimeZoneOffset);
        await EditReplyMarkupAsync(callbackQuery, timeGrid, ct);
    }
    /// <summary>
    /// Обрабатывает возврат к выбору дат из выбора времени.
    /// </summary>
    private async Task HandleBackToDatesAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var player = await UserService.GetPlayerAsync(userId, ct);
        var tzOffset = player?.TimeZoneOffset ?? 0;
        var dateCalendar = AvailabilityMenu.GetDateCalendar(tzOffset);
        await EditTextAsync(callbackQuery, BotConstants.PlayerMessages.CalendarTitle, dateCalendar, ct);
        await AnswerCallbackAsync(callbackQuery, ct);
    }

#endregion
#region Завершение голосования

    /// <summary>
    /// Обрабатывает завершение голосования по расписанию.
    /// </summary>
    private async Task HandleFinishVotingAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var player = await GetPlayerWithGroupsAsync(userId, ct);
        if (player is null)
        {
            await AnswerCallbackAsync(callbackQuery, ct);
            return;
        }
        var groupsToCheck = await LoadFreshGroupsAsync(player.Groups.Select(g => g.Id).ToList(), ct);
        var groupsUpdated = UpdateFinishedVotingForGroups(groupsToCheck, userId);
        if (groupsUpdated.Count > 0)
        {
            await Db.SaveChangesAsync(ct);
            _logger.LogInformation("Сохранено {Count} групп с обновлённым статусом голосования", groupsUpdated.Count);
        }
        await EditTextAsync(callbackQuery, BotConstants.PlayerMessages.DataSaved, ct);
        await NotifyMainChatAsync(string.Format(BotConstants.SystemNotifications.PlayerFinishedVoting, player.GetMarkdownUsername()), ct);
        await CheckGroupsReadinessAsync(groupsToCheck, userId, ct);
        await AnswerCallbackAsync(callbackQuery, ct);
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
        foreach (var group in groups)
        {
            if (group.CurrentSessionUtc == null && !group.FinishedVotingPlayerIds.Contains(userId))
            {
                group.FinishedVotingPlayerIds.Add(userId);
                updated.Add(group);
                _logger.LogDebug("Игрок {UserId} добавил себя в finished voting для группы {GroupName}", userId, group.Name);
            }
        }
        return updated;
    }
    /// <summary>
    /// Проверяет готовность групп к планированию.
    /// </summary>
    private async Task CheckGroupsReadinessAsync(List<Group> groups, long userId, CancellationToken ct)
    {
        foreach (var group in groups)
        {
            if (await AreAllPlayersFinishedVotingAsync(group, ct))
            {
                _logger.LogInformation("Все игроки ({Count}) завершили голосование для {GroupName}", group.Players.Count, group.Name);
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
            _logger.LogInformation("Группа {GroupName} имеет назначенную сессию на {SessionTime}. Запуск проверки доступности...", group.Name, group.CurrentSessionUtc);
            await CheckSessionAvailabilityAsync(group, ct);
        }
        else
        {
            _logger.LogInformation("Группа {GroupName} не имеет сессии. Запуск авто-планирования...", group.Name);
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
        _logger.LogInformation("⏳ Ожидаем завершения голосования: {Finished}/{Total} для группы {GroupName}", finishedCount, totalCount, group.Name);
    }
    /// <summary>
    /// Проверяет, завершили ли все игроки группы голосование.
    /// </summary>
    private async Task<bool> AreAllPlayersFinishedVotingAsync(Group group, CancellationToken ct)
    {
        var freshGroup = await LoadFreshGroupAsync(group.Id, ct);
        if (freshGroup?.Players == null || freshGroup.Players.Count == 0)
        {
            _logger.LogWarning("Группа {GroupId} не имеет игроков для проверки голосования", group?.Id);
            return false;
        }
        var finishedVotingIds = freshGroup.FinishedVotingPlayerIds?.ToHashSet() ?? new HashSet<long>();
        foreach (var player in freshGroup.Players)
        {
            if (player?.TelegramId == null || !finishedVotingIds.Contains(player.TelegramId))
            {
                _logger.LogDebug("Игрок {TelegramId} ещё не завершил голосование в группе {GroupName}", player.TelegramId, freshGroup.Name);
                return false;
            }
        }
        _logger.LogDebug("Все {Count} игроков завершили голосование в группе {GroupName}", freshGroup.Players.Count, freshGroup.Name);
        return true;
    }

#endregion
#region Групповые операции

    /// <summary>
    /// Обрабатывает вступление пользователя в группу.
    /// </summary>
    private async Task HandleJoinGroupAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var user = await UserService.GetPlayerAsync(userId, ct);
        if (user is null)
        {
            Logger.LogError("Не удалось найти игрока с TelegramId {UserId} при попытке вступить в группу", userId);
            return;
        }
        if (!TryParseGroupIdFromCallback(callbackQuery.Data, BotConstants.CallbackPrefixes.JoinGroup, out var groupId))
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.CallbackDataMissing, true, ct);
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
            ? string.Format(BotConstants.PlayerMessages.AlreadyInGroup, BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно"))
            : BotConstants.ErrorMessages.GroupNotFound;
        await AnswerCallbackAsync(callbackQuery, message, true, ct);
    }
    /// <summary>
    /// Обрабатывает успешное вступление в группу.
    /// </summary>
    private async Task HandleJoinSuccessAsync(CallbackQuery callbackQuery, Player user, int groupId, CancellationToken ct)
    {
        var addedGroup = await GetGroupAsync(groupId, ct);
        if (addedGroup is not null)
        {
            _logger.LogInformation("Пользователь {UserId} вступил в группу {GroupId} [{GroupName}]", user.TelegramId, groupId, addedGroup.Name);
            if (addedGroup.SessionStatus != SessionStatus.Pending || addedGroup.FinishedVotingPlayerIds.Count > 0)
            {
                await ResetGroupVotingOnNewPlayerAsync(addedGroup, user, ct);
            }
            var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(addedGroup.Name ?? "Неизвестно");
            await EditTextAsync(callbackQuery, string.Format(BotConstants.PlayerMessages.JoinedGroup, escapedGroupName), ct);
            await NotifyMainChatAsync(string.Format(BotConstants.SystemNotifications.PlayerJoinedGroup, user.GetMarkdownUsername(), escapedGroupName), ct);
        }
        await AnswerCallbackAsync(callbackQuery, ct);
    }
    /// <summary>
    /// Сбрасывает голосование в группе при присоединении нового игрока.
    /// </summary>
    private async Task ResetGroupVotingOnNewPlayerAsync(Group group, Player user, CancellationToken ct)
    {
        ResetGroupVotingData(group);
        await Db.SaveChangesAsync(ct);
        _logger.LogInformation("Группа {GroupName}: голосование сброшено из-за присоединения нового игрока {UserId}", group.Name, user.TelegramId);
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
        await NotifyMainChatAsync(
            string.Format(BotConstants.SystemNotifications.GroupChanged, user.GetMarkdownUsername(), escapedGroupName),
            ct);
    }
    /// <summary>
    /// Обрабатывает подтверждение удаления группы (админ).
    /// </summary>
    private async Task HandleConfirmDeleteGroupAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!IsAdmin(userId))
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.AdminMessages.AdminOnlyDeleteGroup, true, ct);
            return;
        }
        if (!TryParseGroupIdFromCallback(callbackQuery.Data, BotConstants.CallbackPrefixes.ConfirmDeleteGroup, out var groupId))
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.GroupNotFound, true, ct);
            return;
        }
        var group = await GetGroupAsync(groupId, ct);
        if (group is null)
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.GroupNotFound, true, ct);
            return;
        }
        Db.Groups.Remove(group);
        await Db.SaveChangesAsync(ct);
        _logger.LogInformation("Админ {AdminId} удалил группу {GroupId} [{GroupName}]", userId, groupId, group.Name);
        var escapedName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
        await EditTextAsync(callbackQuery, string.Format(BotConstants.AdminMessages.GroupDeleted, escapedName), ct);
        await AnswerCallbackAsync(callbackQuery, ct);
    }
    /// <summary>
    /// Обрабатывает выход пользователя из группы.
    /// </summary>
    private async Task HandleLeaveGroupAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!TryParseGroupIdFromCallback(callbackQuery.Data, BotConstants.CallbackPrefixes.LeaveGroup, out var groupId))
        {
            await AnswerCallbackAsync(callbackQuery, ct);
            return;
        }
        var success = await RemovePlayerFromGroupAsync(userId, groupId, ct);
        if (!success)
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.PlayerMessages.NotInGroup, ct: ct);
            return;
        }
        var group = await GetGroupAsync(groupId, ct);
        if (group is not null)
        {
            _logger.LogInformation("Пользователь {UserId} покинул группу {GroupId} [{GroupName}]", userId, groupId, group.Name);
            var escapedName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
            await EditTextAsync(callbackQuery, string.Format(BotConstants.PlayerMessages.LeftGroup, escapedName), ct);
        }
        await AnswerCallbackAsync(callbackQuery, ct);
    }

#endregion
#region Запрос свободного времени

    /// <summary>
    /// Отправляет запрос на ввод свободного времени для новой игровой сессии.
    /// </summary>
    private async Task HandleStartRequestAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!IsAdmin(userId))
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.AdminMessages.AdminOnlyRequestFreeTime, true, ct);
            return;
        }
        if (!TryParseGroupIdFromCallback(callbackQuery.Data, BotConstants.CallbackPrefixes.StartRequest, out var groupId))
        {
            _logger.LogWarning("Неверный формат ID группы в запросе свободного времени: {Data}", callbackQuery.Data);
            await AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.CallbackDataMissing, true, ct);
            return;
        }
        var group = await GetGroupWithPlayersAsync(groupId, ct);
        if (group is null)
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.GroupNotFound, true, ct);
            return;
        }
        await ResetGroupVotingDataForRequestAsync(group, ct);
        await NotifyGroupAboutFreeTimeRequestAsync(group, ct);
        await RespondToAdminAboutRequestAsync(callbackQuery, group, ct);
        await NotifyMainChatAboutRequestAsync(group, ct);
        await AnswerCallbackAsync(callbackQuery, string.Format(BotConstants.PlayerMessages.RequestSentCallbackResponse, group.Name), ct: ct);
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
        group.SessionStatus = SessionStatus.Pending;
        await Db.SaveChangesAsync(ct);
        _logger.LogInformation("Группа {GroupName}: сброшены данные голосования (SessionUtc={SessionUtc}, FinishedVoting={FinishedCount}, HadSession={HadSession})",
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
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
        var requestMessage = string.Format(BotConstants.PlayerMessages.FreeTimeRequest, escapedGroupName);
        await NotifyAllInGroupAsync(group, requestMessage, ct);
        _logger.LogInformation("Запрос свободного времени отправлен в чат группы {GroupName}", group.Name);
    }
    /// <summary>
    /// Отвечает админу о результате отправки запроса.
    /// </summary>
    private async Task RespondToAdminAboutRequestAsync(CallbackQuery callbackQuery, Group group, CancellationToken ct)
    {
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
        var responseText = string.Format(BotConstants.PlayerMessages.RequestSentToGroup, escapedGroupName);
        await EditTextAsync(callbackQuery, responseText, ct);
    }
    /// <summary>
    /// Уведомляет основной чат о запросе свободного времени.
    /// </summary>
    private async Task NotifyMainChatAboutRequestAsync(Group group, CancellationToken ct)
    {
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
        var mainChatText = string.Format(BotConstants.SystemNotifications.FreeTimeRequested, escapedGroupName, "Игроки, проверьте чат группы и заполните расписание!");
        await NotifyMainChatAsync(mainChatText, ct);
    }

#endregion
#region Запуск планирования

    /// <summary>
    /// Обрабатывает запуск поиска свободных окон для группы.
    /// </summary>
    private async Task HandleStartPlanningAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!IsAdmin(userId))
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.AdminMessages.AdminOnlyPlanning, true, ct);
            return;
        }
        if (!TryParseGroupIdFromCallback(callbackQuery.Data, BotConstants.CallbackPrefixes.StartPlan, out var groupId))
        {
            await AnswerCallbackAsync(callbackQuery, BotConstants.ErrorMessages.CallbackDataMissing, true, ct);
            return;
        }
        _logger.LogInformation("Запуск поиска окон для группы {GroupId}, мин. длительность: {Hours}ч", groupId, BotConstants.MinPlanningDurationHours);
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
            var recommendationResult = await GetRecommendationsForGroupAsync(group, ct);
            if (recommendationResult.HasRecommendations)
            {
                await ShowRecommendationsAsync(callbackQuery, group, recommendationResult, ct);
                return;
            }
        }
        await EditTextAsync(callbackQuery, BotConstants.AdminMessages.NoIntersectionsFound, ct);
    }
    /// <summary>
    /// Показывает результаты планирования администратору.
    /// </summary>
    private async Task ShowPlanningResultsAsync(CallbackQuery callbackQuery, int groupId, List<DateTimeRange> intersections, CancellationToken ct)
    {
        var resultText = "🗓 **Найденные окна (Ваше время):**\n";
        var buttons = new List<InlineKeyboardButton[]>();
        foreach (var interval in intersections.Take(BotConstants.MaxPlanningResultsToShow))
        {
            var admin = await UserService.GetPlayerAsync(AdminIds.FirstOrDefault(), ct);
            var localStart = ConvertUtcToLocal(interval.Start, admin?.TimeZoneOffset ?? 0);
            var localEnd = ConvertUtcToLocal(interval.End, admin?.TimeZoneOffset ?? 0);
            var timeStr = $"{localStart:dd.MM HH:mm} - {localEnd:HH:mm}";
            resultText += $"🔹 {timeStr}\n";
            buttons.Add([
                InlineKeyboardButton.WithCallbackData(
                    $"✅ {timeStr}",
                    $"{BotConstants.CallbackPrefixes.ConfirmTime}{groupId}_{interval.Start:yyyyMMddHH}")
            ]);
        }
        await EditTextAsync(callbackQuery, resultText, new InlineKeyboardMarkup(buttons), ct);
    }
    /// <summary>
    /// Показывает рекомендации администратору.
    /// </summary>
    private async Task ShowRecommendationsAsync(CallbackQuery callbackQuery, Group group, RecommendationResult result, CancellationToken ct)
    {
        var resultText = string.Format(BotConstants.AdminMessages.RecommendationsTitle, BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно"), result.OptionsCount) + "\n";
        var buttons = new List<InlineKeyboardButton[]>();
        var admin = await UserService.GetPlayerAsync(AdminIds.FirstOrDefault(), ct);
        foreach (var option in result.Options.Take(BotConstants.MaxPlanningResultsToShow))
        {
            var localStart = ConvertUtcToLocal(option.ProposedStartTime, admin?.TimeZoneOffset ?? 0);
            var localEnd = ConvertUtcToLocal(option.ProposedEndTime, admin?.TimeZoneOffset ?? 0);
            var timeStr = $"{localStart:dd.MM HH:mm} - {localEnd:HH:mm}";
            var index = result.Options.IndexOf(option);
            resultText += $"#{index + 1}. 🕒 {timeStr}\n";
            resultText += $"   👥 {option.AttendingPlayersCount}/{option.TotalPlayersCount} игроков\n";
            resultText += $"   ✅ Свободны: {option.GetAttendingPlayersMarkdown()}\n";
            buttons.Add([
                InlineKeyboardButton.WithCallbackData(
                    $"✅ Вариант #{index + 1}",
                    $"{BotConstants.CallbackPrefixes.SelectRecommendation}{group.Id}_{index}")
            ]);
        }
        await EditTextAsync(callbackQuery, resultText, new InlineKeyboardMarkup(buttons), ct);
    }

#endregion
#region Проверка доступности сессии

    /// <summary>
    /// Проверяет, могут ли игроки присутствовать на уже назначенной сессии.
    /// </summary>
    private async Task CheckSessionAvailabilityAsync(Group group, CancellationToken ct)
    {
        if (group.CurrentSessionUtc == null)
        {
            Logger.LogDebug("Группа {GroupName} не имеет назначенной сессии", group.Name);
            return;
        }
        Logger.LogInformation("Проверка доступности для сессии {GroupName} на {SessionTime}", group.Name, group.CurrentSessionUtc);
        var sessionStart = group.CurrentSessionUtc.Value;
        var (canAttendPlayers, cannotAttendPlayers) = await CheckPlayersAvailabilityAsync(group, sessionStart, ct);
        var adminsCanAttend = await CheckAdminsAvailabilityForSessionAsync(group, canAttendPlayers, ct);
        var attendanceRate = CalculateAttendanceRate(canAttendPlayers.Count, group.Players.Count);
        if (attendanceRate >= BotConstants.ConfirmationThreshold && adminsCanAttend)
        {
            await ConfirmExistingSessionAsync(group, canAttendPlayers, cannotAttendPlayers, attendanceRate, ct);
        }
        else
        {
            await RescheduleExistingSessionAsync(group, canAttendPlayers, cannotAttendPlayers, attendanceRate, adminsCanAttend, ct);
        }
    }
    /// <summary>
    /// Проверяет доступность всех игроков группы.
    /// </summary>
    private async Task<(List<Player> CanAttend, List<Player> CannotAttend)> CheckPlayersAvailabilityAsync(Group group, DateTime sessionStart, CancellationToken ct)
    {
        var canAttend = new List<Player>();
        var cannotAttend = new List<Player>();
        var sessionEnd = sessionStart.AddHours(BotConstants.DefaultSessionDurationHours);
        foreach (var player in group.Players)
        {
            var playerSlots = await Db.Slots.Where(s => s.PlayerId == player.TelegramId).ToListAsync(ct);
            var canPlayerAttend = playerSlots.Any(s => s.DateTimeUtc <= sessionStart && s.DateTimeUtc.AddHours(1) > sessionStart);
            (canPlayerAttend ? canAttend : cannotAttend).Add(player);
        }
        return (canAttend, cannotAttend);
    }
    /// <summary>
    /// Проверяет доступность администраторов для сессии.
    /// </summary>
    private async Task<bool> CheckAdminsAvailabilityForSessionAsync(Group group, List<Player> canAttendPlayers, CancellationToken ct)
    {
        var adminsInGroup = GetAdminsInGroup(group);
        return adminsInGroup.All(admin => canAttendPlayers.Contains(admin));
    }
    /// <summary>
    /// Рассчитывает процент присутствующих игроков.
    /// </summary>
    private double CalculateAttendanceRate(int canAttendCount, int totalCount) =>
        totalCount > 0 ? (double)canAttendCount / totalCount : 0;
    /// <summary>
    /// Подтверждает существующую сессию.
    /// </summary>
    private async Task ConfirmExistingSessionAsync(Group group, List<Player> canAttendPlayers, List<Player> cannotAttendPlayers, double attendanceRate, CancellationToken ct)
    {
        group.SessionStatus = SessionStatus.Confirmed;
        await Db.SaveChangesAsync(ct);
        var attendanceText = cannotAttendPlayers.Any()
            ? $"⚠️ **Не смогут присутствовать:** {string.Join(", ", cannotAttendPlayers.Select(p => p.GetMarkdownUsername()))}"
            : "✅ Все игроки могут присутствовать!";
        var admin = await UserService.GetPlayerAsync(AdminIds.FirstOrDefault(), ct);
        var localStart = ConvertUtcToLocal(group.CurrentSessionUtc!.Value, admin?.TimeZoneOffset ?? 0);
        var localTimeStr = localStart.ToString(BotConstants.DateFormats.FullLocalTimeFormat);
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
        var notificationText = string.Format(
            BotConstants.PlayerMessages.SessionStillValid,
            escapedGroupName,
            localTimeStr,
            canAttendPlayers.Count,
            group.Players.Count,
            attendanceRate,
            BotConstants.ConfirmationThreshold,
            attendanceText);
        await NotifyMainChatAsync(notificationText, ct);
        await NotifyUnavailablePlayersAsync(cannotAttendPlayers, group, localStart, attendanceRate, ct);
        Logger.LogInformation("✅ Сессия {GroupName} подтверждена ({Rate:P1}). Не смогут: {CannotAttendCount}", group.Name, attendanceRate, cannotAttendPlayers.Count);
    }
    /// <summary>
    /// Уведомляет игроков, которые не могут присутствовать.
    /// </summary>
    private async Task NotifyUnavailablePlayersAsync(List<Player> cannotAttendPlayers, Group group, DateTime localStart, double attendanceRate, CancellationToken ct)
    {
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
        var localTimeStr = localStart.ToString(BotConstants.DateFormats.FullLocalTimeFormat);
        foreach (var player in cannotAttendPlayers)
        {
            await SendToUserAsync(
                player.TelegramId,
                string.Format(BotConstants.PlayerMessages.PlayerCannotAttendWarning, escapedGroupName, localTimeStr, attendanceRate),
                ct);
        }
    }
    /// <summary>
    /// Запускает перепланирование существующей сессии.
    /// </summary>
    private async Task RescheduleExistingSessionAsync(Group group, List<Player> canAttendPlayers, List<Player> cannotAttendPlayers, double attendanceRate, bool adminsCanAttend, CancellationToken ct)
    {
        var reason = !adminsCanAttend
            ? $"❌ **Администраторы не могут:** {string.Join(", ", GetAdminsInGroup(group).Where(a => !canAttendPlayers.Contains(a)).Select(p => p.GetMarkdownUsername()))}"
            : $"❌ **Мало игроков:** {canAttendPlayers.Count}/{group.Players.Count} ({attendanceRate:P0})";
        ResetGroupVotingData(group);
        group.SessionStatus = SessionStatus.Rescheduled;
        await Db.SaveChangesAsync(ct);
        var escapedGroupName = BotConstants.TextHelpers.EscapeMarkdown(group.Name ?? "Неизвестно");
        var notificationText = string.Format(
            BotConstants.PlayerMessages.NewPlanningRequired,
            escapedGroupName,
            reason,
            BotConstants.ConfirmationThreshold);
        await NotifyMainChatAsync(notificationText, ct);
        Logger.LogInformation("⚠️ Сессия {GroupName} перепланирована. Причина: {Reason}", group.Name, !adminsCanAttend ? "админы не могут" : "мало игроков");
        await AutoRunPlanningForGroupAsync(group, ct);
    }

#endregion
#region Вспомогательные методы для работы с сущностями

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

#endregion
}
