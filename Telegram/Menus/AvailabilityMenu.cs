using Telegram.Bot.Types.ReplyMarkups;
using TgDataPlanner.Data.Entities;

namespace TgDataPlanner.Telegram.Menus;

public static class AvailabilityMenu
{
    // Генерация календаря на 14 дней
    public static InlineKeyboardMarkup GetDateCalendar(int timeZoneOffset)
    {
        var buttons = new List<InlineKeyboardButton[]>();
        var startDate = DateTime.UtcNow.AddHours(timeZoneOffset).AddDays(1); // Начинаем со следующего дня

        for (int i = 0; i < 14; i++)
        {
            var date = startDate.AddDays(i);
            var dateStr = date.ToString("dd.MM (ddd)");
            var callbackData = $"pick_date_{date:yyyy-MM-dd}";
            
            buttons.Add(new[] { InlineKeyboardButton.WithCallbackData(dateStr, callbackData) });
        }

        return new InlineKeyboardMarkup(buttons);
    }

    // Генерация сетки часов 12:00 - 00:00
    public static InlineKeyboardMarkup GetTimeGrid(DateTime date, List<AvailabilitySlot> activeSlots, int offset)
    {
        var buttons = new List<InlineKeyboardButton[]>();
        var row = new List<InlineKeyboardButton>();

        for (int hour = 12; hour <= 24; hour++)
        {
            // Проверяем, выбран ли этот час уже в БД (с учетом UTC)
            var slotTimeUtc = new DateTime(date.Year, date.Month, date.Day, hour % 24, 0, 0).AddHours(-offset);
            bool isSelected = activeSlots.Any(s => s.DateTimeUtc == slotTimeUtc);

            var label = $"{(isSelected ? "✅" : "⬜️")} {hour}:00";
            var callbackData = $"toggle_time_{date:yyyy-MM-dd}_{hour}";
            
            row.Add(InlineKeyboardButton.WithCallbackData(label, callbackData));

            if (row.Count == 3) // По 3 кнопки в ряд
            {
                buttons.Add(row.ToArray());
                row.Clear();
            }
        }
        
        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад к датам", "back_to_dates") });
        return new InlineKeyboardMarkup(buttons);
    }
}
