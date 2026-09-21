namespace CitizenServicesCopilot.Infrastructure.Llm;

public class LlmOptions
{
    public const string SectionName = "LlmSettings";

    public string Provider { get; set; } = "Ollama"; // "OpenAI" or "Ollama"

    /// <summary>
    /// Bound for every outbound LLM HTTP call. Prevents a hung provider from
    /// stalling a workflow run (and its SSE stream) indefinitely.
    /// </summary>
    public int HttpTimeoutSeconds { get; set; } = 60;

    public OpenAiConfig OpenAI { get; set; } = new();
    public OllamaConfig Ollama { get; set; } = new();
}

public class OpenAiConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string CheapModel { get; set; } = "gpt-4o-mini";
    public string ExpensiveModel { get; set; } = "gpt-4o";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
}

public class OllamaConfig
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string CheapModel { get; set; } = "llama3.2:1b";
    public string ExpensiveModel { get; set; } = "llama3.1:8b";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
}
