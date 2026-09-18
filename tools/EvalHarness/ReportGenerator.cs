using System.Text;

namespace CitizenServicesCopilot.EvalHarness;

/// <summary>
/// Renders a deterministic Markdown evaluation baseline. Contains no timestamps or
/// machine-specific data so committed artifacts are byte-stable across runs.
/// </summary>
public static class ReportGenerator
{
    public static string Render(EvaluationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sb = new StringBuilder();

        sb.AppendLine("# D4 Citizen Services Retrieval Evaluation Baseline");
        sb.AppendLine();
        sb.AppendLine("- **Harness:** tools/EvalHarness (offline, deterministic)");
        sb.AppendLine($"- **Golden set:** v{report.Set.Version} - {report.Set.Cases.Count} cases "
            + $"({report.Set.Cases.Count(c => c.Type == "standard")} standard, "
            + $"{report.Set.Cases.Count(c => c.Type == "adversarial")} adversarial)");
        sb.AppendLine($"- **Corpus:** {report.CorpusDocumentCount} documents, {report.CorpusChunkCount} chunks");
        sb.AppendLine($"- **Retrieval config:** min_relevance_score={report.Set.Defaults.MinRelevanceScore:F2}, "
            + $"top_k={report.Set.Defaults.TopK}");
        sb.AppendLine("- **Embeddings:** deterministic SHA-256-stub (no provider calls)");
        sb.AppendLine("- **Domain:** " + report.Set.Domain);
        sb.AppendLine();
        sb.AppendLine("> Opinionated. This baseline reflects the shipped hybrid retrieval pipeline. "
            + "Any number below 100% is a real finding, not a goalpost adjustment.");
        sb.AppendLine();

        RenderSummary(sb, report);
        RenderRefusalMatrix(sb, report.Refusals);
        RenderPerCaseTable(sb, report);
        RenderInjectionDiagnostics(sb, report);
        RenderFailures(sb, report);
        RenderNotes(sb);

        return sb.ToString();
    }

