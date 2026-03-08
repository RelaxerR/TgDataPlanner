using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgDataPlanner.Common;
using TgDataPlanner.Data;
using TgDataPlanner.Data.Entities;

namespace TgDataPlanner.Services;

/// <summary>
/// Сервис для управления сущностями игроков (Players).
/// Инкапсулирует бизнес-логику создания, обновления и поиска пользователей.
/// </summary>
public class UserService
{
    private readonly AppDbContext _db;
    private readonly ILogger<UserService> _logger;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UserService"/>.
    /// </summary>
    /// <param name="db">Контекст базы данных.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    public UserService(
        AppDbContext db,
        ILogger<UserService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Получает игрока по идентификатору Telegram.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Объект игрока или null, если не найден.</returns>
    public async Task<Player?> GetPlayerAsync(long telegramId, CancellationToken ct = default) =>
        await _db.Players.FindAsync([telegramId], ct);

    /// <summary>
    /// Получает игрока с загруженными связанными данными (группы, слоты).
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Объект игрока с загруженными коллекциями или null.</returns>
    public async Task<Player?> GetPlayerWithRelationsAsync(long telegramId, CancellationToken ct = default) =>
        await _db.Players
            .Include(p => p.Groups)
            .Include(p => p.Slots)
            .FirstOrDefaultAsync(p => p.TelegramId == telegramId, ct);

    /// <summary>
    /// Получает существующего игрока или создаёт нового, если он не найден.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="username">Имя пользователя (опционально).</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Объект игрока (существующий или ново созданный).</returns>
    public async Task<Player> GetOrCreatePlayerAsync(
        long telegramId,
        string? username = null,
        CancellationToken ct = default)
    {
        var player = await GetPlayerAsync(telegramId, ct);

        if (player is null)
        {
            _logger.LogInformation("Создание нового игрока: TelegramId={TelegramId}, Username={Username}",
                telegramId, username ?? "Unknown");

            player = new Player(telegramId, username);
            _db.Players.Add(player);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            // Обновляем имя пользователя, если оно изменилось
            if (string.IsNullOrEmpty(username) || player.Username == username)
                return player;
            
            player.Username = username;
            await _db.SaveChangesAsync(ct);
            _logger.LogDebug("Обновлено имя пользователя {TelegramId}: {OldName} -> {NewName}",
                telegramId, player.Username, username);
        }

        return player;
    }

    /// <summary>
    /// Обновляет часовой пояс игрока.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="timeZoneOffset">Новое смещение часового пояса (UTC).</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>True, если обновление прошло успешно.</returns>
    public async Task<bool> UpdateTimeZoneAsync(
        long telegramId,
        int timeZoneOffset,
        CancellationToken ct = default)
    {
        var player = await GetPlayerAsync(telegramId, ct);
        if (player is null)
        {
            _logger.LogWarning("Попытка обновить часовой пояс для несуществующего игрока {TelegramId}", telegramId);
            return false;
        }

        var oldOffset = player.TimeZoneOffset;
        player.TimeZoneOffset = timeZoneOffset;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Часовой пояс игрока {TelegramId} изменён: UTC{OldOffset} -> UTC{NewOffset}",
            telegramId, oldOffset, timeZoneOffset);

        return true;
    }

    /// <summary>
    /// Устанавливает или сбрасывает состояние машины состояний игрока.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="state">Новое состояние (null для сброса).</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>True, если состояние обновлено успешно.</returns>
    public async Task<bool> SetPlayerStateAsync(
        long telegramId,
        PlayerState state,
        CancellationToken ct = default)
    {
        var player = await GetPlayerAsync(telegramId, ct);
        if (player is null)
            return false;

        player.CurrentState = state;

        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Состояние игрока {TelegramId} установлено в: {State}",
            telegramId, state.ToString());

        return true;
    }

    /// <summary>
    /// Добавляет игрока в группу.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="groupId">Идентификатор группы.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>True, если игрок успешно добавлен; false, если уже состоит в группе.</returns>
    public async Task<bool> AddPlayerToGroupAsync(
        long telegramId,
        int groupId,
        CancellationToken ct = default)
    {
        var player = await _db.Players
            .Include(p => p.Groups)
            .FirstOrDefaultAsync(p => p.TelegramId == telegramId, ct);

        if (player is null)
        {
            _logger.LogWarning("Игрок {TelegramId} не найден для добавления в группу {GroupId}", telegramId, groupId);
            return false;
        }

        if (player.IsMemberOfGroup(groupId))
        {
            _logger.LogDebug("Игрок {TelegramId} уже состоит в группе {GroupId}", telegramId, groupId);
            return false;
        }

        var group = await _db.Groups.FindAsync([groupId], ct);
        if (group is null)
        {
            _logger.LogWarning("Группа {GroupId} не найдена", groupId);
            return false;
        }

        player.Groups.Add(group);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Игрок {Username} ({TelegramId}) добавлен в группу '{GroupName}' ({GroupId})",
            player.Username, telegramId, group.Name, groupId);

        return true;
    }

    /// <summary>
    /// Удаляет игрока из группы.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="groupId">Идентификатор группы.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>True, если игрок успешно удалён из группы.</returns>
    public async Task<bool> RemovePlayerFromGroupAsync(
        long telegramId,
        int groupId,
        CancellationToken ct = default)
    {
        var player = await _db.Players
            .Include(p => p.Groups)
            .FirstOrDefaultAsync(p => p.TelegramId == telegramId, ct);

        if (player is null || !player.IsMemberOfGroup(groupId))
            return false;

        var group = player.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null)
            return false;
        
        player.Groups.Remove(group);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Игрок {Username} ({TelegramId}) покинул группу '{GroupName}' ({GroupId})",
            player.Username, telegramId, group.Name, groupId);

        return true;

    }

    /// <summary>
    /// Получает список всех игроков, состоящих в указанной группе.
    /// </summary>
    /// <param name="groupId">Идентификатор группы.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Список игроков группы.</returns>
    public async Task<List<Player>> GetGroupPlayersAsync(int groupId, CancellationToken ct = default) =>
        await _db.Groups
            .Where(g => g.Id == groupId)
            .SelectMany(g => g.Players)
            .ToListAsync(ct);

    /// <summary>
    /// Получает список всех игроков, у которых есть слоты доступности.
    /// </summary>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Список игроков со слотами.</returns>
    public async Task<List<Player>> GetPlayersWithSlotsAsync(CancellationToken ct = default) =>
        await _db.Players
            .Include(p => p.Slots)
            .Where(p => p.Slots.Any())
            .ToListAsync(ct);

    /// <summary>
    /// Обновляет время последней активности игрока.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="ct">Токен отмены операции.</param>
    public async Task TouchPlayerActivityAsync(long telegramId, CancellationToken ct = default)
    {
        var player = await GetPlayerAsync(telegramId, ct);
        if (player is not null)
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Проверяет, существует ли игрок в базе данных.
    /// </summary>
    /// <param name="telegramId">Идентификатор пользователя Telegram.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>True, если игрок найден.</returns>
    public async Task<bool> PlayerExistsAsync(long telegramId, CancellationToken ct = default) =>
        await _db.Players.AnyAsync(p => p.TelegramId == telegramId, ct);
    
}