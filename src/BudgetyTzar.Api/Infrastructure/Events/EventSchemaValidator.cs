using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace BudgetyTzar.Api.Infrastructure.Events;

public sealed class PermanentProjectionException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class EventSchemaValidator
{
    private readonly Lazy<JsonSchema> _envelopeSchema;
    private readonly Lazy<string> _contractsRoot;
    private readonly Dictionary<string, JsonSchema> _payloadSchemas = [];

    public EventSchemaValidator()
    {
        _contractsRoot = new Lazy<string>(FindContractsRoot);
        _envelopeSchema = new Lazy<JsonSchema>(() => LoadSchema(Path.Combine(_contractsRoot.Value, "event-envelope.schema.json")));
    }

    public EventEnvelope ValidateAndDeserialize(string envelopeJson)
    {
        JsonNode node;
        try
        {
            node = JsonNode.Parse(envelopeJson) ?? throw new PermanentProjectionException("Event envelope JSON is empty.");
        }
        catch (JsonException ex)
        {
            throw new PermanentProjectionException("Event envelope JSON is invalid.", ex);
        }

        ValidateSchema(_envelopeSchema.Value, node, "event envelope");

        EventEnvelope envelope;
        try
        {
            envelope = node.Deserialize<EventEnvelope>(EventSerialization.Options)
                ?? throw new PermanentProjectionException("Event envelope could not be deserialized.");
        }
        catch (JsonException ex)
        {
            throw new PermanentProjectionException("Event envelope could not be deserialized.", ex);
        }

        var payloadSchema = GetPayloadSchema(envelope.EventType);
        ValidateSchema(payloadSchema, envelope.Payload, envelope.EventType);
        return envelope;
    }

    private JsonSchema GetPayloadSchema(string eventType)
    {
        if (_payloadSchemas.TryGetValue(eventType, out var schema))
        {
            return schema;
        }

        var parts = eventType.Split('.');
        if (parts.Length != 4 || parts[0] != "budgetytzar")
        {
            throw new PermanentProjectionException($"Event type '{eventType}' is not a known BudgetyTzar contract.");
        }

        var schemaPath = Path.Combine(_contractsRoot.Value, parts[1], $"{parts[2]}.{parts[3]}.schema.json");
        if (!File.Exists(schemaPath))
        {
            throw new PermanentProjectionException($"No payload schema exists for event type '{eventType}'.");
        }

        schema = LoadSchema(schemaPath);
        _payloadSchemas[eventType] = schema;
        return schema;
    }

    private static JsonSchema LoadSchema(string path)
    {
        try
        {
            return JsonSchema.FromText(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            throw new PermanentProjectionException($"Schema '{path}' could not be loaded.", ex);
        }
    }

    private static void ValidateSchema(JsonSchema schema, JsonNode node, string context)
    {
        var result = schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (result.IsValid)
        {
            return;
        }

        var errors = result.Details
            .Where(x => x.HasErrors)
            .SelectMany(x => x.Errors!.Select(error => $"{x.InstanceLocation}: {error.Value}"))
            .ToArray();
        throw new PermanentProjectionException($"{context} failed schema validation: {string.Join("; ", errors)}");
    }

    private static string FindContractsRoot()
    {
        var outputContractsRoot = Path.Combine(AppContext.BaseDirectory, "contracts", "events");
        if (File.Exists(Path.Combine(outputContractsRoot, "event-envelope.schema.json")))
        {
            return outputContractsRoot;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BudgetyTzar.sln")))
        {
            directory = directory.Parent;
        }

        return directory is not null
            ? Path.Combine(directory.FullName, "contracts", "events")
            : throw new PermanentProjectionException("Could not find event contract schemas.");
    }
}
