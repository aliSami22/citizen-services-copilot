using System.Security.Cryptography;
using System.Text;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Domain.Enums;

namespace CitizenServicesCopilot.EvalHarness;

/// <summary>
/// Seeds the offline corpus from the golden set definition. Returns a mapping of
/// golden document id to the seeded Document entity for citation checks.
/// </summary>
public static class CorpusBuilder
{
    public static IReadOnlyDictionary<string, Document> Seed(
        OfflineEvalDbContext db,
        GoldenSet set,
        IEmbeddingGenerator embeddings)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(embeddings);

        var mapping = new Dictionary<string, Document>(StringComparer.Ordinal);

        foreach (var goldenDocument in set.CorpusDocuments)
        {
            var document = new Document
            {
                Id = Guid.NewGuid(),
                Title = goldenDocument.Title,
                Source = goldenDocument.Source,
                Version = goldenDocument.Version,
                ContentHash = ComputeHash(goldenDocument.Id + "|" + goldenDocument.Title + "|" + goldenDocument.Version),
                Status = IngestionStatus.Completed,
                CreatedAtUtc = DateTime.UnixEpoch
            };

            document.Content = string.Join(Environment.NewLine,
                goldenDocument.Sections.Select(s => s.Content));

            db.Documents.Add(document);

            var chunkEmbeddings = embeddings.GenerateEmbeddingsBatchAsync(
                goldenDocument.Sections.Select(s => s.Content).ToList()).GetAwaiter().GetResult();

            for (int index = 0; index < goldenDocument.Sections.Count; index++)
            {
                var section = goldenDocument.Sections[index];
                var chunk = new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = document.Id,
                    Document = document,
                    Content = section.Content,
                    Section = section.Title,
                    PageNumber = section.Page,
                    ChunkIndex = index,
                    Embedding = chunkEmbeddings[index]
                };

                document.Chunks.Add(chunk);
                db.DocumentChunks.Add(chunk);
            }

            mapping[goldenDocument.Id] = document;
        }

        db.SaveChanges();
        return mapping;
    }

    private static string ComputeHash(string input)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}