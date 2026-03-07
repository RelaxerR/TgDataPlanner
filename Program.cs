using DefaultNamespace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using TgDataPlanner.Telegram;
using TgDataPlanner.Telegram.Handlers;

var builder = Host.CreateApplicationBuilder(args);

// Настройка логирования
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information); 

// 1. Регистрация настроек (токен и БД)
var botToken = builder.Configuration["BotToken"];

if (string.IsNullOrEmpty(botToken))
{
    throw new Exception("BotToken не найден в appsettings.json!");
}

// 2. Подключаем Telegram Bot Client
builder.Services.AddSingleton<ITelegramBotClient>(provider => 
    new TelegramBotClient(botToken));

// 3. Подключаем Базу Данных
builder.Services.AddDbContext<AppDbContext>();

// 4. Регистрация наших сервисов (указываем классы-обработчики)
builder.Services.AddScoped<UpdateHandler>(); 
builder.Services.AddScoped<CommandHandler>();
builder.Services.AddScoped<CallbackHandler>();

// Регистрируем фоновые службы (наследуются от BackgroundService)
builder.Services.AddHostedService<BotBackgroundService>(); 
// TODO: builder.Services.AddHostedService<ReminderService>();

using var host = builder.Build();

// Автоматическое применение миграций при старте
using (var scope = host.Services.CreateScope())
{
    // Указываем в скобках <AppDbContext>, чтобы компилятор понял, что достать из контейнера
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

await host.RunAsync();
