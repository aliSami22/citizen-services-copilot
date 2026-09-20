using System.Text.Json;

namespace CitizenServicesCopilot.Application.Common.Interfaces;

public interface IToolSchemaValidator
{
    void Validate(string toolName, JsonElement args);
}