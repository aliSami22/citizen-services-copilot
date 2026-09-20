using System.Reflection;
using System.Text.RegularExpressions;
using CitizenServicesCopilot.Application.Common.Interfaces;

namespace CitizenServicesCopilot.Application.Services.Prompts;

/// <summary>
/// Loads prompt templates from embedded <c>prompts/*.md</c> resources in the
/// Application assembly. Each file declares a banner:
/// <c>&lt;!-- prompt: &lt;key&gt; | version: &lt;n&gt; --&gt;</c>
/// which is stripped from the returned template and surfaced verbatim as the
/// prompt version.
/// </summary>
public sealed class EmbeddedResourcePromptProvider : IPromptProvider
{
    private const string PromptMarker = ".prompts.";
    private const string BannerStart = "<!-- prompt:";

    private static readonly Regex VersionPattern = new(@"version:\s*(?<version>[0-9a-zA-Z\._-]+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly Assembly _assembly;
    private readonly IReadOnlyDictionary<string, string> _resourceNames;

    public EmbeddedResourcePromptProvider(Assembly? assembly = null)
    {
        _assembly = assembly ?? typeof(EmbeddedResourcePromptProvider).Assembly;
        _resourceNames = _assembly
            .GetManifestResourceNames()
            .Where(name => name.Contains(PromptMarker, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(ToPromptKey, StringComparer.Ordinal);
    }

    public Task<string> GetPromptAsync(string promptKey, CancellationToken ct = default)
    {
        var content = ReadPrompt(promptKey);
        return Task.FromResult(StripBanner(content).Trim());
    }

    public Task<string> GetPromptVersionAsync(string promptKey, CancellationToken ct = default)
    {
        var content = ReadPrompt(promptKey);
        return Task.FromResult(ParseVersion(content));
    }

    private string ReadPrompt(string promptKey)
    {
        if (!_resourceNames.TryGetValue(promptKey, out var resourceName))
        {
            throw new InvalidOperationException($"No embedded prompt resource exists for key '{promptKey}'.");
        }

        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded prompt resource '{resourceName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string ToPromptKey(string resourceName)
    {
        var tail = resourceName.Substring(resourceName.LastIndexOf(PromptMarker, StringComparison.OrdinalIgnoreCase) + PromptMarker.Length);
        return tail.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? tail[..^3] : tail;
    }

    private static string StripBanner(string content)
    {
        var firstLine = content.Split(['\r', '\n'], 2)[0];
        return firstLine.TrimStart().StartsWith(BannerStart, StringComparison.Ordinal)
            ? content[(firstLine.Length + 1)..]
            : content;
    }

    private static string ParseVersion(string content)
    {
        var firstLine = content.Split(['\r', '\n'], 2)[0];
        var match = VersionPattern.Match(firstLine);
        return match.Success ? match.Groups["version"].Value : "unknown";
    }
}