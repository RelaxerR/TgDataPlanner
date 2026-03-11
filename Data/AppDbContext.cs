using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgDataPlanner.Data.Entities;

namespace TgDataPlanner.Data;

/// <summary>
/// Контекст базы данных для приложения планирования игровых сессий.
/// Управляет сущностями <see cref="Player"/>, <see cref="Group"/> и <see cref="AvailabilitySlot"/>.
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>
    /// Конфигурационные константы контекста.
    /// </summary>
    private static class Config
    {
        public const string DatabaseFileName = "dnd_planner.db";
        public const string ConnectionStringTemplate = "Data Source={0};Cache=Shared;Foreign Keys=True;";
    }

    /// <summary>
    /// Набор сущностей игроков для запросов и сохранения.
    /// </summary>
    public DbSet<Player> Players
    {
        get => Set<Player>();
    }

    /// <summary>
    /// Набор сущностей групп для запросов и сохранения.
    /// </summary>
    public DbSet<Group> Groups
    {
        get => Set<Group>();
    }

    /// <summary>
    /// Набор сущностей слотов доступности для запросов и сохранения.
    /// </summary>
    public DbSet<AvailabilitySlot> Slots
    {
        get => Set<AvailabilitySlot>();
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="AppDbContext"/>.
    /// </summary>
    public AppDbContext() { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="AppDbContext"/> с заданными опциями.
    /// </summary>
    /// <param name="options">Опции конфигурации контекста.</param>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    /// <summary>
    /// Настраивает параметры подключения к базе данных.
    /// Используется при создании контекста без явной передачи опций.
    /// </summary>
    /// <param name="optionsBuilder">Конструктор опций контекста.</param>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured)
            return;
        var dbPath = GetDatabasePath();
        var connectionString = string.Format(Config.ConnectionStringTemplate, dbPath);
        optionsBuilder
            .UseSqlite(connectionString, sqliteOptions =>
            {
                sqliteOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            })
            .EnableSensitiveDataLogging()      // Только для разработки!
            .LogTo(Console.WriteLine, LogLevel.Information);
    }

    /// <summary>
    /// Настраивает модель данных: связи, индексы, ограничения.
    /// Вызывается автоматически при инициализации контекста.
    /// </summary>
    /// <param name="modelBuilder">Конструктор модели данных.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigurePlayerEntity(modelBuilder);
        ConfigureGroupEntity(modelBuilder);
        ConfigureAvailabilitySlotEntity(modelBuilder);
        ConfigureManyToManyRelation(modelBuilder);
    }

    /// <summary>
    /// Настраивает сущность <see cref="Player"/>: индексы и ограничения.
    /// </summary>
    private static void ConfigurePlayerEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasKey(p => p.TelegramId);
            entity.HasIndex(p => p.Username)
                .HasDatabaseName("IX_Players_Username");
            entity.Property(p => p.Username)
                .IsRequired()
                .HasMaxLength(32)
                .HasDefaultValue("Unknown");
            entity.Property(p => p.CurrentState)
                .HasMaxLength(50);
        });
    }

    /// <summary>
    /// Настраивает сущность <see cref="Group"/>: индексы и ограничения.
    /// </summary>
    private static void ConfigureGroupEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Group>(entity =>
        {
            entity.HasKey(g => g.Id);
            // Индекс для производительности поиска по чату
            entity.HasIndex(g => g.TelegramChatId)
                .HasDatabaseName("IX_Groups_ChatId");
            // Индекс для фильтрации по статусу сессии
            entity.HasIndex(g => g.SessionStatus)
                .HasDatabaseName("IX_Groups_SessionStatus");
            entity.Property(g => g.Name)
                .IsRequired(false)
                .HasMaxLength(100);
            entity.Property(g => g.SessionStatus)
                .HasDefaultValue(Common.SessionStatus.NoSession);
        });
    }

    /// <summary>
    /// Настраивает сущность <see cref="AvailabilitySlot"/>: индексы и ограничения.
    /// </summary>
    private static void ConfigureAvailabilitySlotEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AvailabilitySlot>(entity =>
        {
            entity.HasKey(s => s.Id);
            // Уникальный индекс: один слот на игрока на конкретное время
            entity.HasIndex(s => new { s.PlayerId, s.DateTimeUtc })
                .HasDatabaseName("IX_Slots_Player_Time")
                .IsUnique();
            entity.HasIndex(s => s.DateTimeUtc)
                .HasDatabaseName("IX_Slots_DateTime");
            entity.Property(s => s.DateTimeUtc)
                .IsRequired()
                .HasColumnType("datetime");
            entity.Property(s => s.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }

    /// <summary>
    /// Настраивает связь многие-ко-многим между <see cref="Player"/> и <see cref="Group"/>.
    /// </summary>
    private static void ConfigureManyToManyRelation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Group>()
            .HasMany(g => g.Players)
            .WithMany(p => p.Groups)
            .UsingEntity<Dictionary<string, object>>(
                "GroupPlayers",  // Имя промежуточной таблицы
                j => j.HasOne<Player>().WithMany().HasForeignKey("PlayersTelegramId"),
                j => j.HasOne<Group>().WithMany().HasForeignKey("GroupsId"),
                j =>
                {
                    j.HasKey("GroupsId", "PlayersTelegramId");
                    j.HasIndex("PlayersTelegramId", "GroupsId").HasDatabaseName("IX_GroupPlayers_Reverse");
                });
    }

    /// <summary>
    /// Возвращает полный путь к файлу базы данных.
    /// Создаёт директорию, если она не существует.
    /// </summary>
    /// <returns>Абсолютный путь к файлу .db.</returns>
    private static string GetDatabasePath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var relativePath = Path.Combine("Data", "Database", Config.DatabaseFileName);
        var fullPath = Path.Combine(baseDirectory, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        return fullPath;
    }

    /// <summary>
    /// Применяет миграции к базе данных при необходимости.
    /// Удобный метод для инициализации БД при старте приложения.
    /// </summary>
    public void EnsureDatabaseCreated()
    {
        if (Database.GetPendingMigrations().Any())
        {
            Database.Migrate();
        }
        else
        {
            Database.EnsureCreated();
        }
    }
}