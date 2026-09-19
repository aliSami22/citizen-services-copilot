using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Services.Tools;

/// <summary>
/// Central catalog of tool names and schemas for the tool executor.
/// </summary>
public static class ToolCatalog
{
    public const string SearchCorpus = "search_corpus";

    public const string GetRegulationVersion = "get_regulation_version";

    public const string ComputeFee = "compute_fee";

    public const string PersistDraft = "persist_draft";

    /// <summary>
    /// Schemas registered with the schema validator; every tool in the
    /// catalog must appear here.
    /// </summary>
    public static IReadOnlyList<ToolSchema> Schemas { get; } = new[]
    {
        SearchCorpusTool.Schema,
        GetRegulationVersionTool.Schema,
        ComputeFeeTool.Schema,
        PersistDraftTool.Schema
    };
}