    private static void RenderSummary(StringBuilder sb, EvaluationReport report)
    {
        var hitPopulation = report.HitRatePopulation;
        var groundedPopulation = report.GroundednessPopulation;
        var refusals = report.Refusals;

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| Hit-Rate (>=1 expected citation matched) | {hitPopulation.Count(c => c.HitRatePassed)}/{hitPopulation.Count} "
            + $"({report.HitRate:P1}) |");
        sb.AppendLine($"| Groundedness (expected claims in cited chunks) | {report.Groundedness:P1} "
            + $"over {groundedPopulation.Count} cases |");
        sb.AppendLine($"| Refusal Accuracy | {refusals.TrueRefuse + refusals.TrueAccept}/{refusals.Totalled} "
            + $"({refusals.Accuracy:P1}) |");
        sb.AppendLine($"| True refusals | {refusals.TrueRefuse} |");
        sb.AppendLine($"| Missed refusals (expected refuse, answered) | {refusals.MissedRefusal} "
            + $"({refusals.MissedRefusalRate:P1} of expected-refuse cases) |");
        sb.AppendLine($"| False refusals (expected answer, refused) | {refusals.FalseRefusal} |");
        sb.AppendLine($"| Poisoned injection content surfaced in top-K | {report.PoisonedContentSurfaced.Count} cases |");
        sb.AppendLine();
    }

    private static void RenderRefusalMatrix(StringBuilder sb, RefusalMatrix r)
    {
        sb.AppendLine("## Refusal Matrix (observed vs expected)");
        sb.AppendLine();
        sb.AppendLine("| Observed \\ Expected | Refuse | Accept |");
        sb.AppendLine("| --- | --- | --- |");
        sb.AppendLine($"| Refused | **{r.TrueRefuse}** (true refusal) | {r.FalseRefusal} (false refusal) |");
        sb.AppendLine($"| Answered | {r.MissedRefusal} (missed refusal) | **{r.TrueAccept}** (true accept) |");
        sb.AppendLine();
    }

    private static void RenderPerCaseTable(StringBuilder sb, EvaluationReport report)
    {
        sb.AppendLine("## Per-Case Results");
        sb.AppendLine();
        sb.AppendLine("Columns: success(?) = refusal decision correct; H = hit-rate; G = groundedness; "
            + "max = top combined score; sections = top-ranked returned citation sources.");
        sb.AppendLine();

        foreach (var grouped in report.Cases.GroupBy(c => c.Golden.AdversarialCategory ?? "standard"))
        {
            sb.AppendLine($"### {grouped.Key}");
            sb.AppendLine();
            sb.AppendLine("| id | expected | observed | success | H | G | max | returned sections |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");

            foreach (var c in grouped.OrderBy(c => c.Golden.Id, StringComparer.Ordinal))
            {
                string expected = c.Golden.ShouldRefuse ? "refuse" : "answer";
                string observed = c.Retrieval.IsRefusal ? "refuse" : "answer";
                string success = c.RefusalCorrect ? "yes" : "**no**";
                string hit = c.Golden.ExpectedCitations.Count > 0
                    ? (c.HitRatePassed ? "yes" : "no")
                    : "-";
                string grounded = c.Golden.ExpectedClaims.Count > 0 ? $"{c.Groundedness:P0}" : "-";

                string sections = string.Join(", ", c.ReturnedSections.Take(3));
                if (sections.Length > 90) sections = sections[..90] + "...";

                sb.AppendLine($"| {c.Golden.Id} | {expected} | {observed} | {success} | {hit} | {grounded} "
                    + $"| {c.Retrieval.MaxScore:F4} | {Escape(sections)} |");
            }

            sb.AppendLine();
        }
    }

    private static void RenderInjectionDiagnostics(StringBuilder sb, EvaluationReport report)
    {
        sb.AppendLine("## Injection Exposure Diagnostics");
        sb.AppendLine();
        sb.AppendLine("Checks whether text poisoned by an ingested document (injection marker embedded in the "
            + "corpus) surfaces inside returned retrieval chunks. Retrieval alone cannot remove poisoned "
            + "content; exposure here flags where a downstream human-review layer must strip or warn.");
        sb.AppendLine();

        if (report.InjectionExposureCases.Count == 0)
        {
            sb.AppendLine("No golden case asserts injection exposure expectations.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| id | marker | exposed | note |");
        sb.AppendLine("| --- | --- | --- | --- |");

        foreach (var c in report.InjectionExposureCases)
        {
            string marker = c.Golden.InjectionMarker ?? "-";
            if (marker.Length > 40) marker = marker[..40] + "...";
            string exposed = c.InjectionExposureObserved ? "YES (poisoned chunk in results)" : "no";
            sb.AppendLine($"| {c.Golden.Id} | `{Escape(marker)}` | {exposed} | {Escape(c.Golden.Id)} |");
        }

        sb.AppendLine();
        sb.AppendLine($"Poisoned document surfaced in top-K for {report.PoisonedContentSurfaced.Count} of "
            + $"{report.Cases.Count} cases: "
            + (report.PoisonedContentSurfaced.Count == 0
                ? "none."
                : string.Join(", ", report.PoisonedContentSurfaced.Select(c => c.Golden.Id))));
        sb.AppendLine();
    }

    private static void RenderFailures(StringBuilder sb, EvaluationReport report)
    {
        var failures = report.Cases
            .Where(c => !c.RefusalCorrect
                || (c.Golden.ExpectedCitations.Count > 0 && !c.HitRatePassed)
                || (c.Golden.ExpectedClaims.Count > 0 && c.Groundedness < 1.0))
            .ToList();

        if (failures.Count == 0)
        {
            return;
        }

        sb.AppendLine("## Failure Detail");
        sb.AppendLine();

        foreach (var c in failures)
        {
            sb.AppendLine($"### {c.Golden.Id} ({c.Golden.AdversarialCategory ?? "standard"})");
            sb.AppendLine();
            sb.AppendLine($"Query: `{Escape(c.Golden.Query)}`");

            if (!c.RefusalCorrect)
            {
                sb.AppendLine($"- Expected to **{(c.Golden.ShouldRefuse ? "refuse" : "answer")}** but "
                    + $"{(c.Retrieval.IsRefusal ? "refused" : "answered")}; max score "
                    + $"{c.Retrieval.MaxScore:F4}.");
            }

            if (!c.HitRatePassed && c.MissingCitationSections.Count > 0)
            {
                sb.AppendLine("- Expected citations not retrieved: "
                    + string.Join(", ", c.MissingCitationSections.Select(Escape)));
                sb.AppendLine("- Returned sections: " + string.Join(", ", c.ReturnedSections.Select(Escape)));
            }

            if (c.Golden.ExpectedClaims.Count > 0 && c.Groundedness < 1.0)
            {
                var missingClaims = c.Golden.ExpectedClaims
                    .Where(claim => !c.Retrieval.Chunks.Any(s =>
                        s.Chunk.Content.Contains(claim, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                sb.AppendLine("- Claims not grounded in cited chunks: "
                    + string.Join(", ", missingClaims.Select(Escape)));
            }

            sb.AppendLine();
        }
    }

    private static void RenderNotes(StringBuilder sb)
    {
        sb.AppendLine("## Known Limitations");
        sb.AppendLine();
        sb.AppendLine("1. **Refusal gate is rank-relative, not score-relative.** GroundedRefusalEngine's fused "
            + "score is normalized by the theoretical maximum RRF (rank 1 in both lists), so any candidate at "
            + "rank 1 of either list scores at least ~0.50. With the MinRelevanceScore gate at 0.40, a refusal "
            + "fires only when both the dense and keyword candidate lists are empty. Out-of-corpus, ambiguous, "
            + "and injection queries therefore receive top-K chunks instead of a grounded refusal (see the "
            + "Refusal Matrix rows above).");
        sb.AppendLine("2. **Dense ranking is offline-stubbed.** The deterministic SHA-256 embedding generator "
            + "produces stable but semantically random vectors; dense ranks are therefore noisy and the "
            + "keyword signal dominates. Replace with a real embedding provider in an online harness to recover "
            + "true semantic ordering.");
        sb.AppendLine("3. **No generation stage.** The harness exercises retrieval and refusal only; it does not "
            + "verify the downstream LLM answer, claim synthesis, or the human-review handoff.");
        sb.AppendLine("4. **Golden expectations encode the requirement spec, not current behavior.** These are "
            + "the acceptance criteria ITI will check; results below are the honest delta to be closed by "
            + "follow-up changes to the retrieval/refusal components.");
    }

    private static string Escape(string value)
        => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}