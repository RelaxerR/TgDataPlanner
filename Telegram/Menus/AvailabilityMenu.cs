using Telegram.Bot.Types.ReplyMarkups;
using TgDataPlanner.Data.Entities;

namespace TgDataPlanner.Telegram.Menus;

/// <summary>
/// Статический класс для генерации inline-клавиатур, связанных с выбором доступного времени.
/// Предоставляет методы для создания календаря дат и сетки часов с учётом часового пояса пользователя.
/// </summary>
public static class AvailabilityMenu
{
    /// <summary>
    /// Константы конфигурации интерфейса.
    /// </summary>
    private static class Config
    {
        public const int CalendarDaysCount = 14;          // Количество дней в календаре
        public const int TimeGridStartHour = 12;          // Начальный час сетки (включительно)
        public const int TimeGridEndHour = 24;            // Конечный час сетки (включительно)
        public const int ButtonsPerRow = 3;               // Количество кнопок в ряду сетки времени
        public const string FinishVotingCallback = "finish_voting";
        public const string BackToDatesCallback = "back_to_dates";
    }

    /// <summary>
    /// Форматы дат для отображения и передачи в callback.
    /// </summary>
    private static class DateFormats
    {
        public const string DisplayFormat = "dd.MM (ddd)";      // Для текста кнопки: "15.03 (Сб)"
        public const string CallbackFormat = "yyyy-MM-dd";      // Для callback_data: "2024-03-15"
    }

    /// <summary>
    /// Генерирует inline-клавиатуру с календарём на 14 дней для выбора даты.
    /// </summary>
    /// <param name="timeZoneOffset">Смещение часового пояса пользователя относительно UTC.</param>
    /// <returns>Объект <see cref="InlineKeyboardMarkup"/> с кнопками дат.</returns>
    public static InlineKeyboardMarkup GetDateCalendar(int timeZoneOffset)
    {
        var buttons = new List<InlineKeyboardButton[]>();
        var startDate = GetLocalizedToday(timeZoneOffset).AddDays(1);

        for (var i = 0; i < Config.CalendarDaysCount; i++)
        {
            var date = startDate.AddDays(i);
            var button = CreateDateButton(date);
            buttons.Add([button]);
        }

        // Добавляем кнопку завершения отдельным рядом
        buttons.Add([CreateFinishVotingButton()]);

        return new InlineKeyboardMarkup(buttons);
    }

    /// <summary>
    /// Генерирует inline-клавиатуру с сеткой часов для выбора свободного времени.
    /// </summary>
    /// <param name="date">Дата, для которой генерируется сетка (в локальном времени пользователя).</param>
    /// <param name="activeSlots">Список уже выбранных слотов доступности пользователя.</param>
    /// <param name="timeZoneOffset">Смещение часового пояса пользователя относительно UTC.</param>
    /// <returns>Объект <see cref="InlineKeyboardMarkup"/> с кнопками часов.</returns>
    public static InlineKeyboardMarkup GetTimeGrid(
        DateTime date,
        List<AvailabilitySlot> activeSlots,
        int timeZoneOffset)
    {
        var buttons = new List<InlineKeyboardButton[]>();
        var currentRow = new List<InlineKeyboardButton>();

        for (var hour = Config.TimeGridStartHour; hour <= Config.TimeGridEndHour; hour++)
        {
            var button = CreateTimeButton(date, hour, activeSlots, timeZoneOffset);
            currentRow.Add(button);

            // Перенос на новую строку после достижения лимита кнопок
            if (currentRow.Count == Config.ButtonsPerRow)
            {
                buttons.Add([.. currentRow]);
                currentRow.Clear();
            }
        }

        // Добавляем оставшиеся кнопки в последнем ряду
        if (currentRow.Any())
        {
            buttons.Add([.. currentRow]);
        }

        // Добавляем кнопку "Назад" отдельным рядом
        buttons.Add([CreateBackToDatesButton()]);

        return new InlineKeyboardMarkup(buttons);
    }

