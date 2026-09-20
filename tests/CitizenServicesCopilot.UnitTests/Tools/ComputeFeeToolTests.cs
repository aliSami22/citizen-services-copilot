using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services.Tools;

namespace CitizenServicesCopilot.UnitTests.Tools;

public class ComputeFeeToolTests
{
    private readonly ComputeFeeTool _tool = new();

    private static JsonElement Args(string serviceType, object parameters)
        => JsonSerializer.SerializeToElement(new { serviceType, parameters });

    private static decimal Total(ToolResult result)
        => result.Payload.GetProperty("total").GetDecimal();

    [Theory]
    [InlineData("passport", 100)]
    [InlineData("driver_license", 80)]
    [InlineData("birth_certificate", 30)]
    [InlineData("national_id", 50)]
    public async Task FlatAndBaseServices_ReturnExpectedTotal(string serviceType, decimal expected)
    {
        var result = await _tool.ExecuteAsync(Args(serviceType, new { }));

        Assert.True(result.Success);
        Assert.Equal(expected, Total(result));
        Assert.Equal("AED", result.Payload.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task Passport_AdditionalPages_SurchargeApplied()
    {
        var result = await _tool.ExecuteAsync(Args("passport", new { pages = 40 }));

        Assert.True(result.Success);
        Assert.Equal(140m, Total(result));
    }

    [Fact]
    public async Task Passport_DefaultPages_NoSurcharge()
    {
        var result = await _tool.ExecuteAsync(Args("passport", new { pages = 32 }));

        Assert.True(result.Success);
        Assert.Equal(100m, Total(result));
    }

    [Fact]
    public async Task Passport_Expedited_SurchargeApplied()
    {
        var result = await _tool.ExecuteAsync(Args("passport", new { expedited = true }));

        Assert.True(result.Success);
        Assert.Equal(150m, Total(result));
        Assert.Single(result.Payload.GetProperty("items").EnumerateArray(), i => i.GetProperty("description").GetString() == "Expedited processing");
    }

    [Fact]
    public async Task ResidencyPermit_ZeroDurationYears_ReturnsBaseFee()
    {
        var result = await _tool.ExecuteAsync(Args("residency_permit", new { durationYears = 0 }));

        Assert.True(result.Success);
        Assert.Equal(100m, Total(result));
    }

    [Fact]
    public async Task ResidencyPermit_WithDependents_SumsSurcharges()
    {
        var result = await _tool.ExecuteAsync(Args("residency_permit", new { durationYears = 2, dependents = 1 }));

        Assert.True(result.Success);
        Assert.Equal(100m + 150m + 50m, Total(result));
    }

    [Theory]
    [InlineData("passport", new object[] { "pages", 31 }, "must be at least")]
    [InlineData("passport", new object[] { "pages", 0 }, "must be at least")]
    [InlineData("passport", new object[] { "pages", -5 }, "must be at least")]
    [InlineData("passport", new object[] { "pages", 100000 }, "must be at most")]
    [InlineData("residency_permit", new object[] { "durationYears", -1 }, "must be non-negative")]
    [InlineData("residency_permit", new object[] { "durationYears", 100000 }, "must be at most")]
    [InlineData("residency_permit", new object[] { "dependents", -1 }, "must be non-negative")]
    [InlineData("residency_permit", new object[] { "dependents", 99 }, "must be at most")]
    public async Task OutOfRangeValues_Fail(string serviceType, object[] badParam, string messageFragment)
    {
        var (name, rawValue) = (badParam[0].ToString()!, Convert.ToInt64(badParam[1]));
        var parameters = new Dictionary<string, object> { [name] = rawValue };
        if (name == "dependents")
        {
            parameters["durationYears"] = 5;
        }
        else if (name == "pages")
        {
            parameters["serviceType"] = "passport";
        }

        var result = await _tool.ExecuteAsync(Args(serviceType, parameters));

        Assert.False(result.Success);
        Assert.Contains(messageFragment, result.Error);
    }

    [Fact]
    public async Task MissingRequiredParameter_Fails()
    {
        var result = await _tool.ExecuteAsync(Args("residency_permit", new { }));

        Assert.False(result.Success);
        Assert.Contains("missing required parameter 'durationYears'", result.Error);
    }

    [Fact]
    public async Task WrongParameterType_Fails()
    {
        var result = await _tool.ExecuteAsync(Args("residency_permit", new Dictionary<string, object> { ["durationYears"] = "two" }));

        Assert.False(result.Success);
        Assert.Contains("must be a JSON number", result.Error);
    }

    [Fact]
    public async Task FractionalParameter_Fails()
    {
        var result = await _tool.ExecuteAsync(Args("residency_permit", new Dictionary<string, object> { ["durationYears"] = 2.5 }));

        Assert.False(result.Success);
        Assert.Contains("must be a whole number", result.Error);
    }

    [Fact]
    public async Task UnknownServiceType_Fails()
    {
        var result = await _tool.ExecuteAsync(Args("teleportation", new { }));

        Assert.False(result.Success);
        Assert.Contains("unknown service type 'teleportation'", result.Error);
    }

    [Fact]
    public async Task MissingServiceType_Fails()
    {
        var result = await _tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { parameters = new { } }));

        Assert.False(result.Success);
        Assert.Contains("serviceType", result.Error);
    }

    [Fact]
    public async Task NonObjectParameters_Fails()
    {
        var result = await _tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { serviceType = "passport", parameters = "nope" }));

        Assert.False(result.Success);
        Assert.Contains("parameters", result.Error);
    }
}