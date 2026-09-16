using System.Text.RegularExpressions;
using CitizenServicesCopilot.Application.Common.Interfaces.Retrieval;

namespace CitizenServicesCopilot.Application.Services.Retrieval;

/// <summary>
/// Domain query enhancer that bridges colloquial citizen phrasing to official regulatory and procedural vocabulary.
/// </summary>
public class QueryEnhancer : IQueryEnhancer
{
    private static readonly (string Pattern, string Expansion)[] DomainSynonyms =
    {
        (@"\b(lost\s*job|unemployed|fired|laid\s*off)\b", "unemployment insurance compensation job loss"),
        (@"\b(بطاقة|بطاقه|رقم\s*قومي|تجديد\s*بطاقة)\b", "بطاقة الرقم القومي الأحوال المدنية السجل المدني"),
        (@"\b(معاش|تكافل|كرامة|ضمان)\b", "معاش تكافل وكرامة الضمان الاجتماعي الدعم النقدي"),
        (@"\b(passport|passports|جواز|سفر)\b", "جواز السفر مصلحة الهجرة والجنسية"),
        (@"\b(license|driving\s*license|رخصة|قيادة)\b", "رخصة قيادة إدارة المرور الفحص الفني"),
        (@"\b(ration|food\s*subsidy|تموين|بطاقة\s*تموين)\b", "بطاقة التموين السلع التموينية الدعم العيني"),
        (@"\b(health\s*insurance|تأمين\s*صحي|علاج)\b", "التأمين الصحي الشامل الرعاية الصحية المستشفيات")
    };

    public Task<string> EnhanceQueryAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(string.Empty);
        }

        var normalized = query.Trim();
        var expansions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (pattern, expansion) in DomainSynonyms)
        {
            if (Regex.IsMatch(normalized, pattern, RegexOptions.IgnoreCase))
            {
                expansions.Add(expansion);
            }
        }

        if (expansions.Count == 0)
        {
            return Task.FromResult(normalized);
        }

        var enhanced = $"{normalized} {string.Join(" ", expansions)}";
        return Task.FromResult(enhanced);
    }
}
