using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Exceptions;

namespace CitizenServicesCopilot.Application.Common;

/// <summary>
/// Structural validator for drafts that must match the <c>DraftResponse</c>
/// shape (isRefusal, refusalReason, eligibilitySummary, requiredDocuments,
/// procedureSteps, feesAndTimeline, citations, tokensUsed). Used to reject
/// edited drafts whose content is not a well-formed response.
/// </summary>
public static class DraftJsonShapeValidator
{
    public static void Validate(string draftJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(draftJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidEditedContentException("not valid JSON", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidEditedContentException("expected a JSON object");
            }

            Require(root, "isRefusal", JsonValueKind.True, JsonValueKind.False);
            OptionalNullableString(root, "refusalReason");
            Require(root, "eligibilitySummary", JsonValueKind.String);
            Require(root, "requiredDocuments", JsonValueKind.String);
            Require(root, "procedureSteps", JsonValueKind.String);
            Require(root, "feesAndTimeline", JsonValueKind.String);
            Require(root, "citations", JsonValueKind.Array);
            RequireInteger(root, "tokensUsed");
        }
    }

    private static void Require(JsonElement root, string property, params JsonValueKind[] allowedKinds)
    {
        if (!root.TryGetProperty(property, out var value))
        {
            throw new InvalidEditedContentException($"missing required field '{property}'");
        }

        if (allowedKinds.Contains(value.ValueKind))
        {
            return;
        }

        throw new InvalidEditedContentException($"field '{property}' must be a {string.Join(" or ", allowedKinds)}");
    }

    private static void OptionalNullableString(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var value) &&
            value.ValueKind != JsonValueKind.Null &&
            value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidEditedContentException($"field '{property}' must be a string or null");
        }
    }

    private static void RequireInteger(JsonElement root, string property)
    {
        Require(root, property, JsonValueKind.Number);
        if (root.GetProperty(property).TryGetInt32(out _))
        {
            return;
        }

        throw new InvalidEditedContentException($"field '{property}' must be a whole number");
    }
}