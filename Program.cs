using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;
// using MyDndBot.Data;
// using MyDndBot.Telegram;

var builder = Host.CreateApplicationBuilder(args);

// 1. Регистрация настроек (токен и БД)
// В реальности лучше брать из builder.Configuration
string botToken = "ВАШ_ТОКЕН";

// 2. Подключаем Telegram Bot Client
builder.Services.AddSingleton(provider =>
    new TelegramBotClient(botToken));

// 3. Подключаем Базу Данных
// builder.Services.AddDbContext();

// 4. Регистрация наших сервисов
// builder.Services.AddScoped(); // Логика обработки сообщений
// builder.Services.AddHostedService(); // Сервис, который держит бота запущенным
// builder.Services.AddHostedService(); // Тот самый сервис для напоминаний раз в 3 часа

using IHost host = builder.Build();

// Автоматическое применение миграций при старте (удобно для разработки)
using (var scope = host.Services.CreateScope())
{
    // var db = scope.ServiceProvider.GetRequiredService();
    // db.Database.EnsureCreated();
}

await host.RunAsync();
