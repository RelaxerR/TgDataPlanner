namespace TgDataPlanner.AI;

public class AiSettings
{
    /// <summary>
    /// Базовый адрес API Ollama.
    /// Для локальной разработки через SSH-туннель должно быть: http://localhost:11434
    /// Для продакшена внутри Docker-сети: http://ollama:11434
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Имя модели, например "qwen2.5:1.5b-instruct-q4_K_M"
    /// </summary>
    public string ModelName { get; set; } = "qwen2.5:1.5b-instruct";

    /// <summary>
    /// Таймаут запроса в секундах
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;
}
