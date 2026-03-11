using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TgDataPlanner.Common;
using TgDataPlanner.Services;

namespace TgDataPlanner.Telegram.Handlers;

/// <summary>
/// Обработчики команд для планирования игровых сессий.
/// Интегрирует сервисы планирования и рекомендаций.
/// </summary>
public class SessionHandlers
{
    private readonly ITelegramBotClient _botClient;
    private readonly SessionPlanningService _planningService;
    private readonly Dictionary<long, List<TimeSlot>> _tempAvailability;

    public SessionHandlers(
        ITelegramBotClient botClient,
        SessionPlanningService planningService)
    {
        _botClient = botClient;
        _planningService = planningService;
        _tempAvailability = new Dictionary<long, List<TimeSlot>>();
    }

    /// <summary>
    /// Обработчик команды /request - создание сессии планирования (Админ)
    /// </summary>
    public async Task HandleRequestCommand(Message message, string[] args)
    {
        var chatId = message.Chat.Id;
        var adminId = message.From.Id;

        // Парсим длительность сессии (по умолчанию 4 часа)
        double sessionDuration = 4;
        if (args.Length > 0 && double.TryParse(args[0], out var parsedDuration))
        {
            sessionDuration = parsedDuration;
        }

        // Парсим список участников (ID или @username)
        var participantIds = new List<long>();
        if (args.Length > 1)
        {
            for (int i = 1; i < args.Length; i++)
            {
                if (long.TryParse(args[i], out var id))
                {
                    participantIds.Add(id);
                }
                // TODO: Добавить поддержку @username
            }
        }

        // Если участники не указаны, добавляем всех из чата (требуется реализация)
        if (participantIds.Count == 0)
        {
            participantIds.Add(adminId);
            // TODO: Получить список участников чата
        }

        // Создаем сессию планирования
        var session = _planningService.CreatePlanningSession(
            chatId,
            adminId,
            participantIds,
            sessionDuration);

        var responseText = new StringBuilder();
        responseText.AppendLine("🎮 **Сессия планирования создана**");
        responseText.AppendLine();
        responseText.AppendLine($"📋 ID сессии: `{session.SessionId}`");
        responseText.AppendLine($"⏱ Длительность: {session.SessionDurationHours} ч.");
        responseText.AppendLine($"👥 Участники: {session.TotalPlayersCount}");
        responseText.AppendLine();
        responseText.AppendLine("Каждый игрок должен отправить команду /free со своей доступностью");
        responseText.AppendLine();
        responseText.AppendLine("Пример: `/free 10:00-14:00, 18:00-23:00`");

        await _botClient.SendMessage(
            chatId,
            responseText.ToString(),
            ParseMode.Markdown,
            cancellationToken: CancellationToken.None);

        // Уведомляем участников
        await NotifyParticipants(session);
    }

