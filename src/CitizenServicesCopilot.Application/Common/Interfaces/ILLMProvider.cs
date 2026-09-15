using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Common.Interfaces;

public interface ILLMProvider
{
    string ProviderName { get; }
    Task<LlmResponse> GenerateCompletionAsync(LlmPrompt prompt, CancellationToken ct = default);
}
