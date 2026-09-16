using CitizenServicesCopilot.Application.Services.Retrieval;

namespace CitizenServicesCopilot.UnitTests.Retrieval;

public class QueryEnhancerTests
{
    private readonly QueryEnhancer _enhancer = new();

    // ─── Passthrough ─────────────────────────────────────────────────────────

    [Fact]
    public async Task EnhanceQueryAsync_NonMappedQuery_ReturnsSameQuery()
    {
        const string query = "What documents do I need for a court summons?";
        var result = await _enhancer.EnhanceQueryAsync(query);
        Assert.Equal(query, result);
    }

    [Fact]
    public async Task EnhanceQueryAsync_EmptyQuery_ReturnsEmpty()
    {
        var result = await _enhancer.EnhanceQueryAsync(string.Empty);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task EnhanceQueryAsync_WhitespaceQuery_ReturnsEmpty()
    {
        var result = await _enhancer.EnhanceQueryAsync("   ");
        Assert.Equal(string.Empty, result);
    }

    // ─── Domain synonym expansion ─────────────────────────────────────────────

    [Theory]
    [InlineData("I am unemployed")]
    [InlineData("I was fired last month")]
    [InlineData("I was laid off")]
    [InlineData("lost job compensation")]
    public async Task EnhanceQueryAsync_UnemploymentTerms_ExpandsToCanonicalForm(string query)
    {
        var result = await _enhancer.EnhanceQueryAsync(query);
        Assert.Contains("unemployment", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("compensation", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("How do I renew my passport?")]
    [InlineData("passport application requirements")]
    public async Task EnhanceQueryAsync_PassportTerms_ExpandsToCanonicalForm(string query)
    {
        var result = await _enhancer.EnhanceQueryAsync(query);
        // Should contain the original query
        Assert.StartsWith(query, result, StringComparison.OrdinalIgnoreCase);
        // And should have been expanded
        Assert.True(result.Length > query.Length, "Expected query to be expanded");
    }

    [Theory]
    [InlineData("driving license renewal")]
    [InlineData("How do I get a license?")]
    public async Task EnhanceQueryAsync_DrivingLicenseTerms_ExpandsToCanonicalForm(string query)
    {
        var result = await _enhancer.EnhanceQueryAsync(query);
        Assert.True(result.Length > query.Trim().Length, "Expected expansion to occur");
    }

    [Theory]
    [InlineData("health insurance coverage")]
    [InlineData("I need علاج")]
    public async Task EnhanceQueryAsync_HealthInsuranceTerms_ExpandsToCanonicalForm(string query)
    {
        var result = await _enhancer.EnhanceQueryAsync(query);
        Assert.True(result.Length > query.Trim().Length, "Expected expansion to occur");
    }

    // ─── Structural guarantees ────────────────────────────────────────────────

    [Fact]
    public async Task EnhanceQueryAsync_ExpandedQuery_ContainsOriginalQuery()
    {
        const string query = "lost job help";
        var result = await _enhancer.EnhanceQueryAsync(query);

        Assert.StartsWith(query, result);
    }

    [Fact]
    public async Task EnhanceQueryAsync_MultipleMatchingPatterns_AllExpansionsAppended()
    {
        // "passport" matches passport pattern; deliberately combine two domains
        const string query = "lost job passport renewal";
        var result = await _enhancer.EnhanceQueryAsync(query);

        Assert.Contains("unemployment", result, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Length > query.Length, "Multiple expansions should make the result longer");
    }

    [Fact]
    public async Task EnhanceQueryAsync_IsDeterministic_SameInputSameOutput()
    {
        const string query = "unemployed and need health insurance";

        var result1 = await _enhancer.EnhanceQueryAsync(query);
        var result2 = await _enhancer.EnhanceQueryAsync(query);

        Assert.Equal(result1, result2);
    }
}
