using System.Text.Json;

namespace BudgetyTzar.Api.Infrastructure.Events;

public static class EventSerialization
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
