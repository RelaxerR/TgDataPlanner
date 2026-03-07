using DefaultNamespace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;
// using MyDndBot.Data;
// using MyDndBot.Telegram;

var builder = Host.CreateApplicationBuilder(args);

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

// Регистрируем фоновые службы (наследуются от BackgroundService)
// TODO: builder.Services.AddHostedService<BotBackgroundService>(); 
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
