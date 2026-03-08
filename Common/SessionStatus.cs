namespace TgDataPlanner.Common;

public enum SessionStatus
{
    Pending,      // Ожидание ответов игроков
    Confirmed,    // 75%+ подтвердили — сессия назначена
    Cancelled     // Мало игроков — требуется новый сбор
}