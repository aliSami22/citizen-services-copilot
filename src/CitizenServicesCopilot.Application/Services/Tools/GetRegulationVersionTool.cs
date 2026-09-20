using System.Text.Json;
using CitizenServicesCopilot.Application.Common;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Services.Tools;

/// <summary>
/// Read tool returning the version and issuance date of a regulation document.
/// </summary>
public sealed class GetRegulationVersionTool : ITool
{
    private readonly IDocumentRepository _documentRepository;

    public string Name => ToolCatalog.GetRegulationVersion;

    public bool IsWrite => false;

    public static ToolSchema Schema { get; } = new(
        ToolName: ToolCatalog.GetRegulationVersion,
        Parameters: new[]
        {
            new ToolParameterSpec("documentId", JsonValueKind.String, IsRequired: true)
        });

    public GetRegulationVersionTool(IDocumentRepository documentRepository)
    {
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
    }

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("documentId", out var documentIdProp) ||
            documentIdProp.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(documentIdProp.GetString()))
        {
            return ToolResult.Failed("missing or invalid required field 'documentId'.");
        }

        if (!Guid.TryParse(documentIdProp.GetString(), out var documentId))
        {
            return ToolResult.Failed("field 'documentId' must be a valid GUID.");
        }

        var document = await _documentRepository.GetByIdAsync(documentId, ct);
        if (document is null)
        {
            return ToolResult.Failed($"document '{documentId}' was not found.");
        }

        var payload = JsonSerializer.SerializeToElement(new
        {
            documentId = document.Id,
            title = document.Title,
            version = document.Version,
            issuedAt = document.CreatedAtUtc,
            source = document.Source
        }, JsonOptions.CamelCase);

        return ToolResult.Succeeded(payload);
    }
}