    /// <summary>
    /// Создаёт кнопку для выбора даты в календаре.
    /// </summary>
    /// <param name="date">Дата для кнопки.</param>
    /// <returns>Объект <see cref="InlineKeyboardButton"/>.</returns>
    private static InlineKeyboardButton CreateDateButton(DateTime date)
    {
        var displayText = date.ToString(DateFormats.DisplayFormat);
        var callbackData = $"{CallbackPrefixes.PickDate}{date:yyyy-MM-dd}";

        return InlineKeyboardButton.WithCallbackData(displayText, callbackData);
    }

    /// <summary>
    /// Создаёт кнопку для переключения доступности в конкретный час.
    /// </summary>
    /// <param name="date">Дата слота (в локальном времени).</param>
    /// <param name="hour">Час слота (0-23).</param>
    /// <param name="activeSlots">Список активных слотов пользователя.</param>
    /// <param name="timeZoneOffset">Смещение часового пояса пользователя.</param>
    /// <returns>Объект <see cref="InlineKeyboardButton"/> с индикатором выбора.</returns>
    private static InlineKeyboardButton CreateTimeButton(
        DateTime date,
        int hour,
        List<AvailabilitySlot> activeSlots,
        int timeZoneOffset)
    {
        var slotTimeUtc = ConvertToLocalToUtc(date, hour, timeZoneOffset);
        var isSelected = activeSlots.Any(s => s.DateTimeUtc == slotTimeUtc);

        var displayText = $"{(isSelected ? "✅" : "⬜️")} {hour:D2}:00";
        var callbackData = $"{CallbackPrefixes.ToggleTime}{date:yyyy-MM-dd}_{hour}";

        return InlineKeyboardButton.WithCallbackData(displayText, callbackData);
    }

    /// <summary>
    /// Создаёт кнопку завершения заполнения расписания.
    /// </summary>
    private static InlineKeyboardButton CreateFinishVotingButton() =>
        InlineKeyboardButton.WithCallbackData("✅ ЗАВЕРШИТЬ ЗАПОЛНЕНИЕ", Config.FinishVotingCallback);

    /// <summary>
    /// Создаёт кнопку возврата к выбору дат.
    /// </summary>
    private static InlineKeyboardButton CreateBackToDatesButton() =>
        InlineKeyboardButton.WithCallbackData("⬅️ Назад к датам", Config.BackToDatesCallback);

    /// <summary>
    /// Преобразует локальное время пользователя в UTC для хранения в БД.
    /// </summary>
    /// <param name="localDate">Дата в локальном времени пользователя.</param>
    /// <param name="hour">Час в локальном времени (0-23).</param>
    /// <param name="timeZoneOffset">Смещение часового пояса пользователя.</param>
    /// <returns>Время слота в формате UTC.</returns>
    private static DateTime ConvertToLocalToUtc(DateTime localDate, int hour, int timeZoneOffset) =>
        new DateTime(localDate.Year, localDate.Month, localDate.Day, hour % 24, 0, 0, DateTimeKind.Unspecified)
            .AddHours(-timeZoneOffset);

    /// <summary>
    /// Получает текущую дату с учётом часового пояса пользователя.
    /// </summary>
    /// <param name="timeZoneOffset">Смещение часового пояса.</param>
    /// <returns>Локализованная дата без времени.</returns>
    private static DateTime GetLocalizedToday(int timeZoneOffset) =>
        DateTime.UtcNow.AddHours(timeZoneOffset).Date;

    /// <summary>
    /// Префиксы для callback-данных, используемых в меню.
    /// </summary>
    /// <remarks>
    /// Вынесены в отдельный класс для устранения магических строк и централизованного управления.
    /// </remarks>
    private static class CallbackPrefixes
    {
        public const string PickDate = "pick_date_";
        public const string ToggleTime = "toggle_time_";
    }
}