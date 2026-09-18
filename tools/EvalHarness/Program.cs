using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services.Retrieval;
using CitizenServicesCopilot.EvalHarness;
using CitizenServicesCopilot.Infrastructure.Persistence;
using CitizenServicesCopilot.Infrastructure.Retrieval;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CitizenServicesCopilot.EvalHarness;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var setPath = ResolveGoldenSetPath(GetOption(args, "--set"));
        var outputPath = GetOption(args, "--output") ?? Path.Combine(Directory.GetCurrentDirectory(), "docs", "EVALUATION.md");
        var goldenSet = GoldenSetLoader.Load(setPath);

        Console.WriteLine(
            $"[harness] loaded golden set v{goldenSet.Version} - {goldenSet.Cases.Count} cases, {goldenSet.CorpusDocuments.Count} documents");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new OfflineEvalDbContext(options);

        var embeddings = new DeterministicEmbeddingGenerator();
        var documents = CorpusBuilder.Seed(db, goldenSet, embeddings);

        Console.WriteLine(
            $"[harness] seeded corpus: {documents.Count} documents, {db.DocumentChunks.Count()} chunks");

        var retriever = new GroundedRetriever(
            db,
            embeddings,
            new QueryEnhancer(),
            new HybridFusionEngine(),
            NullLogger<GroundedRetriever>.Instance);

        var defaults = goldenSet.Defaults;
        var runs = new List<(GoldenCase Golden, RetrievalResult Result)>(goldenSet.Cases.Count);

        foreach (var goldenCase in goldenSet.Cases)
        {
            var result = await retriever.RetrieveAsync(
                new RetrievalQuery(goldenCase.Query, defaults.TopK, defaults.MinRelevanceScore));

            runs.Add((goldenCase, result));

            Console.WriteLine(
                $"[run] {goldenCase.Id,-8} category={goldenCase.AdversarialCategory ?? "standard",-24} " +
                $"refuse={result.IsRefusal,-5} max={result.MaxScore,6:F4} chunks={result.Chunks.Count,-2} citations={result.Citations.Count}");
        }

        var report = MetricsCalculator.Compute(goldenSet, documents, runs);
        var markdown = ReportGenerator.Render(report);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        File.WriteAllText(outputPath, markdown);

        Console.WriteLine($"[harness] executed {runs.Count}/{goldenSet.Cases.Count} cases");
        Console.WriteLine($"[harness] hit-rate {report.HitRate:P1}, groundedness {report.Groundedness:P1}, "
            + $"refusal accuracy {report.Refusals.Accuracy:P1} (missed refusals {report.Refusals.MissedRefusal})");
        Console.WriteLine($"[harness] report written to {Path.GetFullPath(outputPath)}");

        return runs.Count == goldenSet.Cases.Count ? 0 : 1;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string ResolveGoldenSetPath(string? explicitPath)
    {
        string[] candidates;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            candidates = new[] { explicitPath };
        }
        else
        {
            var cwd = Directory.GetCurrentDirectory();
            candidates = new[]
            {
                Path.Combine(cwd, "tests", "eval", "golden-set.yaml"),
                Path.Combine(cwd, "..", "..", "tests", "eval", "golden-set.yaml")
            };
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException(
            $"Golden evaluation set not found. Tried: {string.Join("; ", candidates)}");
    }
}