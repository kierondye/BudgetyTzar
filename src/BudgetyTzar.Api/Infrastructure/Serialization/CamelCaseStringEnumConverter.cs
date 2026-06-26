using System.Text.Json;
using System.Text.Json.Serialization;

namespace BudgetyTzar.Api;

public sealed class CamelCaseStringEnumConverter : JsonStringEnumConverter
{
    public CamelCaseStringEnumConverter()
        : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    {
    }
}
