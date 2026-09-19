namespace CitizenServicesCopilot.Application.Common.Interfaces;

/// <summary>
/// Resolves prompt templates for agents. Prompts live outside agent code
/// (embedded resources) so they can be inspected and versioned centrally.
/// </summary>
public interface IPromptProvider
{
    /// <summary>
    /// Returns the full prompt template for the given prompt key.
    /// </summary>
    Task<string> GetPromptAsync(string promptKey, CancellationToken ct = default);

    /// <summary>
    /// Returns the version banner declared at the top of the prompt template, or
    /// "unknown" when no version banner is present.
    /// </summary>
    Task<string> GetPromptVersionAsync(string promptKey, CancellationToken ct = default);
}