    /// <summary>
    /// Обработчик команды /free - игрок указывает свою доступность
    /// </summary>
    public async Task HandleFreeCommand(Message message, string[] args)
    {
        var chatId = message.Chat.Id;
        var playerId = message.From.Id;
        var playerName = message.From.FirstName;

        // Получаем активную сессию для чата
        var session = _planningService.GetActiveSessionForChat(chatId);

        if (session == null)
        {
            await _botClient.SendMessage(
                chatId,
                "❌ Активная сессия планирования не найдена. Админ должен создать запрос командой /request",
                ParseMode.Markdown,
                cancellationToken: CancellationToken.None);
            return;
        }

        // Парсим доступность из аргументов
        var availableSlots = ParseAvailability(args);

        if (availableSlots.Count == 0)
        {
            await _botClient.SendMessage(
                chatId,
                "❌ Неверный формат доступности. Пример: `/free 10:00-14:00, 18:00-23:00`",
                ParseMode.Markdown,
                cancellationToken: CancellationToken.None);
            return;
        }

        // Определяем предпочтительное время (первый слот)
        var preferredStart = availableSlots.FirstOrDefault()?.Start;
        var preferredEnd = availableSlots.FirstOrDefault()?.End;

        // Обновляем доступность игрока
        var result = _planningService.UpdatePlayerAvailability(
            session.SessionId,
            playerId,
            availableSlots,
            preferredStart,
            preferredEnd);

        var responseText = new StringBuilder();
        responseText.AppendLine($"✅ **{playerName}**, ваша доступность обновлена!");
        responseText.AppendLine();
        responseText.AppendLine($"📅 Заполнено: {session.PlayersWithAvailabilityCount}/{session.TotalPlayersCount}");
        responseText.AppendLine();

        if (result.Status == PlanningStatus.WaitingForConfirmation)
        {
            responseText.AppendLine($"🎉 **Найдено время для всех!**");
            responseText.AppendLine();
            responseText.AppendLine($"🕐 {result.ScheduledStartTime:dd.MM.yyyy HH:mm} - {result.ScheduledEndTime:dd.MM.yyyy HH:mm}");
            responseText.AppendLine();
            responseText.AppendLine("Требуется подтверждение от 75% участников.");
            responseText.AppendLine();
            responseText.AppendLine("Нажмите кнопку ниже для подтверждения:");

            var keyboard = CreateConfirmationKeyboard(session.SessionId);

            await _botClient.SendMessage(
                chatId,
                responseText.ToString(),
                ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: CancellationToken.None);
        }
        else if (result.Status == PlanningStatus.RecommendationsSent)
        {
            responseText.AppendLine("⚠️ **Прямое пересечение не найдено**");
            responseText.AppendLine();
            responseText.AppendLine("Сервис рекомендаций подготовил варианты. Ожидайте решения админа.");
            responseText.AppendLine();

            await _botClient.SendMessage(
                chatId,
                responseText.ToString(),
                ParseMode.Markdown,
                cancellationToken: CancellationToken.None);

            // Отправляем рекомендации админу
            await SendRecommendationsToAdmin(session);
        }
        else
        {
            responseText.AppendLine("Ожидаем заполнения доступности остальными игроками...");

            await _botClient.SendMessage(
                chatId,
                responseText.ToString(),
                ParseMode.Markdown,
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Обработчик команды /status - проверка статуса планирования
    /// </summary>
    public async Task HandleStatusCommand(Message message)
    {
        var chatId = message.Chat.Id;
        var playerId = message.From.Id;

        var session = _planningService.GetActiveSessionForChat(chatId);

        if (session == null)
        {
            await _botClient.SendMessage(
                chatId,
                "❌ Активная сессия планирования не найдена",
                ParseMode.Markdown,
                cancellationToken: CancellationToken.None);
            return;
        }

        var responseText = BuildStatusMessage(session, playerId);

        await _botClient.SendMessage(
            chatId,
            responseText,
            ParseMode.Markdown,
            cancellationToken: CancellationToken.None);
    }

    /// <summary>
    /// Обработчик команды /recommendations - ручной запуск рекомендаций (Админ)
    /// </summary>
    public async Task HandleRecommendationsCommand(Message message)
    {
        var chatId = message.Chat.Id;
        var playerId = message.From.Id;

        var session = _planningService.GetActiveSessionForChat(chatId);

        if (session == null)
        {
            await _botClient.SendMessage(
                chatId,
                "❌ Активная сессия планирования не найдена",
                ParseMode.Markdown,
                cancellationToken: CancellationToken.None);
            return;
        }

        var result = _planningService.RequestRecommendations(session.SessionId, playerId);

        if (result.Success && result.Status == PlanningStatus.RecommendationsSent)
        {
            await SendRecommendationsToAdmin(session);
        }
        else
        {
            await _botClient.SendMessage(
                chatId,
                $"❌ {result.Message}",
                ParseMode.Markdown,
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Обработчик callback для подтверждения времени
    /// </summary>
    public async Task HandleConfirmationCallback(CallbackQuery callbackQuery, long sessionId)
    {
        var playerId = callbackQuery.From.Id;
        var chatId = callbackQuery.Message.Chat.Id;

        var result = _planningService.ConfirmTime(sessionId, playerId);

        string responseText;

        if (result.Success && result.Status == PlanningStatus.Scheduled)
        {
            responseText = $"✅ **Сессия запланирована!**\n\n";
            responseText += $"🕐 {result.ScheduledStartTime:dd.MM.yyyy HH:mm} - {result.ScheduledEndTime:dd.MM.yyyy HH:mm}\n\n";
            responseText += $"👥 Подтвердили: {result.Message}";

            await _botClient.EditMessageText(
                chatId,
                callbackQuery.Message.MessageId,
                responseText,
                ParseMode.Markdown,
                cancellationToken: CancellationToken.None);

            // Уведомляем всех о успешном планировании
            await NotifySessionScheduled(chatId, result.ScheduledStartTime.Value, result.ScheduledEndTime.Value);
        }
        else if (result.Success)
        {
            responseText = $"✅ {result.Message}\n\n";
            responseText += "Ожидаем подтверждения от остальных участников...";

            await _botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                "Подтверждение принято!",
                cancellationToken: CancellationToken.None);

            await _botClient.EditMessageText(
                chatId,
                callbackQuery.Message.MessageId,
                responseText,
                ParseMode.Markdown,
                replyMarkup: CreateConfirmationKeyboard(sessionId),
                cancellationToken: CancellationToken.None);
        }
        else
        {
            await _botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                result.Message,
                showAlert: true,
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Обработчик callback для выбора варианта рекомендации
    /// </summary>
    public async Task HandleRecommendationCallback(CallbackQuery callbackQuery, long sessionId, int optionIndex)
    {
        var playerId = callbackQuery.From.Id;
        var chatId = callbackQuery.Message.Chat.Id;

        var result = _planningService.SelectRecommendation(sessionId, playerId, optionIndex);

        if (result.Success)
        {
            var responseText = $"📋 **Выбран вариант #{optionIndex + 1}**\n\n";
            responseText += $"🕐 {result.ScheduledStartTime:dd.MM.yyyy HH:mm} - {result.ScheduledEndTime:dd.MM.yyyy HH:mm}\n\n";
            responseText += "Требуется подтверждение от 75% участников.";
            responseText += "\n";
            responseText += "Нажмите кнопку ниже для подтверждения:";

            var keyboard = CreateConfirmationKeyboard(sessionId);

            await _botClient.EditMessageText(
                chatId,
                callbackQuery.Message.MessageId,
                responseText,
                ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: CancellationToken.None);

            await _botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                "Вариант выбран! Ожидаем подтверждений.",
                cancellationToken: CancellationToken.None);
        }
        else
        {
            await _botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                $"❌ {result.Message}",
                showAlert: true,
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Обработчик команды /cancel - отмена сессии планирования (Админ)
    /// </summary>
    public async Task HandleCancelCommand(Message message)
    {
        var chatId = message.Chat.Id;
        var playerId = message.From.Id;

        var session = _planningService.GetActiveSessionForChat(chatId);

        if (session == null)
        {
            await _botClient.SendMessage(
                chatId,
                "❌ Активная сессия планирования не найдена",
                ParseMode.Markdown,
                cancellationToken: CancellationToken.None);
            return;
        }

        var result = _planningService.CancelSession(session.SessionId, playerId);

        await _botClient.SendMessage(
            chatId,
            result.Success ? "✅ Сессия планирования отменена" : $"❌ {result.Message}",
            ParseMode.Markdown,
            cancellationToken: CancellationToken.None);
    }

    /// <summary>
    /// Парсит строку доступности в список временных слотов
    /// </summary>
    private List<TimeSlot> ParseAvailability(string[] args)
    {
        var slots = new List<TimeSlot>();

        if (args.Length == 0)
        {
            return slots;
        }

        // Объединяем все аргументы в одну строку
        var availabilityString = string.Join(" ", args);

        // Разделяем по запятой
        var slotStrings = availabilityString.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var slotString in slotStrings)
        {
            // Парсим формат "HH:MM-HH:MM"
            var timeParts = slotString.Trim().Split('-');

            if (timeParts.Length == 2 &&
                TryParseTime(timeParts[0].Trim(), out var startTime) &&
                TryParseTime(timeParts[1].Trim(), out var endTime))
            {
                // Если конец раньше начала, считаем что это следующий день
                if (endTime < startTime)
                {
                    endTime = endTime.AddDays(1);
                }

                slots.Add(new TimeSlot
                {
                    Start = startTime,
                    End = endTime
                });
            }
        }

        return slots;
    }

    /// <summary>
    /// Парсит время из строки (поддерживает форматы HH:MM, H:MM, HHMM)
    /// </summary>
    private bool TryParseTime(string timeString, out DateTime result)
    {
        result = DateTime.MinValue;

        try
        {
            // Формат HH:MM
            if (timeString.Contains(":"))
            {
                var parts = timeString.Split(':');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var hours) &&
                    int.TryParse(parts[1], out var minutes))
                {
                    result = DateTime.Today.AddHours(hours).AddMinutes(minutes);
                    return true;
                }
            }
            // Формат HHMM
            else if (timeString.Length == 4 && int.TryParse(timeString, out var timeValue))
            {
                var hours = timeValue / 100;
                var minutes = timeValue % 100;
                result = DateTime.Today.AddHours(hours).AddMinutes(minutes);
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Создает клавиатуру с кнопкой подтверждения
    /// </summary>
    private InlineKeyboardMarkup CreateConfirmationKeyboard(long sessionId)
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "✅ Подтвердить участие",
                    $"confirm_{sessionId}")
            }
        });
    }

    /// <summary>
    /// Создает клавиатуру с вариантами рекомендаций
    /// </summary>
    private InlineKeyboardMarkup CreateRecommendationsKeyboard(long sessionId, List<RecommendationOption> options)
    {
        var buttons = new List<InlineKeyboardButton>();

        for (int i = 0; i < Math.Min(options.Count, 5); i++)
        {
            var option = options[i];
            var buttonText = $"#{i + 1} {option.ProposedStartTime:dd.MM HH:mm} ({option.GetPriorityDescription()})";

            buttons.Add(InlineKeyboardButton.WithCallbackData(
                buttonText,
                $"recommend_{sessionId}_{i}"));
        }

        return new InlineKeyboardMarkup(buttons.Select(b => new[] { b }));
    }

    /// <summary>
    /// Отправляет рекомендации администратору
    /// </summary>
    private async Task SendRecommendationsToAdmin(PlanningSession session)
    {
        if (session.RecommendationResult == null || !session.RecommendationResult.HasRecommendations)
        {
            return;
        }

        var responseText = new StringBuilder();
        responseText.AppendLine("📊 **Варианты рекомендаций**");
        responseText.AppendLine();
        responseText.AppendLine($"Всего найдено: {session.RecommendationResult.OptionsCount} вариантов");
        responseText.AppendLine();

        // Показываем топ-5 вариантов
        var topOptions = session.RecommendationResult.Options.Take(5).ToList();

        for (int i = 0; i < topOptions.Count; i++)
        {
            var option = topOptions[i];
            responseText.AppendLine($"**#{i + 1}.** {option.ProposedStartTime:dd.MM.yyyy HH:mm} - {option.ProposedEndTime:dd.MM.yyyy HH:mm}");
            responseText.AppendLine($"   └─ {option.GetPriorityDescription()}");
            responseText.AppendLine($"   └─ Участников: {option.AttendingPlayersCount}/{option.TotalPlayersCount} ({option.AttendancePercentage:F0}%)");

            if (option.ExcludedPlayerIds.Count > 0)
            {
                responseText.AppendLine($"   └─ Не смогут: {string.Join(", ", option.ExcludedPlayerIds)}");
            }

            responseText.AppendLine();
        }

        responseText.AppendLine("Выберите вариант для голосования:");

        var keyboard = CreateRecommendationsKeyboard(session.SessionId, topOptions);

        await _botClient.SendMessage(
            session.ChatId,
            responseText.ToString(),
            ParseMode.Markdown,
            replyMarkup: keyboard,
            cancellationToken: CancellationToken.None);
    }

    /// <summary>
    /// Уведомляет участников о создании сессии планирования
    /// </summary>
    private async Task NotifyParticipants(PlanningSession session)
    {
        var messageText = new StringBuilder();
        messageText.AppendLine("🎮 **Новая сессия планирования!**");
        messageText.AppendLine();
        messageText.AppendLine($"Администратор создал запрос на планирование.");
        messageText.AppendLine($"⏱ Длительность: {session.SessionDurationHours} ч.");
        messageText.AppendLine();
        messageText.AppendLine("Отправьте команду /free со своей доступностью:");
        messageText.AppendLine("Пример: `/free 10:00-14:00, 18:00-23:00`");

        foreach (var participant in session.Participants)
        {
            try
            {
                await _botClient.SendMessage(
                    participant.PlayerId,
                    messageText.ToString(),
                    ParseMode.Markdown,
                    cancellationToken: CancellationToken.None);
            }
            catch
            {
                // Игрок мог заблокировать бота
            }
        }
    }

    /// <summary>
    /// Уведомляет всех о успешном планировании сессии
    /// </summary>
    private async Task NotifySessionScheduled(long chatId, DateTime startTime, DateTime endTime)
    {
        var messageText = new StringBuilder();
        messageText.AppendLine("🎉 **Сессия успешно запланирована!**");
        messageText.AppendLine();
        messageText.AppendLine($"🕐 {startTime:dd.MM.yyyy HH:mm} - {endTime:dd.MM.yyyy HH:mm}");
        messageText.AppendLine();
        messageText.AppendLine("Не забудьте подготовиться к игре!");

        await _botClient.SendMessage(
            chatId,
            messageText.ToString(),
            ParseMode.Markdown,
            cancellationToken: CancellationToken.None);
    }

    /// <summary>
    /// Строит сообщение со статусом сессии
    /// </summary>
    private string BuildStatusMessage(PlanningSession session, long playerId)
    {
        var responseText = new StringBuilder();
        responseText.AppendLine("📋 **Статус планирования**");
        responseText.AppendLine();
        responseText.AppendLine($"ID сессии: `{session.SessionId}`");
        responseText.AppendLine($"Статус: {GetStatusEmoji(session.Status)} {session.Status}");
        responseText.AppendLine($"Длительность: {session.SessionDurationHours} ч.");
        responseText.AppendLine();
        responseText.AppendLine($"👥 Участники: {session.PlayersWithAvailabilityCount}/{session.TotalPlayersCount} заполнили доступность");
        responseText.AppendLine();

        if (session.Status == PlanningStatus.WaitingForConfirmation ||
            session.Status == PlanningStatus.RecommendationsSent)
        {
            responseText.AppendLine($"🕐 Предлагаемое время:");
            responseText.AppendLine($"   {session.ScheduledStartTime:dd.MM.yyyy HH:mm} - {session.ScheduledEndTime:dd.MM.yyyy HH:mm}");
            responseText.AppendLine();
            responseText.AppendLine($"✅ Подтверждения: {session.ConfirmationCount}/{session.TotalPlayersCount} ({session.ConfirmationPercentage:F0}%)");
            responseText.AppendLine($"   Требуется: {Math.Ceiling(session.TotalPlayersCount * 0.75)} ({75}%)");
            responseText.AppendLine();

            var participant = session.Participants.FirstOrDefault(p => p.PlayerId == playerId);
            if (participant != null)
            {
                responseText.AppendLine($"Ваш статус: {(participant.HasConfirmed ? "✅ Подтвердили" : "⏳ Ожидается подтверждение")}");
            }
        }
        else if (session.Status == PlanningStatus.Scheduled)
        {
            responseText.AppendLine($"🎉 Сессия запланирована на:");
            responseText.AppendLine($"   {session.ScheduledStartTime:dd.MM.yyyy HH:mm} - {session.ScheduledEndTime:dd.MM.yyyy HH:mm}");
        }

        return responseText.ToString();
    }

    /// <summary>
    /// Возвращает emoji для статуса
    /// </summary>
    private string GetStatusEmoji(PlanningStatus status)
    {
        return status switch
        {
            PlanningStatus.WaitingForAvailability => "⏳",
            PlanningStatus.WaitingForConfirmation => "✅",
            PlanningStatus.RecommendationsSent => "📊",
            PlanningStatus.Scheduled => "🎉",
            PlanningStatus.Cancelled => "❌",
            _ => "❓"
        };
    }
}
