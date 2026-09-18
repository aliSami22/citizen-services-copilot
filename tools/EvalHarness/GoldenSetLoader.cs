using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CitizenServicesCopilot.EvalHarness;

/// <summary>
/// Loads and structurally validates the golden evaluation set.
/// </summary>
public static class GoldenSetLoader
{
    private static readonly string[] RequiredAdversarialCategories =
    {
        "out_of_corpus",
        "ambiguous",
        "direct_prompt_injection",
        "indirect_prompt_injection",
        "conflicting_sources"
    };

    public static GoldenSet Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Golden evaluation set not found: {path}", path);
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        var set = deserializer.Deserialize<GoldenSet>(File.ReadAllText(path))
            ?? throw new InvalidDataException($"Golden evaluation set is empty: {path}");

        ResolveFixtures(set, path);
        Validate(set);
        return set;
    }

    private static void ResolveFixtures(GoldenSet set, string goldenSetPath)
    {
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(goldenSetPath)) ?? ".";
        foreach (var document in set.CorpusDocuments)
        {
            foreach (var section in document.Sections)
            {
                if (string.IsNullOrWhiteSpace(section.FixturePath))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(section.Content))
                {
                    throw new InvalidDataException(
                        $"Section '{section.Title}' (doc '{document.Id}') declares both inline content and fixture_path.");
                }

                var fixturePath = Path.Combine(baseDir, section.FixturePath);
                if (!File.Exists(fixturePath))
                {
                    throw new FileNotFoundException(
                        $"Fixture for doc '{document.Id}', section '{section.Title}' not found: {fixturePath}",
                        fixturePath);
                }

                section.Content = File.ReadAllText(fixturePath);
            }
        }
    }

    public static void Validate(GoldenSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        if (set.Cases.Count == 0)
        {
            throw new InvalidDataException("Golden set must contain at least one case.");
        }

        var duplicateId = set.Cases.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicateId != null)
        {
            throw new InvalidDataException($"Duplicate case id '{duplicateId.Key}'.");
        }

        var documentIds = set.CorpusDocuments.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var document in set.CorpusDocuments)
        {
            if (string.IsNullOrWhiteSpace(document.Id))
            {
                throw new InvalidDataException("Every corpus document requires a non-empty id.");
            }
        }

        foreach (var c in set.Cases)
        {
            if (string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Query))
            {
                throw new InvalidDataException($"Case requires non-empty id and query: '{c.Id}'.");
            }

            if (c.Type is not ("standard" or "adversarial"))
            {
                throw new InvalidDataException($"Case '{c.Id}' has invalid type '{c.Type}'.");
            }

            foreach (var citation in c.ExpectedCitations)
            {
                if (!documentIds.Contains(citation.Document))
                {
                    throw new InvalidDataException($"Case '{c.Id}' cites unknown document '{citation.Document}'.");
                }
            }

            if (c.ShouldRefuse
                && (c.ExpectedCitations.Count > 0 || !string.IsNullOrWhiteSpace(c.Answer)))
            {
                throw new InvalidDataException(
                    $"Refusal case '{c.Id}' must not carry expected citations or an answer.");
            }

            if (c.ExpectsInjectionExposure && string.IsNullOrWhiteSpace(c.InjectionMarker))
            {
                throw new InvalidDataException(
                    $"Case '{c.Id}' expects injection exposure but has no injection_marker.");
            }
        }

        var standardCount = set.Cases.Count(c => c.Type == "standard");
        if (standardCount < 25)
        {
            throw new InvalidDataException(
                $"Golden set requires at least 25 standard cases (found {standardCount}).");
        }

        var adversarial = set.Cases.Where(c => c.Type == "adversarial").ToList();
        if (adversarial.Count < 5)
        {
            throw new InvalidDataException(
                $"Golden set requires at least 5 adversarial cases (found {adversarial.Count}).");
        }

        var categoryCounts = adversarial
            .GroupBy(c => c.AdversarialCategory!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        foreach (var category in RequiredAdversarialCategories)
        {
            if (!categoryCounts.TryGetValue(category, out var count))
            {
                throw new InvalidDataException(
                    $"Golden set is missing adversarial category '{category}' (exposure/refusal suite incomplete).");
            }

            _ = count;
        }
    }
}