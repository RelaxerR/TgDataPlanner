using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using TgDataPlanner.Telegram.Handlers;
using static TgDataPlanner.Configuration.BotConstants.SystemMessages;

namespace TgDataPlanner.Telegram;

/// <summary>
/// Фоновая служба для запуска и поддержания работы Telegram-бота.
/// Реализует долгосрочный процесс получения обновлений через Long Polling.
/// </summary>
[SuppressMessage("Usage", "CA2253:Named placeholders should not be numeric values")]
public class BotBackgroundService : BackgroundService
{
    private readonly ILogger<BotBackgroundService> _logger;
    private readonly ITelegramBotClient _botClient;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="BotBackgroundService"/>.
    /// </summary>
    /// <param name="logger">Логгер для записи событий службы.</param>
    /// <param name="botClient">Клиент Telegram Bot API.</param>
    /// <param name="serviceProvider">Поставщик услуг для разрешения зависимостей.</param>
    public BotBackgroundService(
        ILogger<BotBackgroundService> logger,
        ITelegramBotClient botClient,
        IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// Основной цикл выполнения фоновой службы.
    /// Запускает механизм получения обновлений и поддерживает работу бота.
    /// </summary>
    /// <param name="stoppingToken">Токен отмены для корректного завершения службы.</param>
    /// <returns>Задача выполнения службы.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(BotStartingInit);
        try
        {
            var botInfo = await _botClient.GetMe(stoppingToken);
            _logger.LogInformation(
                BotAuthenticated,
                botInfo.Username,
                botInfo.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, BotTokenCheckFailed);
            return;
        }

        var receiverOptions = new ReceiverOptions
        {
            DropPendingUpdates = true,
            AllowedUpdates = [] // Получать все типы обновлений
        };

        _logger.LogInformation(BotUpdateHandlersSetup);
        _botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);

        _logger.LogInformation(BotStartedWaiting);

        // Поддерживаем службу активной до сигнала отмены
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    /// <summary>
    /// Обрабатывает входящее обновление от Telegram API.
    /// Создает scope DI и делегирует обработку специализированному обработчику.
    /// </summary>
    /// <param name="bot">Экземпляр клиента бота.</param>
    /// <param name="update">Объект обновления.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Задача выполнения обработки.</returns>
    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var updateHandler = scope.ServiceProvider.GetRequiredService<UpdateHandler>();
            _logger.LogDebug(
                BotUpdateDelegated,
                update.Type);
            await updateHandler.HandleUpdateAsync(update, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Ожидаемое поведение при остановке службы
            _logger.LogDebug(BotUpdateCancelled);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                BotUpdateError,
                update.Type);
        }
    }

    /// <summary>
    /// Обрабатывает ошибки, возникшие при получении обновлений от Telegram API.
    /// </summary>
    /// <param name="bot">Экземпляр клиента бота.</param>
    /// <param name="ex">Исключение, содержащее информацию об ошибке.</param>
    /// <param name="source">Источник ошибки в конвейере обработки.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Задача выполнения обработки.</returns>
    private Task HandleErrorAsync(
        ITelegramBotClient bot,
        Exception ex,
        HandleErrorSource source,
        CancellationToken ct)
    {
        // Классификация ошибок для более точного логирования
        switch (ex)
        {
            case ApiRequestException apiEx:
                _logger.LogWarning(
                    BotApiError,
                    apiEx.ErrorCode,
                    apiEx.Message,
                    source);
                break;
            case TaskCanceledException when ct.IsCancellationRequested:
                _logger.LogDebug(BotServiceCancelled, source);
                break;
            default:
                _logger.LogError(
                    ex,
                    BotCriticalError,
                    source);
                break;
        }

        // Возвращаем CompletedTask, чтобы polling продолжил работу
        return Task.CompletedTask;
    }

    /// <summary>
    /// Выполняет корректную остановку службы.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача выполнения остановки.</returns>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(BotStopping);
        // Останавливаем polling (если используется новая версия библиотеки с StopReceiving)
        // _botClient.StopReceiving(cancellationToken);
        await base.StopAsync(cancellationToken);
        _logger.LogInformation(BotStopped);
    }
}