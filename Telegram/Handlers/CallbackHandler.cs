using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TgDataPlanner.Common;
using TgDataPlanner.Data;
using TgDataPlanner.Data.Entities;
using TgDataPlanner.Services;
using TgDataPlanner.Telegram.Menus;

namespace TgDataPlanner.Telegram.Handlers;

/// <summary>
/// Обработчик нажатий на inline-кнопки (CallbackQuery).
/// Маршрутизирует колбэки к соответствующим методам и управляет взаимодействием с пользователем.
/// </summary>
public class CallbackHandler : BaseHandler
{
    private readonly SessionPlanningService _planningService;
    private readonly ILogger<CallbackHandler> _logger;

    /// <summary>
    /// Префиксы callback-данных для маршрутизации.
    /// </summary>
    private static class CallbackPrefixes
    {
        public const string CancelAction = "cancel_action";
        public const string SetTimeZone = "set_tz_";
        public const string PickDate = "pick_date_";
        public const string ToggleTime = "toggle_time_";
        public const string BackToDates = "back_to_dates";
        public const string StartPlan = "start_plan_";
        public const string FinishVoting = "finish_voting";
        public const string JoinGroup = "join_group_";
        public const string ConfirmDeleteGroup = "confirm_delete_";
        public const string ConfirmTime = "confirm_time_";
        public const string LeaveGroup = "leave_group_";
        public const string RsvpYes = "rsvp_yes_";
        public const string RsvpNo = "rsvp_no_";
        public const string StartRequest = "start_request_";
    }

    /// <summary>
    /// Минимальная длительность окна планирования в часах.
    /// </summary>
    private const int MinPlanningDurationHours = 3;

    /// <summary>
    /// Максимальное количество вариантов окон для отображения пользователю.
    /// </summary>
    private const int MaxPlanningResultsToShow = 5;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="CallbackHandler"/>.
    /// </summary>
    /// <param name="config">Конфигурация приложения.</param>
    /// <param name="botClient">Клиент Telegram Bot API.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    /// <param name="db">Контекст базы данных</param>
    /// <param name="userService">Сервис управления пользователями.</param>
    /// <param name="schedulingService">Сервис расписания.</param>
    /// <param name="planningService">Сервис планирования.</param>
    public CallbackHandler(
        IConfiguration config,
        ITelegramBotClient botClient,
        ILogger<CallbackHandler> logger,
        AppDbContext db,
        UserService userService,
        SchedulingService schedulingService,
        SessionPlanningService planningService)
        : base(config, botClient, logger, db, userService, schedulingService)
    {
        _planningService = planningService;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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
            await AnswerCallbackAsync(callbackQuery, ct: ct);
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
            // Ожидаемое поведение при остановке бота
            _logger.LogDebug("Обработка callback прервана из-за отмены операции");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Необработанное исключение при обработке callback '{Data}' от пользователя {UserId}",
                callbackQuery.Data,
                userId);

