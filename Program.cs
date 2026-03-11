using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using TgDataPlanner.Data;
using TgDataPlanner.Services;
using TgDataPlanner.Telegram;
using TgDataPlanner.Telegram.Handlers;

namespace TgDataPlanner;

/// <summary>
/// Точка входа в приложение планирования игровых сессий для Telegram.
/// Настраивает хостинг, зависимость внедрение, логирование и запускает бота.
/// </summary>
public static class Program
{
    /// <summary>
    /// Секция конфигурации для настроек бота.
    /// </summary>
    private const string BotConfigSection = "TelegramBot";

    /// <summary>
    /// Ключ конфигурации для токена бота.
    /// </summary>
    private const string BotTokenKey = "BotToken";

    /// <summary>
    /// Ключ конфигурации для идентификатора основного чата.
    /// </summary>
    private const string MainChatIdKey = "MainChatId";

    /// <summary>
    /// Ключ конфигурации для идентификаторов администраторов (через запятую).
    /// </summary>
    private const string AdminIdsKey = "AdminIds";

    /// <summary>
    /// Точка входа в приложение.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    /// <returns>Задача выполнения хоста.</returns>
    public static async Task Main(string[] args)
    {
        var builder = CreateHostBuilder(args);
        using var host = builder.Build();
        await InitializeDatabaseAsync(host);
        await ValidateConfigurationAsync(host);
        await host.RunAsync();
    }

    /// <summary>
    /// Создаёт и настраивает хост приложения.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    /// <returns>Настроенный <see cref="IHostBuilder"/>.</returns>
    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureLogging(ConfigureLogging)
            .ConfigureServices(ConfigureServices);

    /// <summary>
    /// Настраивает систему логирования приложения.
    /// </summary>
    /// <param name="context">Контекст хоста.</param>
    /// <param name="logging">Конфигуратор логирования.</param>
    private static void ConfigureLogging(HostBuilderContext context, ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.AddDebug();
        // Устанавливаем минимальный уровень логирования из конфигурации или по умолчанию
        var minLevel = context.Configuration.GetValue("Logging:LogLevel:Default", LogLevel.Information);
        logging.SetMinimumLevel(minLevel);
        // Фильтрация шумных логов от библиотек
        logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
        logging.AddFilter("Telegram.Bot", LogLevel.Warning);
    }

    /// <summary>
    /// Регистрирует сервисы в контейнере зависимости внедрения.
    /// </summary>
    /// <param name="context">Контекст хоста.</param>
    /// <param name="services">Коллекция сервисов.</param>
    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        // 1. Валидация и привязка конфигурации
        BindBotConfiguration(context.Configuration);

        // 2. Регистрация Telegram Bot Client (Singleton — один клиент на всё приложение)
        services.AddSingleton<ITelegramBotClient>(sp =>
        {
            var token = context.Configuration[$"{BotConfigSection}:{BotTokenKey}"]
                ?? throw new InvalidOperationException($"Токен бота не найден в секции '{BotConfigSection}'");
            return new TelegramBotClient(token);
        });

        // 3. Регистрация DbContext (Scoped — один контекст на запрос/обработку)
        services.AddDbContext<AppDbContext>(options =>
        {
            var connectionString = context.Configuration.GetConnectionString("DefaultConnection")
                ?? BuildDefaultConnectionString();
            options.UseSqlite(connectionString, sqlite =>
            {
                sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
            });
            // Только для разработки: подробные логи и чувствительные данные
            if (!context.HostingEnvironment.IsDevelopment())
                return;
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
        });

