namespace TgDataPlanner.Common;

public enum SessionStatus
{
    Pending,      // Ожидание ответов игроков
    Confirmed,    // 75%+ подтвердили и ВСЕ админы могут присутствовать — сессия назначена
    Cancelled,    // Мало игроков — требуется новый сбор
    Rescheduled   // Администраторы не могут присутствовать — требуется перепланирование
}