            await AnswerCallbackAsync(callbackQuery, "⚠️ Произошла ошибка при обработке действия", showAlert: true, ct);
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
            _logger.LogError("Попытка маршрутизации callback без данных от пользователя {UserId}", userId);
            return;
        }

        // Проверка прав администратора для критичных действий
        if (data.StartsWith(CallbackPrefixes.ConfirmTime) && !IsAdmin(userId))
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
            case CallbackPrefixes.CancelAction:
                await HandleCancelActionAsync(callbackQuery, userId, ct);
                break;

            case var _ when data.StartsWith(CallbackPrefixes.SetTimeZone):
                await HandleSetTimeZoneAsync(callbackQuery, userId, ct);
                break;

            case var _ when data.StartsWith(CallbackPrefixes.PickDate):
                await HandlePickDateAsync(callbackQuery, userId, ct);
                break;

            case var _ when data.StartsWith(CallbackPrefixes.ToggleTime):
                await HandleToggleTimeAsync(callbackQuery, userId, ct);
                break;

            case CallbackPrefixes.BackToDates:
                await HandleBackToDatesAsync(callbackQuery, userId, ct);
                break;

            case var _ when data.StartsWith(CallbackPrefixes.StartPlan):
                await HandleStartPlanningAsync(callbackQuery, userId, ct);
                break;

            case CallbackPrefixes.FinishVoting:
                await HandleFinishVotingAsync(callbackQuery, userId, ct);
                break;

            case var _ when data.StartsWith(CallbackPrefixes.JoinGroup):
                await HandleJoinGroupAsync(callbackQuery, userId, ct);
                break;

            case var _ when data.StartsWith(CallbackPrefixes.ConfirmDeleteGroup):
                await HandleConfirmDeleteGroupAsync(callbackQuery, userId, ct);
                break;

            case var _ when data.StartsWith(CallbackPrefixes.LeaveGroup):
                await HandleLeaveGroupAsync(callbackQuery, userId, ct);
                break;
            
            case var _ when data.StartsWith(CallbackPrefixes.ConfirmTime):
                await HandleConfirmTimeAsync(callbackQuery, player, ct);
                break;

            case var _ when data.StartsWith(CallbackPrefixes.RsvpYes):
                await HandleRsvpAsync(callbackQuery, userId, isComing: true, ct);
                break;

            case var _ when data.StartsWith(CallbackPrefixes.RsvpNo):
                await HandleRsvpAsync(callbackQuery, userId, isComing: false, ct);
                break;
            
            case var _ when data.StartsWith(CallbackPrefixes.StartRequest):
                await HandleStartRequestAsync(callbackQuery, userId, ct); // Было: break;
                break;

            default:
                _logger.LogWarning("Неизвестный callback-запрос: {Data}", data);
                await AnswerCallbackAsync(callbackQuery, ct: ct);
                break;
        }
    }

    #region Обработчики колбэков

    /// <summary>
    /// Автоматически запускает поиск окон, выбирает ближайшее и отправляет запросы RSVP игрокам.
    /// </summary>
    private async Task AutoRunPlanningForGroupAsync(Group group, CancellationToken ct)
    {
        // 🔹 Перезагружаем группу для актуального количества игроков
        var freshGroup = await Db.Groups
            .Include(g => g.Players)
            .AsSplitQuery()
            .FirstOrDefaultAsync(g => g.Id == group.Id, ct);
        if (freshGroup == null)
        {
            _logger.LogWarning("Группа {GroupId} не найдена для авто-планирования", group.Id);
            return;
        }
        _logger.LogInformation("🤖 Авто-планирование для группы {GroupName} ({PlayersCount})",
            freshGroup.Name, freshGroup.Players.Count);
        var intersections = await SchedulingService.FindIntersectionsAsync(
            freshGroup.Id, MinPlanningDurationHours);
        if (intersections.Count == 0)
        {
            // 🔹 Уведомляем админа
            await SendTextAsync(
                AdminId,
                $"😔 **Авто-планирование: {freshGroup.Name}**\n" +
                $"К сожалению, общие окна не найдены.\n" +
                $"💡 Попробуйте:\n" +
                $"• Попросить игроков добавить больше вариантов\n" +
                $"• Уменьшить минимальную длительность сессии",
                ct: ct);
            // 🔹 Уведомляем общий чат
            await NotifyMainChatAsync(
                $"😔 **Группа {freshGroup.Name}**: не найдено подходящего времени\n" +
                $"Игрокам будет отправлено уведомление с рекомендациями.",
                ct: ct);
            // 🔹 Уведомляем игроков группы
            await NotifyAllInGroupAsync(
                freshGroup,
                $"⚠️ **Не найдено общего времени**\n" +
                $"К сожалению, не удалось подобрать время, когда все свободны.\n" +
                $"Мастер получит рекомендации по оптимизации расписания.",
                ct: ct);
            return;
        }
        // 🔹 Выбираем ближайшее окно (сортировка по началу времени)
        var nearestSlot = intersections.OrderBy(i => i.Start).First();
        // Получаем админа для конвертации времени в локальное (для текста)
        var admin = await UserService.GetPlayerAsync(AdminId, ct);
        var localStart = ConvertUtcToLocal(nearestSlot.Start, admin?.TimeZoneOffset ?? 0);
        var localEnd = ConvertUtcToLocal(nearestSlot.End, admin?.TimeZoneOffset ?? 0);
        // 🔹 Автоматически подтверждаем сессию в БД
        freshGroup.CurrentSessionUtc = nearestSlot.Start;
        freshGroup.ConfirmedPlayerIds = new List<long>();
        freshGroup.DeclinedPlayerIds = new List<long>();
        freshGroup.SessionStatus = SessionStatus.Pending; // Ожидаем RSVP от игроков
        await Db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "✅ Авто-выбор времени для {GroupName}: {StartTime} UTC",
            freshGroup.Name, nearestSlot.Start);
        // 🔹 Формируем клавиатуру RSVP
        var rsvpKeyboard = new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithCallbackData("⚔️ ИДУ", $"{CallbackPrefixes.RsvpYes}{freshGroup.Id}"),
                InlineKeyboardButton.WithCallbackData("🚫 НЕ СМОГУ", $"{CallbackPrefixes.RsvpNo}{freshGroup.Id}")
            ]
        ]);
        var announcementText =
            $"⚔️ **АВТО-НАЗНАЧЕНИЕ СЕССИИ** ⚔️\n" +
            $"🤖 Бот подобрал оптимальное время на основе вашего расписания.\n" +
            $"👥 Группа: **{freshGroup.Name}**\n" +
            $"📅 Дата: **{localStart:dd.MM (ddd)}**\n" +
            $"🕒 Начало: **{localStart:HH:mm}** (по МСК)\n" +
            $"⏳ Длительность: **{(nearestSlot.End - nearestSlot.Start).TotalHours} ч.**\n" +
            $"❗ Пожалуйста, подтвердите явку кнопками ниже!\n" +
            $"🎯 Для подтверждения сессии требуется **75%** игроков.";
        // 🔹 Отправляем уведомление ВСЕМ игрокам группы (включая админа)
        foreach (var player in freshGroup.Players)
        {
            try
            {
                await SendTextAsync(
                    chatId: player.TelegramId,
                    text: announcementText,
                    replyMarkup: rsvpKeyboard,
                    ct: ct);
                _logger.LogDebug("RSVP отправлен игроку {PlayerId}", player.TelegramId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось отправить RSVP игроку {PlayerId}", player.TelegramId);
            }
        }
        // 🔹 Уведомление админу об успешном авто-выборе
        await SendTextAsync(
            AdminId,
            $"🤖 **Авто-планирование завершено**\n" +
            $"✅ Выбрано ближайшее окно: **{localStart:dd.MM HH:mm}**\n" +
            $"👥 Игроков в группе: **{freshGroup.Players.Count}**\n" +
            $"Игрокам отправлены запросы на подтверждение. " +
            $"Как только 75% подтвердят — сессия будет финализирована.",
            ct: ct);
        // 🔹 Уведомление в чат группы
        await NotifyMainChatAsync(
            $"🎯 **Время игры назначено!**\n" +
            $"Бот автоматически подобрал ближайшее окно: **{localStart:dd.MM HH:mm}**\n" +
            $"Игроки, проверьте ЛС от бота и подтвердите участие! ⚔️",
            ct: ct);
        _logger.LogInformation("✅ Результаты авто-планирования отправлены для группы {GroupName}", freshGroup.Name);
    }
        
    /// <summary>
    /// Обрабатывает выбор админом конкретного времени для сессии и публикует анонс.
    /// </summary>
    private async Task HandleConfirmTimeAsync(CallbackQuery callbackQuery, Player player, CancellationToken ct)
    {
        if (player.TelegramId != AdminId)
        {
            await AnswerCallbackAsync(callbackQuery, "🧙 Только Мастер может назначать игру!", showAlert: true, ct);
            return;
        }

        var dataParts = callbackQuery.Data!.Split('_');
        if (dataParts.Length < 4 || !int.TryParse(dataParts[2], out var groupId))
        {
            _logger.LogWarning("Неверный формат данных в callback подтверждения времени: {Data}", callbackQuery.Data);
            return;
        }

        var timeRaw = dataParts[3]; // Формат yyyyMMddHH
        if (!DateTime.TryParseExact(timeRaw, "yyyyMMddHH", null, System.Globalization.DateTimeStyles.None, out var sessionTimeUtc))
        {
            _logger.LogError("Ошибка парсинга даты сессии из callback: {TimeRaw}", timeRaw);
            return;
        }

        var group = await Db.Groups.Include(g => g.Players).FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group == null)
        {
            await AnswerCallbackAsync(callbackQuery, "⚠️ Группа не найдена", showAlert: true, ct);
            return;
        }

        // Обновляем данные группы
        group.CurrentSessionUtc = sessionTimeUtc;
        group.ConfirmedPlayerIds = [];  // 🔹 Сброс подтверждений
        group.DeclinedPlayerIds = [];   // 🔹 Сброс отказов
        group.SessionStatus = SessionStatus.Pending;  // 🔹 Статус ожидания
        await Db.SaveChangesAsync(ct);

        _logger.LogInformation("Мастер {UserId} назначил сессию для группы {GroupName} на {Time} UTC", 
            player.TelegramId, group.Name, sessionTimeUtc);

        // Получаем локальное время админа для текста
        var admin = await Db.Players.FindAsync([player.TelegramId], ct);
        var localTime = sessionTimeUtc.AddHours(admin?.TimeZoneOffset ?? 0);

        // Редактируем сообщение у админа
        await EditTextAsync(callbackQuery, $"✅ Сессия для **{group.Name}** назначена на {localTime:dd.MM HH:mm}", ct: ct);

        // Готовим анонс с кнопками RSVP
        var rsvpKeyboard = new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithCallbackData("⚔️ ИДУ", $"{CallbackPrefixes.RsvpYes}{groupId}"),
                InlineKeyboardButton.WithCallbackData("🚫 НЕ СМОГУ", $"{CallbackPrefixes.RsvpNo}{groupId}")
            ]
        ]);

        await NotifyAllInGroupAsync(
            group,
            $"⚔️ **ОБЪЯВЛЕН СБОР НА ПАРТИЮ!** ⚔️\n\n" +
            $"👥 Группа: **{group.Name}**\n" +
            $"📅 Дата: **{localTime:dd.MM (ddd)}**\n" +
            $"🕒 Начало: **{localTime:HH:mm}** (по МСК)\n\n" +
            $"Игроки, подтвердите явку кнопками ниже!", 
            rsvpKeyboard, ct);
        
        await AnswerCallbackAsync(callbackQuery, ct: ct);
    }
        
    /// <summary>
    /// Обрабатывает ответ игрока (подтверждение или отказ) на анонс сессии.
    /// </summary>
    private async Task HandleRsvpAsync(CallbackQuery callbackQuery, long userId, bool isComing, CancellationToken ct)
    {
        var prefix = isComing ? CallbackPrefixes.RsvpYes : CallbackPrefixes.RsvpNo;
        var groupIdString = callbackQuery.Data!.Replace(prefix, string.Empty);
        if (!int.TryParse(groupIdString, out var groupId))
        {
            _logger.LogWarning("Неверный ID группы в RSVP callback: {Data}", callbackQuery.Data);
            return;
        }
        // 🔹 Перезагружаем группу с актуальным списком игроков
        var group = await Db.Groups
            .Include(g => g.Players)
            .FirstOrDefaultAsync(g => g.Id == groupId, ct);
        var player = await Db.Players.FindAsync([userId], ct);
        if (group == null || player == null)
        {
            await AnswerCallbackAsync(callbackQuery, "⚠️ Ошибка: данные не найдены", showAlert: true, ct);
            return;
        }
        if (group.SessionStatus != SessionStatus.Pending)
        {
            await AnswerCallbackAsync(callbackQuery, "ℹ️ Статус сессии уже определён", showAlert: true, ct);
            return;
        }
        // Обновление списков
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
        await Db.SaveChangesAsync(ct);
        // 🔹 Подсчет от ВСЕХ игроков (включая админа) — используем GetTargetPlayers
        var allPlayers = GetTargetPlayers(group);
        var totalPlayers = allPlayers.Count;
        var confirmedCount = group.ConfirmedPlayerIds.Count;
        var respondedCount = group.ConfirmedPlayerIds.Count + group.DeclinedPlayerIds.Count;
        var participationRate = totalPlayers > 0
            ? (double)confirmedCount / totalPlayers
            : 0;
        // 🔹 ИСПРАВЛЕНО: используем именованные параметры для корректного логирования
        _logger.LogInformation(
            "RSVP Группа {GroupId}: Подтвердили {ConfirmedCount}/{TotalPlayers} ({ParticipationRate:P1}). Ответили {RespondedCount}/{TotalPlayers}",
            groupId, confirmedCount, totalPlayers, participationRate, respondedCount, totalPlayers);
        // Обновление UI
        await EditTextAsync(callbackQuery, isComing ? "✅ Вы подтвердили участие." : "❌ Вы отказались.", ct: ct);
        await EditReplyMarkupAsync(callbackQuery, null, ct: ct);
        await AnswerCallbackAsync(callbackQuery, ct: ct);
        // 🔹 ФИНАЛИЗАЦИЯ: Только если ответили ВСЕ игроки
        if (respondedCount >= totalPlayers && totalPlayers > 0)
        {
            _logger.LogInformation("Все игроки ответили, запускаем финализацию");
            await FinalizeSessionDecisionAsync(group, participationRate, ct);
        }
        else
        {
            var remaining = totalPlayers - respondedCount;
            _logger.LogInformation("⏳ Ожидаем ещё {Remaining} ответов из {Total}", remaining, totalPlayers);
        }
    }
    
    /// <summary>
    /// Обрабатывает попытку выполнения админ-действия не-администратором.
    /// </summary>
    private async Task HandleAdminOnlyActionAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        _logger.LogWarning(
            "Пользователь {UserId} попытался выполнить админ-действие: {Data}",
            callbackQuery.From.Id,
            callbackQuery.Data);

        await AnswerCallbackAsync(callbackQuery, "🔒 Только администратор может выполнить это действие", showAlert: true, ct);
    }

    /// <summary>
    /// Обрабатывает отмену текущего действия.
    /// </summary>
    private async Task HandleCancelActionAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        // Сбрасываем состояние через UserService
        await SetPlayerStateAsync(userId, PlayerState.Idle, ct);

        await EditTextAsync(
            callbackQuery,
            "🚫 Действие отменено.",
            replyMarkup: null,
            ct: ct);

        await AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Обрабатывает установку часового пояса пользователя.
    /// </summary>
    private async Task HandleSetTimeZoneAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var offsetString = callbackQuery.Data!.Replace(CallbackPrefixes.SetTimeZone, string.Empty);

        if (!int.TryParse(offsetString, out var offset))
        {
            _logger.LogWarning("Неверный формат часового пояса в callback: {Data}", callbackQuery.Data);
            await AnswerCallbackAsync(callbackQuery, "⚠️ Неверный формат часового пояса", showAlert: true, ct);
            return;
        }

        // Обновляем часовой пояс через UserService
        var success = await UpdatePlayerTimeZoneAsync(userId, offset, ct);
        if (!success)
        {
            await AnswerCallbackAsync(callbackQuery, "⚠️ Ошибка при обновлении настроек", showAlert: true, ct);
            return;
        }

        _logger.LogInformation(
            "Пользователь {UserId} установил часовой пояс: UTC{Offset:+#;-#;0}",
            userId, offset);

        await EditTextAsync(
            callbackQuery,
            $"✅ Ваш часовой пояс установлен: **UTC {(offset >= 0 ? "+" : "")}{offset}**",
            ct: ct);

        await AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Обрабатывает выбор даты для отметки свободного времени.
    /// </summary>
    private async Task HandlePickDateAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var dateStr = callbackQuery.Data!.Replace(CallbackPrefixes.PickDate, string.Empty);

        if (!DateTime.TryParse(dateStr, out var selectedDate))
        {
            _logger.LogWarning("Не удалось распарсить дату из callback: {Data}", callbackQuery.Data);
            await AnswerCallbackAsync(callbackQuery, "⚠️ Ошибка формата даты", showAlert: true, ct);
            return;
        }

        // Получаем игрока со слотами через UserService
        var player = await GetPlayerWithSlotsAsync(userId, ct);
        if (player is null)
        {
            await AnswerCallbackAsync(callbackQuery, "⚠️ Пользователь не найден", showAlert: true, ct);
            return;
        }

        await EditTextAsync(
            callbackQuery,
            $"🕒 Выберите время для **{selectedDate:dd.MM}**:",
            replyMarkup: AvailabilityMenu.GetTimeGrid(selectedDate, player.Slots, player.TimeZoneOffset),
            ct: ct);

        await AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Обрабатывает переключение доступности для конкретного часа.
    /// </summary>
    private async Task HandleToggleTimeAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var parts = callbackQuery.Data!.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            _logger.LogWarning("Неверный формат toggle_time callback: {Data}", callbackQuery.Data);
            await AnswerCallbackAsync(callbackQuery, "⚠️ Ошибка обработки", showAlert: true, ct);
            return;
        }

        var date = DateTime.Parse(parts[2]);
        var hour = int.Parse(parts[3]);

        var player = await GetPlayerWithSlotsAsync(userId, ct);
        if (player is null)
        {
            await AnswerCallbackAsync(callbackQuery, "⚠️ Пользователь не найден", showAlert: true, ct);
            return;
        }

        var slotTimeUtc = ConvertLocalToUtc(
            new DateTime(date.Year, date.Month, date.Day, hour % 24, 0, 0),
            player.TimeZoneOffset);

        var existingSlot = player.Slots.FirstOrDefault(s => s.DateTimeUtc == slotTimeUtc);

        if (existingSlot is not null)
        {
            Db.Slots.Remove(existingSlot);
            _logger.LogDebug("Удалён слот для пользователя {UserId} на {SlotTimeUtc}", userId, slotTimeUtc);
        }
        else
        {
            Db.Slots.Add(new AvailabilitySlot
            {
                PlayerId = player.TelegramId,
                DateTimeUtc = slotTimeUtc
            });
            _logger.LogDebug("Добавлен слот для пользователя {UserId} на {SlotTimeUtc}", userId, slotTimeUtc);
        }

        await Db.SaveChangesAsync(ct);

        await EditReplyMarkupAsync(
            callbackQuery,
            AvailabilityMenu.GetTimeGrid(date, player.Slots, player.TimeZoneOffset),
            ct: ct);

        await AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Обрабатывает возврат к выбору дат из выбора времени.
    /// </summary>
    private async Task HandleBackToDatesAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var player = await UserService.GetPlayerAsync(userId, ct);
        var tzOffset = player?.TimeZoneOffset ?? 0;

        await EditTextAsync(
            callbackQuery,
            "📅 **Выбор доступного времени**\nВыберите дату, чтобы отметить часы, когда вы свободны:",
            replyMarkup: AvailabilityMenu.GetDateCalendar(tzOffset),
            ct: ct);

        await AnswerCallbackAsync(callbackQuery, ct: ct);
    }
    
    /// <summary>
    /// Отправляет запрос на ввод свободного времени для новой игровой сессии.
    /// </summary>
    private async Task HandleStartRequestAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        // Проверяем права администратора
        if (!IsAdmin(userId))
        {
            await AnswerCallbackAsync(callbackQuery, "🔒 Только Мастер может запрашивать свободное время", true, ct);
            return;
        }
        // Парсим ID группы из callback-данных
        var groupIdString = callbackQuery.Data!.Replace(CallbackPrefixes.StartRequest, string.Empty);
        if (!int.TryParse(groupIdString, out var groupId))
        {
            _logger.LogWarning("Неверный формат ID группы в запросе свободного времени: {Data}", callbackQuery.Data);
            await AnswerCallbackAsync(callbackQuery, "⚠️ Ошибка обработки запроса", true, ct);
            return;
        }
        // Получаем группу с игроками
        var group = await Db.Groups
            .Include(g => g.Players)
            .FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null)
        {
            await AnswerCallbackAsync(callbackQuery, "⚠️ Группа не найдена", true, ct);
            return;
        }
        _logger.LogInformation(
            "Мастер {AdminId} инициировал запрос свободного времени для группы {GroupName} ({GroupId})",
            userId,
            group.Name,
            groupId);
        // 🔹 ПОЛНЫЙ СБРОС СЕССИИ И ГОЛОСОВАНИЯ
        var hadSession = group.CurrentSessionUtc.HasValue;
        group.CurrentSessionUtc = null;
        group.FinishedVotingPlayerIds.Clear();
        group.ConfirmedPlayerIds.Clear();
        group.DeclinedPlayerIds.Clear();
        group.SessionStatus = SessionStatus.Pending;
        await Db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Группа {GroupName}: сброшены данные голосования (SessionUtc={SessionUtc}, FinishedVoting={FinishedCount}, HadSession={HadSession})",
            group.Name,
            group.CurrentSessionUtc,
            group.FinishedVotingPlayerIds.Count,
            hadSession);
        // Формируем сообщение для игроков
        var requestMessage =
            $"🎲 **Запрос свободного времени**" +
            $"Мастер запрашивает ваше расписание для планирования следующей сессии группы **{group.Name}**." +
            $"👉 Пожалуйста, укажите когда вы свободны, используя команду /free" +
            $"📝 Инструкция:" +
            $"1. Нажмите /free или введите эту команду в чат с ботом" +
            $"2. Выберите удобные даты в календаре" +
            $"3. Отметьте часы, когда вы доступны для игры" +
            $"4. Подтвердите выбор кнопкой «✅ ЗАВЕРШИТЬ ЗАПОЛНЕНИЕ»" +
            $"⏰ Чем быстрее вы заполните расписание, тем скорее Мастер сможет назначить игру!";
        // 🔹 ОТПРАВЛЯЕМ СООБЩЕНИЕ В ЧАТ ГРУППЫ (ОДНО СООБЩЕНИЕ ДЛЯ ВСЕХ)
        await NotifyAllInGroupAsync(group, requestMessage, ct: ct);
        _logger.LogInformation("Запрос свободного времени отправлен в чат группы {GroupName}", group.Name);
        // Уведомляем админа о результате
        var responseText = hadSession
            ? $"✅ Запрос отправлен!" +
              $"📬 Уведомление отправлено в чат группы" +
              $"👥 Группа: **{group.Name}**" +
              $"🔄 **Предыдущая сессия отменена** — данные голосования сброшены." +
              $"Как только все игроки нажмут «Завершить заполнение», запустится авто-планирование."
            : $"✅ Запрос отправлен!" +
              $"📬 Уведомление отправлено в чат группы" +
              $"👥 Группа: **{group.Name}**" +
              $"🔄 Данные голосования сброшены." +
              $"Как только все игроки нажмут «Завершить заполнение», запустится авто-планирование.";
        await EditTextAsync(
            callbackQuery,
            responseText,
            ct: ct);
        // Отправляем уведомление в основной чат (если настроен)
        var mainChatText = hadSession
            ? $"🔔 Мастер запросил свободное время для группы **{group.Name}**. " +
              $"⚠️ Предыдущая сессия отменена — требуется новое планирование!"
            : $"🔔 Мастер запросил свободное время для группы **{group.Name}**. " +
              $"Игроки, проверьте чат группы и заполните расписание!";
        await NotifyMainChatAsync(mainChatText, ct);
        await AnswerCallbackAsync(callbackQuery,
            $"Запрос отправлен в чат группы {group.Name}",
            ct: ct);
    }

    /// <summary>
    /// Обрабатывает запуск поиска свободных окон для группы.
    /// </summary>
    private async Task HandleStartPlanningAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!IsAdmin(userId))
        {
            await AnswerCallbackAsync(callbackQuery, "🔒 Только администратор может запускать планирование", showAlert: true, ct);
            return;
        }

        var groupId = int.Parse(callbackQuery.Data!.Replace(CallbackPrefixes.StartPlan, string.Empty));

        _logger.LogInformation(
            "Запуск поиска окон для группы {GroupId}, мин. длительность: {Hours}ч",
            groupId, MinPlanningDurationHours);

        var intersections = await SchedulingService.FindIntersectionsAsync(groupId, MinPlanningDurationHours);

        if (intersections.Count == 0)
        {
            await EditTextAsync(
                callbackQuery,
                "😔 **Пересечений не найдено.** Все игроки заняты в разное время.\n\n*Запустить режим рекомендаций? (В разработке)*",
                ct: ct);
            return;
        }

        var resultText = "🗓 **Найденные окна (Ваше время):**\n\n";
        var buttons = new List<InlineKeyboardButton[]>();

        foreach (var interval in intersections.Take(MaxPlanningResultsToShow))
        {
            var admin = await UserService.GetPlayerAsync(userId, ct);
            var localStart = ConvertUtcToLocal(interval.Start, admin?.TimeZoneOffset ?? 0);
            var localEnd = ConvertUtcToLocal(interval.End, admin?.TimeZoneOffset ?? 0);

            var timeStr = $"{localStart:dd.MM HH:mm} - {localEnd:HH:mm}";
            resultText += $"🔹 {timeStr}\n";

            buttons.Add([
                InlineKeyboardButton.WithCallbackData(
                    $"✅ {timeStr}",
                    $"{CallbackPrefixes.ConfirmTime}{groupId}_{interval.Start:yyyyMMddHH}")
            ]);
        }

        await EditTextAsync(
            callbackQuery,
            resultText,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            ct: ct);
    }
    
    /// <summary>
    /// Обрабатывает завершение голосования по расписанию.
    /// </summary>
    private async Task HandleFinishVotingAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var player = await Db.Players
            .Include(p => p.Groups)
            .FirstOrDefaultAsync(p => p.TelegramId == userId, ct);
        if (player is null)
        {
            await AnswerCallbackAsync(callbackQuery, ct: ct);
            return;
        }
        _logger.LogDebug("Игрок {UserId} нажал Завершить. Групп в списке: {Count}",
            userId, player.Groups.Count);
        // 🔹 Собираем ВСЕ группы игрока для проверки (без разделения на категории)
        var groupsToCheck = new List<Group>();
        foreach (var groupRef in player.Groups.ToList())
        {
            // ВАЖНО: Перезагружаем группу со всеми игроками из БД
            var group = await Db.Groups
                .Include(g => g.Players)
                .AsSplitQuery()
                .FirstOrDefaultAsync(g => g.Id == groupRef.Id, ct);
            if (group == null)
            {
                _logger.LogWarning("Группа {GroupId} не найдена в БД", groupRef.Id);
                continue;
            }
            // 🔹 Явно загружаем примитивную коллекцию, если она пуста
            if (group.FinishedVotingPlayerIds == null)
            {
                group.FinishedVotingPlayerIds = await Db.Groups
                    .Where(g => g.Id == group.Id)
                    .Select(g => g.FinishedVotingPlayerIds)
                    .FirstOrDefaultAsync(ct) ?? new List<long>();
            }
            _logger.LogDebug("Группа {GroupName}: FinishedVotingPlayerIds.Count = {FinishedCount}, Players.Count = {PlayersCount}, CurrentSessionUtc = {SessionTime}",
                group.Name, group.FinishedVotingPlayerIds.Count, group.Players.Count, group.CurrentSessionUtc);
            groupsToCheck.Add(group);
        }
        // 🔹 Обновляем FinishedVotingPlayerIds для групп БЕЗ сессии
        var groupsUpdated = new List<Group>();
        foreach (var group in groupsToCheck)
        {
            if (group.CurrentSessionUtc == null && !group.FinishedVotingPlayerIds.Contains(userId))
            {
                group.FinishedVotingPlayerIds.Add(userId);
                groupsUpdated.Add(group);
                _logger.LogDebug("Игрок {UserId} добавил себя в finished voting для группы {GroupName}", userId, group.Name);
            }
        }
        if (groupsUpdated.Count > 0)
        {
            await Db.SaveChangesAsync(ct);
            _logger.LogInformation("Сохранено {Count} групп с обновлённым статусом голосования", groupsUpdated.Count);
        }
        await EditTextAsync(callbackQuery, "✅ Данные сохранены!", ct: ct);
        await NotifyMainChatAsync(
            $"🔔 **@{player.GetMarkdownUsername()}** завершил заполнение расписания!",
            ct);
        // 🔹 ЕДИНЫЙ ЦИКЛ ПРОВЕРКИ — без дублирования
        foreach (var group in groupsToCheck)
        {
            // 🔹 Перезагружаем группу ещё раз после SaveChanges для актуальных данных
            var freshGroup = await Db.Groups
                .Include(g => g.Players)
                .AsSplitQuery()
                .FirstOrDefaultAsync(g => g.Id == group.Id, ct);
            if (freshGroup == null)
            {
                _logger.LogWarning("Группа {GroupId} не найдена после SaveChanges", group.Id);
                continue;
            }
            // 🔹 Явно загружаем примитивную коллекцию для freshGroup
            if (freshGroup.FinishedVotingPlayerIds == null || freshGroup.FinishedVotingPlayerIds.Count == 0)
            {
                freshGroup.FinishedVotingPlayerIds = await Db.Groups
                    .Where(g => g.Id == freshGroup.Id)
                    .Select(g => g.FinishedVotingPlayerIds)
                    .FirstOrDefaultAsync(ct) ?? new List<long>();
            }
            // 🔹 Логирование с актуальным количеством игроков
            _logger.LogInformation(
                "Проверка группы {GroupName}: {Finished}/{Total} игроков завершили голосование, SessionUtc={SessionUtc}",
                freshGroup.Name,
                freshGroup.FinishedVotingPlayerIds.Count,
                freshGroup.Players.Count,
                freshGroup.CurrentSessionUtc);
            if (await AreAllPlayersFinishedVotingAsync(freshGroup, ct))
            {
                _logger.LogInformation(
                    "Все игроки ({Count}) завершили голосование для {GroupName}",
                    freshGroup.Players.Count,
                    freshGroup.Name);
                // 🔹 ЛОГИКА: Если сессия уже есть — проверяем доступность
                if (freshGroup.CurrentSessionUtc != null)
                {
                    _logger.LogInformation(
                        "Группа {GroupName} имеет назначенную сессию на {SessionTime}. Запуск проверки доступности...",
                        freshGroup.Name, freshGroup.CurrentSessionUtc);
                    await CheckSessionAvailabilityAsync(freshGroup, ct);
                }
                else
                {
                    // 🔹 ЛОГИКА: Если сессии нет — запускаем авто-планирование
                    _logger.LogInformation(
                        "Группа {GroupName} не имеет сессии. Запуск авто-планирования...",
                        freshGroup.Name);
                    await AutoRunPlanningForGroupAsync(freshGroup, ct);
                }
            }
            else
            {
                var finishedCount = freshGroup.FinishedVotingPlayerIds.Count;
                var totalCount = freshGroup.Players.Count;
                _logger.LogInformation("⏳ Ожидаем завершения голосования: {Finished}/{Total} для группы {GroupName}",
                    finishedCount,
                    totalCount,
                    freshGroup.Name);
            }
        }
        await AnswerCallbackAsync(callbackQuery, ct: ct);
    }

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
        var groupId = int.Parse(callbackQuery.Data!.Replace(CallbackPrefixes.JoinGroup, string.Empty));
        // Используем UserService для добавления в группу
        var success = await AddPlayerToGroupAsync(userId, groupId, ct);
        if (!success)
        {
            var group = await GetGroupAsync(groupId, ct);
            var message = group is not null
                ? $"ℹ️ Вы уже состоите в группе **{group.Name}**"
                : "⚠️ Группа не найдена";
            await AnswerCallbackAsync(callbackQuery, message, showAlert: true, ct);
            return;
        }
        var addedGroup = await GetGroupAsync(groupId, ct);
        if (addedGroup is not null)
        {
            _logger.LogInformation("Пользователь {UserId} вступил в группу {GroupId} [{GroupName}]", userId, groupId, addedGroup.Name);
            
            // 🔹 СБРОС ГОЛОСОВАНИЯ: новый игрок присоединился — все должны заново подтвердить расписание
            if (addedGroup.SessionStatus != SessionStatus.Pending || addedGroup.FinishedVotingPlayerIds.Count > 0)
            {
                addedGroup.FinishedVotingPlayerIds.Clear();
                addedGroup.ConfirmedPlayerIds.Clear();
                addedGroup.DeclinedPlayerIds.Clear();
                addedGroup.CurrentSessionUtc = null;
                addedGroup.SessionStatus = SessionStatus.Pending;
                await Db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Группа {GroupName}: голосование сброшено из-за присоединения нового игрока {UserId}",
                    addedGroup.Name, userId);
                await NotifyMainChatAsync(
                    $"⚠️ **Состав группы изменён**\n" +
                    $"Игрок {user.GetMarkdownUsername()} присоединился к группе **{addedGroup.Name}**.\n" +
                    $"Голосование сброшено — всем игрокам нужно заново заполнить расписание.",
                    ct);
            }
            
            await EditTextAsync(callbackQuery, $"⚔️ Вы вступили в группу **{addedGroup.Name}**!", ct: ct);
            await NotifyMainChatAsync($"⚔️ Игрок {user.GetMarkdownUsername()} вступил в группу **{addedGroup.Name}**!", ct: ct);
        }
        await AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Обрабатывает подтверждение удаления группы (админ).
    /// </summary>
    private async Task HandleConfirmDeleteGroupAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        if (!IsAdmin(userId))
        {
            await AnswerCallbackAsync(callbackQuery, "🔒 Только администратор может удалять группы", showAlert: true, ct);
            return;
        }

        var groupId = int.Parse(callbackQuery.Data!.Replace(CallbackPrefixes.ConfirmDeleteGroup, string.Empty));
        var group = await GetGroupAsync(groupId, ct);

        if (group is null)
        {
            await AnswerCallbackAsync(callbackQuery, "⚠️ Группа не найдена", showAlert: true, ct);
            return;
        }

        Db.Groups.Remove(group);
        await Db.SaveChangesAsync(ct);

        _logger.LogInformation("Админ {AdminId} удалил группу {GroupId} [{GroupName}]", userId, groupId, group.Name);

        await EditTextAsync(callbackQuery, $"🗑 Группа **{group.Name}** удалена.", ct: ct);
        await AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Обрабатывает выход пользователя из группы.
    /// </summary>
    private async Task HandleLeaveGroupAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var groupId = int.Parse(callbackQuery.Data!.Replace(CallbackPrefixes.LeaveGroup, string.Empty));

        var success = await RemovePlayerFromGroupAsync(userId, groupId, ct);

        if (!success)
        {
            await AnswerCallbackAsync(callbackQuery, "ℹ️ Вы не состоите в этой группе", ct: ct);
            return;
        }

        var group = await GetGroupAsync(groupId, ct);
        if (group is not null)
        {
            _logger.LogInformation("Пользователь {UserId} покинул группу {GroupId} [{GroupName}]", userId, groupId, group.Name);
            await EditTextAsync(callbackQuery, $"🚪 Вы покинули группу **{group.Name}**.", ct: ct);
        }

        await AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    #endregion

    #region Вспомогательные методы для работы с сущностями

    /// <summary>
    /// Получает игрока с загруженными слотами доступности.
    /// </summary>
    /// <remarks>
    /// Этот метод оставлен с прямым доступом к DbContext, так как UserService
    /// не предоставляет метод с Include для слотов. В будущем можно добавить
    /// UserService.GetPlayerWithSlotsAsync().
    /// </remarks>
    private async Task<Player?> GetPlayerWithSlotsAsync(long telegramId, CancellationToken ct) =>
        await Db.Players
            .Include(p => p.Slots)  // ← Убрали .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TelegramId == telegramId, ct);

    /// <summary>
    /// Получает группу по идентификатору.
    /// </summary>
    private async Task<Group?> GetGroupAsync(int groupId, CancellationToken ct) =>
        await Db.Groups.FindAsync([groupId], ct);

    /// <summary>
    /// Проверяет, есть ли у всех игроков группы хотя бы один слот доступности.
    /// </summary>
    private async Task<bool> AreAllPlayersReadyAsync(Group group, CancellationToken ct)
    {
        var targetPlayers = GetTargetPlayers(group);

        if (targetPlayers.Count == 0)
        {
            _logger.LogWarning("Группа {GroupName} не имеет игроков", group.Name);
            return true;
        }

        var playerIds = targetPlayers.Select(p => p.TelegramId).ToList();
    
        // Получаем всех, у кого ЕСТЬ слоты, одним запросом
        var playersWithSlots = Db.Slots
            .Where(s => playerIds.Contains(s.PlayerId))
            .Select(s => s.PlayerId);
    
        var playersWithSlotsList = await playersWithSlots.ToListAsync(ct);
        var missingPlayers = playerIds.Except(playersWithSlotsList).ToList();

        if (missingPlayers.Any())
        {
            _logger.LogInformation(
                "Группа {GroupName} не готова. Ожидаем игроков: {MissingIds}", 
                group.Name, 
                string.Join(", ", missingPlayers));
            return false;
        }

        _logger.LogInformation(
            "✅ Все игроки ({Count}) группы {GroupName} заполнили расписание", 
            targetPlayers.Count, group.Name);
        return true;
    }
    
    /// <summary>
    /// Принимает решение о проведении сессии на основе процента подтверждений.
    /// </summary>
    private async Task FinalizeSessionDecisionAsync(Group group, double participationRate, CancellationToken ct)
    {
        const double Threshold = 0.75;
        var allPlayers = GetTargetPlayers(group);
        var totalPlayers = allPlayers.Count;
        var confirmedCount = group.ConfirmedPlayerIds.Count;

        _logger.LogInformation(
            "Финализация группы {GroupName}: {Confirmed}/{Total} ({Rate:P1}), порог {Threshold:P0}",
            group.Name, confirmedCount, totalPlayers, participationRate, Threshold);

        if (participationRate >= Threshold)
        {
            group.SessionStatus = SessionStatus.Confirmed;
            await Db.SaveChangesAsync(ct);

            await NotifyMainChatAsync(
                $"🎉 **Сессия подтверждена!**\n\n" +
                $"👥 Группа: **{group.Name}**\n" +
                $"📅 Дата: **{group.CurrentSessionUtc?.AddHours(3):dd.MM (ddd) HH:mm}** (МСК)\n" +
                $"✅ Подтвердили: {confirmedCount}/{totalPlayers} ({participationRate:P0})\n\n" +
                $"Ждём всех в назначенное время! ⚔️",
                ct: ct);

            _logger.LogInformation("✅ Сессия группы {GroupName} подтверждена ({Rate:P1})", group.Name, participationRate);
        }
        else
        {
            group.SessionStatus = SessionStatus.Cancelled;
            group.CurrentSessionUtc = null;
            await Db.SaveChangesAsync(ct);

            await SendTextAsync(
                AdminId,
                $"⚠️ **Сессия отменена**\n\n" +
                $"👥 Группа: **{group.Name}**\n" +
                $"✅ Подтвердили: {confirmedCount}/{totalPlayers} ({participationRate:P0})\n" +
                $"🎯 Требуется: {Threshold:P0}\n\n" +
                $"Запустить повторный запрос свободного времени?",
                replyMarkup: new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("🔁 Повторить запрос", $"{CallbackPrefixes.StartRequest}{group.Id}")]
                ]),
                ct: ct);

            await NotifyMainChatAsync(
                $"😔 **Сессия отменена**\n\n" +
                $"К сожалению, не набралось достаточное количество игроков ({participationRate:P0}).\n" +
                $"Мастер получит уведомление и, возможно, запустит новый сбор времени.",
                ct: ct);

            _logger.LogInformation("❌ Сессия группы {GroupName} отменена ({Rate:P1})", group.Name, participationRate);
        }
    }
    
    /// <summary>
    /// Возвращает список всех игроков группы для планирования и голосования (включая админа).
    /// </summary>
    private List<Player> GetTargetPlayers(Group group)
    {
        var players = group.Players
            .DistinctBy(p => p.TelegramId)
            .ToList();

        _logger.LogDebug(
            "Группа {GroupName}: Всего игроков {Count}. IDs: {PlayerIds}",
            group.Name, 
            players.Count, 
            string.Join(", ", players.Select(p => p.TelegramId)));

        return players;
    }
    
    /// <summary>
    /// Проверяет, завершили ли все игроки группы голосование.
    /// </summary>
    /// <param name="group">Группа для проверки.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>True, если все игроки завершили голосование; иначе False.</returns>
    private async Task<bool> AreAllPlayersFinishedVotingAsync(Group group, CancellationToken ct)
    {
        // 🔹 Перезагружаем группу с актуальным списком игроков
        var freshGroup = await Db.Groups
            .Include(g => g.Players)
            .AsSplitQuery()
            .FirstOrDefaultAsync(g => g.Id == group.Id, ct);
        
        if (freshGroup?.Players == null || freshGroup.Players.Count == 0)
        {
            _logger.LogWarning("Группа {GroupId} не имеет игроков для проверки голосования", group?.Id);
            return false;
        }
        
        // 🔹 Явно загружаем примитивную коллекцию
        if (freshGroup.FinishedVotingPlayerIds == null || freshGroup.FinishedVotingPlayerIds.Count == 0)
        {
            freshGroup.FinishedVotingPlayerIds = await Db.Groups
                .Where(g => g.Id == freshGroup.Id)
                .Select(g => g.FinishedVotingPlayerIds)
                .FirstOrDefaultAsync(ct) ?? new List<long>();
        }
        
        _logger.LogDebug(
            "DEBUG: FinishedVotingPlayerIds.Count = {FinishedCount}, Players.Count = {PlayersCount}",
            freshGroup.FinishedVotingPlayerIds.Count,
            freshGroup.Players.Count);
        
        // Создаём HashSet для быстрого поиска завершивших голосование
        var finishedVotingIds = freshGroup.FinishedVotingPlayerIds.ToHashSet();
        
        // Проверяем каждого игрока группы: есть ли его TelegramId в списке завершивших
        foreach (var player in freshGroup.Players)
        {
            if (player?.TelegramId == null)
            {
                _logger.LogWarning("Игрок в группе {GroupName} имеет пустой TelegramId", freshGroup.Name);
                continue;
            }
            if (!finishedVotingIds.Contains(player.TelegramId))
            {
                _logger.LogDebug(
                    "Игрок {TelegramId} ещё не завершил голосование в группе {GroupName}",
                    player.TelegramId,
                    freshGroup.Name);
                return false;
            }
        }
        _logger.LogDebug(
            "Все {Count} игроков завершили голосование в группе {GroupName}",
            freshGroup.Players.Count,
            freshGroup.Name);
        return true;
    }
    
    /// <summary>
    /// Проверяет, могут ли игроки присутствовать на уже назначенной сессии.
    /// Если меньше 75% могут — запускает повторное авто-планирование.
    /// </summary>
    private async Task CheckSessionAvailabilityAsync(Group group, CancellationToken ct)
    {
        if (group.CurrentSessionUtc == null)
        {
            _logger.LogDebug("Группа {GroupName} не имеет назначенной сессии", group.Name);
            return;
        }
        _logger.LogInformation("Проверка доступности для сессии {GroupName} на {SessionTime}",
            group.Name, group.CurrentSessionUtc);
        var sessionStart = group.CurrentSessionUtc.Value;
        var sessionEnd = sessionStart.AddHours(3); // Предполагаем 3-часовую сессию
        // 🔹 Подсчитываем, кто может присутствовать
        var canAttendPlayers = new List<Player>();
        var cannotAttendPlayers = new List<Player>();
        var totalPlayers = group.Players.Count;
        foreach (var player in group.Players)
        {
            var playerSlots = await Db.Slots
                .Where(s => s.PlayerId == player.TelegramId)
                .ToListAsync(ct);
            var canAttend = playerSlots.Any(s =>
                s.DateTimeUtc <= sessionStart &&
                s.DateTimeUtc.AddHours(1) > sessionStart);
            if (canAttend)
            {
                canAttendPlayers.Add(player);
                _logger.LogDebug("Игрок {PlayerId} может присутствовать на сессии", player.TelegramId);
            }
            else
            {
                cannotAttendPlayers.Add(player);
                _logger.LogDebug("Игрок {PlayerId} НЕ может присутствовать на сессии", player.TelegramId);
            }
        }
        var canAttendCount = canAttendPlayers.Count;
        var attendanceRate = totalPlayers > 0 ? (double)canAttendCount / totalPlayers : 0;
        const double Threshold = 0.75;
        _logger.LogInformation("Сессия {GroupName}: могут присутствовать {CanAttend}/{Total} ({Rate:P1})",
            group.Name, canAttendCount, totalPlayers, attendanceRate);
        // Получаем локальное время для уведомлений
        var admin = await UserService.GetPlayerAsync(AdminId, ct);
        var localStart = ConvertUtcToLocal(sessionStart, admin?.TimeZoneOffset ?? 0);
        if (attendanceRate >= Threshold)
        {
            // 🔹 75%+ могут — сессия остаётся в силе
            group.SessionStatus = SessionStatus.Confirmed;
            await Db.SaveChangesAsync(ct);
            // 🔹 Уведомление в общий чат
            var attendanceText = cannotAttendPlayers.Any()
                ? $"⚠️ **Не смогут присутствовать:** {string.Join(", ", cannotAttendPlayers.Select(p => p.GetMarkdownUsername()))}"
                : "✅ Все игроки могут присутствовать!";
            await NotifyMainChatAsync(
                $"📅 **Сессия остаётся в силе!**" +
                $"👥 Группа: **{group.Name}**" +
                $"📅 Дата: **{localStart:dd.MM (ddd) HH:mm}** (МСК)" +
                $"✅ Могут присутствовать: {canAttendCount}/{totalPlayers} ({attendanceRate:P0})" +
                $"🎯 Требуется: {Threshold:P0}" +
                $"{attendanceText}" +
                $"Время игры не изменилось!",
                ct);
            // 🔹 Персональное уведомление тем, кто не сможет
            foreach (var player in cannotAttendPlayers)
            {
                await SendTextAsync(
                    player.TelegramId,
                    $"⚠️ **Внимание!**" +
                    $"Вы обновили расписание и больше не можете присутствовать на сессии группы **{group.Name}**." +
                    $"📅 Дата: **{localStart:dd.MM (ddd) HH:mm}** (МСК)" +
                    $"✅ Однако сессия остаётся в силе, так как набралось достаточно игроков ({attendanceRate:P0})." +
                    $"Если вы всё же планируете быть — пожалуйста, обновите своё расписание.",
                    ct: ct);
            }
            _logger.LogInformation("✅ Сессия {GroupName} подтверждена ({Rate:P1}). Не смогут: {CannotAttendCount}",
                group.Name, attendanceRate, cannotAttendPlayers.Count);
        }
        else
        {
            // 🔹 Меньше 75% — отменяем сессию и запускаем новое планирование
            group.SessionStatus = SessionStatus.Cancelled;
            group.CurrentSessionUtc = null;
            group.ConfirmedPlayerIds.Clear();
            group.DeclinedPlayerIds.Clear();
            group.FinishedVotingPlayerIds.Clear(); // 🔹 Сбрасываем голосование для нового сбора
            await Db.SaveChangesAsync(ct);
            // 🔹 Уведомление об отмене
            await NotifyMainChatAsync(
                $"⚠️ **Требуется новое планирование!**" +
                $"👥 Группа: **{group.Name}**" +
                $"❌ Могут присутствовать: {canAttendCount}/{totalPlayers} ({attendanceRate:P0})" +
                $"🎯 Требуется: {Threshold:P0}" +
                $"❌ **Не смогут:** {string.Join(", ", cannotAttendPlayers.Select(p => p.GetMarkdownUsername()))}" +
                $"Запускаю поиск нового времени...",
                ct);
            _logger.LogInformation("⚠️ Сессия {GroupName} отменена ({Rate:P1}). Запуск авто-планирования...",
                group.Name, attendanceRate);
            // 🔹 Запускаем авто-планирование заново
            await AutoRunPlanningForGroupAsync(group, ct);
        }
    }

    #endregion
}