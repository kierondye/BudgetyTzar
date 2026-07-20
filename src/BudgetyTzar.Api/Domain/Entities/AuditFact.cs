using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Api.Domain.Entities;

public enum AuditAction
{
    BudgetCreated,
    BudgetRenamed,
    BudgetItemCreated,
    BudgetItemRenamed,
    BudgetItemPlannedAmountChanged,
    BudgetItemDeleted,
    TransactionCreated,
    TransactionDeleted,
    TransactionAllocationCreated,
    TransactionAllocationRemoved
}

public sealed record AuditFact
{
    private AuditFact(Guid id, AuditAction action, string? oldValue, string? newValue)
    {
        Id = id;
        Action = action;
        OldValue = oldValue;
        NewValue = newValue;
    }

    public Guid Id { get; }

    public AuditAction Action { get; }

    public string? OldValue { get; }

    public string? NewValue { get; }

    internal static AuditFact Create(AuditAction action, string? oldValue, string? newValue)
    {
        return new AuditFact(Guid.NewGuid(), action, oldValue, newValue);
    }
}

internal static class AuditValueSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static string Serialize(Budget budget)
    {
        return JsonSerializer.Serialize(budget, JsonOptions);
    }

    public static string Serialize(Transaction transaction)
    {
        return JsonSerializer.Serialize(transaction, JsonOptions);
    }

    public static string Serialize(TransactionAllocation allocation)
    {
        return JsonSerializer.Serialize(allocation, JsonOptions);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(RemoveExcludedProperties);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = resolver
        };
        options.Converters.Add(new StringValueJsonConverter<NormalizedName>(value => value.Value));
        options.Converters.Add(new StringValueJsonConverter<CurrencyCode>(value => value.Value));
        options.Converters.Add(new StringValueJsonConverter<BudgetItemKind>(value => value.Value));
        options.Converters.Add(new StringValueJsonConverter<TransactionType>(value => value.Value));
        options.Converters.Add(new StringValueJsonConverter<PositiveMoneyAmount>(value => value.FormattedValue));

        return options;
    }

    private static void RemoveExcludedProperties(JsonTypeInfo typeInfo)
    {
        for (var index = typeInfo.Properties.Count - 1; index >= 0; index--)
        {
            var property = typeInfo.Properties[index];
            var isAuditFacts = typeInfo.Type is not null
                && (typeInfo.Type == typeof(Budget)
                    || typeInfo.Type == typeof(Transaction)
                    || typeInfo.Type == typeof(TransactionAllocation))
                && string.Equals(property.Name, "auditFacts", StringComparison.OrdinalIgnoreCase);
            var isTransactionDescription = typeInfo.Type == typeof(Transaction)
                && string.Equals(property.Name, "description", StringComparison.OrdinalIgnoreCase);

            if (isAuditFacts || isTransactionDescription)
            {
                typeInfo.Properties.RemoveAt(index);
            }
        }
    }

    private sealed class StringValueJsonConverter<T>(Func<T, string> value) : JsonConverter<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException("Audit values are write-only.");
        }

        public override void Write(Utf8JsonWriter writer, T valueToWrite, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value(valueToWrite));
        }
    }
}
