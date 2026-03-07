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
/// Обработчик нажатий на inline-кнопки (CallbackQuery).
/// Маршрутизирует колбэки к соответствующим методам и управляет взаимодействием с пользователем.
/// </summary>
public class CallbackHandler : BaseHandler
{
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
    /// <param name="schedulingService">Сервис планирования.</param>
    public CallbackHandler(
        IConfiguration config,
        ITelegramBotClient botClient,
        ILogger<CallbackHandler> logger,
        AppDbContext db,
        UserService userService,
        SchedulingService schedulingService)
        : base(config, botClient, logger, db, userService, schedulingService)
    {
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

            default:
                _logger.LogWarning("Неизвестный callback-запрос: {Data}", data);
                await AnswerCallbackAsync(callbackQuery, ct: ct);
                break;
        }
    }

    #region Обработчики колбэков

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
        await SetPlayerStateAsync(userId, null, ct);

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
        // Получаем игрока с группами для отображения статистики
        var player = await Db.Players
            .Include(p => p.Groups)
            .FirstOrDefaultAsync(p => p.TelegramId == userId, ct);

        if (player is null)
        {
            await AnswerCallbackAsync(callbackQuery, ct: ct);
            return;
        }

        var groupNames = player.Groups.Any()
            ? string.Join(", ", player.Groups.Select(g => g.Name))
            : "Без группы";

        await EditTextAsync(callbackQuery, "✅ Данные сохранены!", ct: ct);

        await NotifyMainChatAsync(
            $"🔔 **{player.Username}** [{groupNames}] завершил заполнение расписания!",
            ct);

        _logger.LogInformation(
            "Пользователь {UserId} [@{Username}] завершил заполнение расписания для групп: {Groups}",
            userId, player.Username, groupNames);

        await AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Обрабатывает вступление пользователя в группу.
    /// </summary>
    private async Task HandleJoinGroupAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
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
            await EditTextAsync(callbackQuery, $"⚔️ Вы вступили в группу **{addedGroup.Name}**!", ct: ct);
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
            .AsNoTracking()
            .Include(p => p.Slots)
            .FirstOrDefaultAsync(p => p.TelegramId == telegramId, ct);

    /// <summary>
    /// Получает группу по идентификатору.
    /// </summary>
    private async Task<Group?> GetGroupAsync(int groupId, CancellationToken ct) =>
        await Db.Groups.FindAsync([groupId], ct);

    /// <summary>
    /// Получает список всех групп.
    /// </summary>
    private async Task<List<Group>> GetAllGroupsAsync(CancellationToken ct) =>
        await Db.Groups.ToListAsync(ct);

    #endregion
}