namespace CitizenServicesCopilot.EvalHarness;

/// <summary>
/// Root object mirroring tests/eval/golden-set.yaml.
/// </summary>
public sealed class GoldenSet
{
    public string Version { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public GoldenDefaults Defaults { get; set; } = new();
    public List<GoldenDocument> CorpusDocuments { get; set; } = new();
    public List<GoldenCase> Cases { get; set; } = new();
}

public sealed class GoldenDefaults
{
    public double MinRelevanceScore { get; set; } = 0.40;
    public int TopK { get; set; } = 4;
}

public sealed class GoldenDocument
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public List<GoldenSection> Sections { get; set; } = new();
}

public sealed class GoldenSection
{
    public string Title { get; set; } = string.Empty;
    public int Page { get; set; } = 1;
    public string Content { get; set; } = string.Empty;
    public string? FixturePath { get; set; }
}

public sealed class GoldenCase
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "standard";
    public string Service { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string? Answer { get; set; }
    public List<string> ExpectedClaims { get; set; } = new();
    public List<GoldenCitation> ExpectedCitations { get; set; } = new();
    public bool ShouldRefuse { get; set; }
    public bool ExpectsInjectionExposure { get; set; }
    public string? InjectionMarker { get; set; }
    public string? AdversarialCategory { get; set; }
    public List<string> Tags { get; set; } = new();
}

public sealed class GoldenCitation
{
    public string Document { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
}