using System.Text.Json;
using System.Text.RegularExpressions;

namespace BudgetyTzar.Tests;

internal static class JsonSchemaTestAssertions
{
    public static void AssertRequiredProperties(JsonDocument schema, JsonDocument sample)
    {
        foreach (var property in schema.RootElement.GetProperty("required").EnumerateArray())
        {
            Assert.True(sample.RootElement.TryGetProperty(property.GetString()!, out _), $"Missing required property {property.GetString()}.");
        }
    }

    public static void AssertElementMatchesSchema(JsonElement schema, JsonElement element, string context)
    {
        foreach (var property in schema.GetProperty("required").EnumerateArray())
        {
            Assert.True(element.TryGetProperty(property.GetString()!, out _), $"{context}: missing required property {property.GetString()}.");
        }

        var properties = schema.GetProperty("properties");
        if (schema.TryGetProperty("additionalProperties", out var additionalProperties)
            && additionalProperties.ValueKind == JsonValueKind.False)
        {
            foreach (var property in element.EnumerateObject())
            {
                Assert.True(properties.TryGetProperty(property.Name, out _), $"{context}: unexpected property {property.Name}.");
            }
        }

        foreach (var property in properties.EnumerateObject())
        {
            if (!element.TryGetProperty(property.Name, out var value))
            {
                continue;
            }

            if (property.Value.TryGetProperty("const", out var constValue))
            {
                Assert.Equal(constValue.GetString(), value.GetString());
            }

            if (property.Value.TryGetProperty("type", out var type))
            {
                AssertSchemaType(type, value, $"{context}.{property.Name}");
            }

            if (property.Value.TryGetProperty("pattern", out var pattern))
            {
                Assert.Matches(new Regex(pattern.GetString()!), value.GetString()!);
            }

            if (property.Value.TryGetProperty("items", out var items) && value.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    AssertElementMatchesSchema(items, item, $"{context}.{property.Name}[{index}]");
                    index++;
                }
            }
        }
    }

    private static void AssertSchemaType(JsonElement type, JsonElement value, string context)
    {
        var allowedTypes = type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(x => x.GetString()!).ToArray()
            : [type.GetString()!];

        var actualType = value.ValueKind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            JsonValueKind.Null => "null",
            _ => value.ValueKind.ToString()
        };

        Assert.True(allowedTypes.Contains(actualType) || actualType == "number" && allowedTypes.Contains("integer"), $"{context}: expected {string.Join(" or ", allowedTypes)}, got {actualType}.");
    }
}
