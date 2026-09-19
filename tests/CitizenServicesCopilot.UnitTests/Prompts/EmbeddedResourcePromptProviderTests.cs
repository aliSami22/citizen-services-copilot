using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services.Prompts;

namespace CitizenServicesCopilot.UnitTests.Prompts;

public class EmbeddedResourcePromptProviderTests
{
    private readonly EmbeddedResourcePromptProvider _provider = new();

    [Fact]
    public async Task GetPromptAsync_ReturnsTemplateWithVersionBannerStripped()
    {
        var prompt = await _provider.GetPromptAsync(PromptKeys.EligibilityIdentifier);

        Assert.Contains("Eligibility Identifier Agent", prompt);
        Assert.Contains("{context}", prompt);
        Assert.DoesNotContain("<!-- prompt:", prompt);
    }

    [Fact]
    public async Task GetPromptVersionAsync_ReturnsDeclaredVersion()
    {
        var version = await _provider.GetPromptVersionAsync(PromptKeys.ResponseDrafter);

        Assert.Equal("1", version);
    }

    [Fact]
    public async Task GetPromptAsync_UnknownKey_Throws()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _provider.GetPromptAsync("no-such-prompt"));

        Assert.Contains("no-such-prompt", ex.Message);
    }
}