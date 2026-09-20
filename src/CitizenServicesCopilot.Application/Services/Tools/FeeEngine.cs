using System.Text.Json;

namespace CitizenServicesCopilot.Application.Services.Tools;

/// <summary>
/// Immutable line item within a fee breakdown.
/// </summary>
public sealed record FeeLineItem(string Description, decimal Amount);

/// <summary>
/// A computed fee estimate for a citizen service.
/// </summary>
public sealed record FeeBreakdown(
    string ServiceType,
    decimal Total,
    string Currency,
    IReadOnlyList<FeeLineItem> Items);

/// <summary>
/// Deterministic, LLM-free fee calculation engine used by <see cref="ComputeFeeTool"/>.
/// All arithmetic is decimal; validation failures return an error string.
/// </summary>
internal static class FeeEngine
{
    public const string Currency = "AED";

    private const decimal PassportBaseFee = 100m;
    private const decimal PassportPageSurcharge = 5m;
    private const decimal ExpeditedSurcharge = 50m;
    private const decimal ResidencyBaseFee = 100m;
    private const decimal ResidencyPerYearFee = 75m;
    private const decimal DependentSurcharge = 50m;
    private const decimal DriverLicenseFee = 80m;
    private const decimal BirthCertificateFee = 30m;
    private const decimal NationalIdFee = 50m;

    private const int DefaultPassportPages = 32;
    private const int MinPassportPages = 32;
    private const int MaxPassportPages = 100;
    private const int MaxDurationYears = 10;
    private const int MaxDependents = 8;

    private const string Passport = "passport";
    private const string ResidencyPermit = "residency_permit";
    private const string DriverLicense = "driver_license";
    private const string BirthCertificate = "birth_certificate";
    private const string NationalId = "national_id";

    public static (FeeBreakdown? Fee, string? Error) Compute(string serviceType, JsonElement parameters)
    {
        switch (serviceType)
        {
            case Passport:
                return ComputePassport(parameters);
            case ResidencyPermit:
                return ComputeResidencyPermit(parameters);
            case DriverLicense:
                return FlatFee(DriverLicense, DriverLicenseFee);
            case BirthCertificate:
                return FlatFee(BirthCertificate, BirthCertificateFee);
            case NationalId:
                return FlatFee(NationalId, NationalIdFee);
            default:
                return (null, $"unknown service type '{serviceType}'");
        }
    }

    private static (FeeBreakdown?, string?) ComputePassport(JsonElement parameters)
    {
        var items = new List<FeeLineItem> { new("Base fee", PassportBaseFee) };
        decimal total = PassportBaseFee;

        if (parameters.TryGetProperty("pages", out var pagesProp) && pagesProp.ValueKind != JsonValueKind.Null)
        {
            if (TryGetInt(pagesProp, "pages", out var pages, out var error))
            {
                if (pages < MinPassportPages)
                {
                    return (null, $"parameter 'pages' must be at least {MinPassportPages}");
                }

                if (pages > MaxPassportPages)
                {
                    return (null, $"parameter 'pages' must be at most {MaxPassportPages}");
                }

                var extraPages = pages - DefaultPassportPages;
                if (extraPages > 0)
                {
                    var pageFee = extraPages * PassportPageSurcharge;
                    items.Add(new FeeLineItem($"{extraPages} additional page(s)", pageFee));
                    total += pageFee;
                }
            }
            else
            {
                return (null, error);
            }
        }

        if (parameters.TryGetProperty("expedited", out var expeditedProp) && expeditedProp.ValueKind != JsonValueKind.Null)
        {
            if (expeditedProp.ValueKind != JsonValueKind.True && expeditedProp.ValueKind != JsonValueKind.False)
            {
                return (null, "parameter 'expedited' must be a boolean");
            }

            if (expeditedProp.GetBoolean())
            {
                items.Add(new FeeLineItem("Expedited processing", ExpeditedSurcharge));
                total += ExpeditedSurcharge;
            }
        }

        return (new FeeBreakdown(Passport, total, Currency, items), null);
    }

    private static (FeeBreakdown?, string?) ComputeResidencyPermit(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("durationYears", out var durationProp) || durationProp.ValueKind == JsonValueKind.Null)
        {
            return (null, "missing required parameter 'durationYears'");
        }

        if (!TryGetInt(durationProp, "durationYears", out var durationYears, out var durationError))
        {
            return (null, durationError);
        }

        if (durationYears < 0)
        {
            return (null, "parameter 'durationYears' must be non-negative");
        }

        if (durationYears > MaxDurationYears)
        {
            return (null, $"parameter 'durationYears' must be at most {MaxDurationYears}");
        }

        var items = new List<FeeLineItem>
        {
            new("Base fee", ResidencyBaseFee),
            new($"Permit duration ({durationYears} year(s))", durationYears * ResidencyPerYearFee)
        };
        decimal total = ResidencyBaseFee + durationYears * ResidencyPerYearFee;

        if (parameters.TryGetProperty("dependents", out var dependentsProp) && dependentsProp.ValueKind != JsonValueKind.Null)
        {
            if (!TryGetInt(dependentsProp, "dependents", out var dependents, out var dependentsError))
            {
                return (null, dependentsError);
            }

            if (dependents < 0)
            {
                return (null, "parameter 'dependents' must be non-negative");
            }

            if (dependents > MaxDependents)
            {
                return (null, $"parameter 'dependents' must be at most {MaxDependents}");
            }

            var dependentsFee = dependents * DependentSurcharge;
            items.Add(new FeeLineItem($"{dependents} dependent(s)", dependentsFee));
            total += dependentsFee;
        }

        return (new FeeBreakdown(ResidencyPermit, total, Currency, items), null);
    }

    private static (FeeBreakdown?, string?) FlatFee(string serviceType, decimal amount)
        => (new FeeBreakdown(serviceType, amount, Currency, new List<FeeLineItem> { new("Flat fee", amount) }), null);

    private static bool TryGetInt(JsonElement element, string name, out int value, out string? error)
    {
        value = 0;
        error = null;

        if (element.ValueKind != JsonValueKind.Number)
        {
            error = $"parameter '{name}' must be a JSON number";
            return false;
        }

        decimal parsed;
        try
        {
            parsed = element.GetDecimal();
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidOperationException)
        {
            error = $"parameter '{name}' is out of range";
            return false;
        }

        if (parsed % 1 != 0)
        {
            error = $"parameter '{name}' must be a whole number";
            return false;
        }

        value = Decimal.ToInt32(parsed);
        return true;
    }
}