        // 4. Регистрация бизнес-сервисов (Scoped — зависят от DbContext)
        // ВАЖНО: регистрировать ДО хендлеров, которые от них зависят
        services.AddScoped<UserService>();
        services.AddScoped<SchedulingService>();
        // 4.1. Регистрация сервиса рекомендаций и планирования
        services.AddScoped<IRecommendationService, RecommendationService>();
        services.AddScoped<SessionPlanningService>();
        // 5. Регистрация обработчиков команд (Scoped — создаются на каждое обновление)
        services.AddScoped<CommandHandler>();
        services.AddScoped<CallbackHandler>();
        services.AddScoped<UpdateHandler>();
        // 6. Регистрация фоновых служб (Singleton — живут весь жизненный цикл хоста)
        services.AddHostedService<BotBackgroundService>();
        // services.AddHostedService<ReminderService>(); // TODO: реализовать сервис напоминаний
        // 7. Дополнительные сервисы (опционально)
        // services.AddHttpClient(); // для будущих внешних API-запросов
    }

    /// <summary>
    /// Привязывает настройки бота из конфигурации и валидирует их наличие.
    /// </summary>
    /// <param name="configuration">Конфигурация приложения.</param>
    private static void BindBotConfiguration(IConfiguration configuration)
    {
        var token = configuration[$"{BotConfigSection}:{BotTokenKey}"];
        var mainChatId = configuration[$"{BotConfigSection}:{MainChatIdKey}"];
        var adminIds = configuration[$"{BotConfigSection}:{AdminIdsKey}"];

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                $"Обязательная настройка '{BotConfigSection}:{BotTokenKey}' не найдена. " +
                $"Добавьте токен бота в appsettings.json или переменные окружения.");
        }

        if (string.IsNullOrWhiteSpace(mainChatId))
        {
            Console.WriteLine($"⚠️  Предупреждение: '{BotConfigSection}:{MainChatIdKey}' не настроен. Системные уведомления отключены.");
        }

        if (string.IsNullOrWhiteSpace(adminIds))
        {
            Console.WriteLine($"⚠️  Предупреждение: '{BotConfigSection}:{AdminIdsKey}' не настроен. Админ-команды будут недоступны.");
        }
    }

    /// <summary>
    /// Инициализирует базу данных: применяет миграции или создаёт схему.
    /// </summary>
    /// <param name="host">Запущенный хост приложения.</param>
    private static async Task InitializeDatabaseAsync(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Database");
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            logger.LogInformation("Инициализация базы данных...");
            // Проверка возможности подключения
            var canConnect = await dbContext.Database.CanConnectAsync();
            if (!canConnect)
            {
                logger.LogWarning("Не удалось подключиться к БД. Попытка создания...");
            }
            // Применение миграций или создание БД
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
            var migrations = pendingMigrations as string[] ?? pendingMigrations.ToArray();
            if (migrations.Length != 0)
            {
                logger.LogInformation("Применение {Count} миграций: {Migrations}",
                    migrations.Length,
                    string.Join(", ", migrations));
                await dbContext.Database.MigrateAsync();
                logger.LogInformation("Миграции успешно применены");
            }
            else
            {
                var created = await dbContext.Database.EnsureCreatedAsync();
                logger.LogInformation(created
                    ? "База данных создана с нуля"
                    : "Схема базы данных актуальна");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Критическая ошибка при инициализации базы данных");
            throw;
        }
    }

    /// <summary>
    /// Выполняет финальную валидацию конфигурации после сборки хоста.
    /// </summary>
    /// <param name="host">Запущенный хост приложения.</param>
    private static async Task ValidateConfigurationAsync(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CFG");
        var botClient = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        try
        {
            // Проверка валидности токена через API Telegram
            var botInfo = await botClient.GetMe();
            logger.LogInformation("✅ Бот @{Username} (ID: {BotId}) успешно аутентифицирован",
                botInfo.Username, botInfo.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Не удалось аутентифицировать бота. Проверьте токен и подключение к интернету");
            throw;
        }
    }

    /// <summary>
    /// Строит строку подключения к базе данных с вашим оригинальным путём.
    /// </summary>
    private static string BuildDefaultConnectionString()
    {
        // Ваш оригинальный путь: 3 уровня вверх от bin/Debug/net8.0/
        var dbPath = Path.Combine(AppContext.BaseDirectory, "../../../dnd_planner.db");
        // Преобразуем в абсолютный путь (важно для SQLite!)
        var absolutePath = Path.GetFullPath(dbPath);
        // Создаём директорию, если не существует
        var directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            Console.WriteLine($"📁 Создана директория: {directory}");
        }
        return $"Data Source={absolutePath};Cache=Shared;Foreign Keys=True;";
    }
}