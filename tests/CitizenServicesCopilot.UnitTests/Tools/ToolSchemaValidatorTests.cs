using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Exceptions;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services.Tools;
using Xunit;

namespace CitizenServicesCopilot.UnitTests.Tools;

public class ToolSchemaValidatorTests
{
    private static ToolSchema BuildSchema(bool allowAdditionalProperties = false)
        => new(
            ToolName: "compute_fee",
            Parameters: new[]
            {
                new ToolParameterSpec("category", JsonValueKind.String, IsRequired: true),
                new ToolParameterSpec("durationYears", JsonValueKind.Number, IsRequired: true),
                new ToolParameterSpec("discountCode", JsonValueKind.String, IsRequired: false)
            },
            AllowAdditionalProperties: allowAdditionalProperties);

    private static JsonElement Args(string json)
        => JsonDocument.Parse(json).RootElement;

    private readonly ToolSchemaValidator _validator = new(new[] { BuildSchema() });

    [Fact]
    public void Validate_ValidArgs_Passes()
    {
        var args = Args("""{ "category": "passport", "durationYears": 5 }""");

        _validator.Validate("compute_fee", args);
    }

    [Fact]
    public void Validate_ValidArgsWithOptionalField_Passes()
    {
        var args = Args("""{ "category": "passport", "durationYears": 5, "discountCode": "FAM10" }""");

        _validator.Validate("compute_fee", args);
    }

    [Fact]
    public void Validate_MissingRequiredField_Throws()
    {
        var args = Args("""{ "category": "passport" }""");

        var ex = Assert.Throws<ToolSchemaValidationException>(() => _validator.Validate("compute_fee", args));

        Assert.Contains("missing required field 'durationYears'", ex.Message);
        Assert.Equal("compute_fee", ex.ToolName);
    }

    [Fact]
    public void Validate_WrongType_Throws()
    {
        var args = Args("""{ "category": "passport", "durationYears": "five" }""");

        var ex = Assert.Throws<ToolSchemaValidationException>(() => _validator.Validate("compute_fee", args));

        Assert.Contains("field 'durationYears' must be of type Number", ex.Message);
    }

    [Fact]
    public void Validate_ExtraUnknownField_Throws()
    {
        var args = Args("""{ "category": "passport", "durationYears": 5, "hack": "dropDatabase" }""");

        var ex = Assert.Throws<ToolSchemaValidationException>(() => _validator.Validate("compute_fee", args));

        Assert.Contains("unexpected field 'hack'", ex.Message);
    }

    [Fact]
    public void Validate_ExtraUnknownField_AllowedWhenSchemaOptsIn()
    {
        var permissiveValidator = new ToolSchemaValidator(new[] { BuildSchema(allowAdditionalProperties: true) });
        var args = Args("""{ "category": "passport", "durationYears": 5, "extra": "ok" }""");

        permissiveValidator.Validate("compute_fee", args);
    }

    [Fact]
    public void Validate_UnregisteredTool_Throws()
    {
        var args = Args("""{ "category": "passport", "durationYears": 5 }""");

        var ex = Assert.Throws<ToolSchemaValidationException>(() => _validator.Validate("no_such_tool", args));

        Assert.Contains("no schema is registered", ex.Message);
    }

    [Fact]
    public void Validate_NonObjectArgs_Throws()
    {
        var args = Args("""[1, 2, 3]""");

        Assert.Throws<ToolSchemaValidationException>(() => _validator.Validate("compute_fee", args));
    }

    [Fact]
    public void Validate_NullArgs_Throws()
    {
        var args = Args("""null""");

        Assert.Throws<ToolSchemaValidationException>(() => _validator.Validate("compute_fee", args));
    }
}