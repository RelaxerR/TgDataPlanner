using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TgDataPlanner.AI;

/// <summary>
/// Сервис для взаимодействия с локальным или удалённым API Ollama.
/// Предоставляет методы для генерации текста с использованием LLM.
/// </summary>
public class OllamaService
{
    private readonly AiSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaService> _logger;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="OllamaService"/>.
    /// </summary>
    /// <param name="settings">Настройки подключения к Ollama.</param>
    /// <param name="httpClient">HTTP клиент для запросов.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    public OllamaService(
        IOptions<AiSettings> settings,
        HttpClient httpClient,
        ILogger<OllamaService> logger)
    {
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Базовый адрес устанавливается при создании клиента в DI, но можно продублировать проверку
        if (_httpClient.BaseAddress == null)
        {
            _logger.LogWarning("HttpClient BaseAddress не установлен. Используем BaseUrl из настроек: {BaseUrl}", _settings.BaseUrl);
        }
        else
        {
            _logger.LogDebug("OllamaService инициализирован. BaseAddress: {BaseAddress}, Model: {Model}", _httpClient.BaseAddress, _settings.ModelName);
        }
    }

    /// <summary>
    /// Генерирует ответ от нейросети на основе переданного промпта.
    /// Использует модель, указанную в настройках.
    /// </summary>
    /// <param name="prompt">Текст запроса пользователя.</param>
    /// <param name="systemPrompt">Системная инструкция для модели (опционально).</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Сгенерированный текст ответа.</returns>
    /// <exception cref="HttpRequestException">Выбрасывается при ошибке сети или API.</exception>
    /// <exception cref="JsonException">Выбрасывается при ошибке парсинга ответа.</exception>
    public async Task<string> GenerateAsync(string prompt, string? systemPrompt = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            _logger.LogWarning("Попытка вызова GenerateAsync с пустым промптом.");
            return string.Empty;
        }

        var requestBody = new
        {
            model = _settings.ModelName,
            prompt = prompt,
            system = systemPrompt ?? "You are a helpful assistant for the TgDataPlanner Telegram bot.",
            stream = false // Получаем полный ответ сразу, а не потоком
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        _logger.LogDebug("Отправка запроса к Ollama (Model: {Model}). Промпт: {PromptPreview}",
            _settings.ModelName,
            TruncateForLog(prompt));

        try
        {
            // Если BaseAddress не задан в HttpClient, добавляем путь явно
            const string requestPath = "/api/generate";
            
            var response = await _httpClient.PostAsync(requestPath, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Ошибка API Ollama: Status={StatusCode}, Details={Details}",
                    response.StatusCode,
                    TruncateForLog(errorContent));

                throw new HttpRequestException($"Ollama API returned status {response.StatusCode}: {errorContent}");
            }

            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

            using var doc = JsonDocument.Parse(responseString);
            if (doc.RootElement.TryGetProperty("response", out var responseElement))
            {
                var result = responseElement.GetString();
                _logger.LogDebug("Успешный ответ от Ollama. Длина: {Length}", result?.Length ?? 0);
                return result ?? string.Empty;
            }

            _logger.LogWarning("Ответ от Ollama не содержит поля 'response'. Полный ответ: {Response}", TruncateForLog(responseString));
            return string.Empty;
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken == cancellationToken)
        {
            _logger.LogWarning("Запрос к Ollama отменён пользователем.");
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Превышен таймаут ожидания ответа от Ollama ({Timeout}s).", _settings.TimeoutSeconds);
            throw new TimeoutException($"Timeout waiting for Ollama response after {_settings.TimeoutSeconds} seconds", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Ошибка парсинга JSON ответа от Ollama.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Критическая ошибка при взаимодействии с Ollama.");
            throw;
        }
    }

    /// <summary>
    /// Обрезает текст для безопасного логирования.
    /// </summary>
    private static string TruncateForLog(string? text, int maxLength = 100)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}