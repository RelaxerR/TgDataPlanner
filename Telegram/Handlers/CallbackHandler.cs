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
    /// Инициализирует новый экземпляр <see cref="CallbackHandler"/>.
    /// </summary>
    public CallbackHandler(
        IConfiguration config,
        ITelegramBotClient botClient,
        ILogger<CallbackHandler> logger,
        AppDbContext db,
        SchedulingService schedulingService)
        : base(config, botClient, logger, db, schedulingService)
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
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Ошибка при обработке callback '{Data}' от пользователя {UserId}",
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

            default:
                _logger.LogWarning("Неизвестный callback-запрос: {Data}", data);
                await AnswerCallbackAsync(callbackQuery, ct: ct);
                break;
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
        var player = await GetPlayerAsync(userId, ct);
        if (player is not null)
        {
            player.CurrentState = null;
            await Db.SaveChangesAsync(ct);
            _logger.LogInformation("Сброшено состояние пользователя {UserId}", userId);
        }

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

        var player = await GetPlayerAsync(userId, ct);
        if (player is null)
        {
            await AnswerCallbackAsync(callbackQuery, "⚠️ Пользователь не найден", showAlert: true, ct);
            return;
        }

        player.TimeZoneOffset = offset;
        await Db.SaveChangesAsync(ct);

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

        var slotTimeUtc = new DateTime(date.Year, date.Month, date.Day, hour % 24, 0, 0)
            .AddHours(-player.TimeZoneOffset);

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

        await BotClient.EditMessageReplyMarkup(
            chatId: callbackQuery.Message!.Chat.Id,
            messageId: callbackQuery.Message.MessageId,
            replyMarkup: AvailabilityMenu.GetTimeGrid(date, player.Slots, player.TimeZoneOffset),
            cancellationToken: ct);

        await AnswerCallbackAsync(callbackQuery, ct: ct);
    }

    /// <summary>
    /// Обрабатывает возврат к выбору дат из выбора времени.
    /// </summary>
    private async Task HandleBackToDatesAsync(CallbackQuery callbackQuery, long userId, CancellationToken ct)
    {
        var player = await GetPlayerAsync(userId, ct);
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
        const int minDurationHours = 3; // TODO: вынести в конфигурацию или добавить параметр

        _logger.LogInformation("Запуск поиска окон для группы {GroupId}, мин. длительность: {Hours}ч", groupId, minDurationHours);

        var intersections = await SchedulingService.FindIntersectionsAsync(groupId, minDurationHours);

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

        foreach (var interval in intersections.Take(5))
        {
            var admin = await GetPlayerAsync(userId, ct);
            var localStart = interval.Start.AddHours(admin?.TimeZoneOffset ?? 0);
            var localEnd = interval.End.AddHours(admin?.TimeZoneOffset ?? 0);

            var timeStr = $"{localStart:dd.MM HH:mm} - {localEnd:HH:mm}";
            resultText += $"🔹 {timeStr}\n";

            buttons.Add([InlineKeyboardButton.WithCallbackData($"✅ {timeStr}", $"{CallbackPrefixes.ConfirmTime}{groupId}_{interval.Start:yyyyMMddHH}")]);
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

        var player = await GetPlayerWithGroupsAsync(userId, ct);
        var group = await Db.Groups.FindAsync([groupId], ct);

        if (group is null)
        {
            await AnswerCallbackAsync(callbackQuery, "⚠️ Группа не найдена", showAlert: true, ct);
            return;
        }

        if (player!.Groups.Any(g => g.Id == groupId))
        {
            await AnswerCallbackAsync(callbackQuery, "ℹ️ Вы уже состоите в этой группе", ct: ct);
            return;
        }

        player.Groups.Add(group);
        await Db.SaveChangesAsync(ct);

        _logger.LogInformation("Пользователь {UserId} вступил в группу {GroupId} [{GroupName}]", userId, groupId, group.Name);

        await EditTextAsync(callbackQuery, $"⚔️ Вы вступили в группу **{group.Name}**!", ct: ct);
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
        var group = await Db.Groups.FindAsync([groupId], ct);

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
    /// Получает игрока по Telegram ID.
    /// </summary>
    private async Task<Player?> GetPlayerAsync(long telegramId, CancellationToken ct) =>
        await Db.Players.FindAsync([telegramId], ct);

    /// <summary>
    /// Получает игрока с загруженными слотами доступности.
    /// </summary>
    private async Task<Player?> GetPlayerWithSlotsAsync(long telegramId, CancellationToken ct) =>
        await Db.Players
            .Include(p => p.Slots)
            .FirstOrDefaultAsync(p => p.TelegramId == telegramId, ct);

    /// <summary>
    /// Получает игрока с загруженными группами.
    /// </summary>
    private async Task<Player?> GetPlayerWithGroupsAsync(long telegramId, CancellationToken ct) =>
        await Db.Players
            .Include(p => p.Groups)
            .FirstOrDefaultAsync(p => p.TelegramId == telegramId, ct);
}