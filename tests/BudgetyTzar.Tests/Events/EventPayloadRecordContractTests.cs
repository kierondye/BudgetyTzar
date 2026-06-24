using System.Text.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Contracts.Events;
using BudgetyTzar.Api.Infrastructure.Events;

namespace BudgetyTzar.Tests;

public sealed class EventPayloadRecordContractTests
{
    private static readonly Guid BudgetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BudgetItemId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SecondBudgetItemId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TransactionId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    public static TheoryData<string, object> PayloadSamples => new()
    {
        {
            "budgetytzar.budgeting.budget-created.v1",
            new BudgetCreatedPayload(
                BudgetId,
                "Household",
                "GBP")
        },
        {
            "budgetytzar.budgeting.budget-item-created.v1",
            new BudgetItemCreatedPayload(
                BudgetId,
                BudgetItemId,
                "Groceries")
        },
        {
            "budgetytzar.budgeting.budget-item-archived.v1",
            new BudgetItemArchivedPayload(
                BudgetId,
                BudgetItemId,
                "Old category",
                new DateTimeOffset(2026, 6, 15, 10, 30, 0, TimeSpan.Zero))
        },
        {
            "budgetytzar.budgeting.budget-adjustment-recorded.v1",
            new BudgetAdjustmentRecordedPayload(
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                BudgetId,
                BudgetItemId,
                25.50m,
                BudgetAdjustmentType.Credit,
                new DateOnly(2026, 6, 12),
                null)
        },
        {
            "budgetytzar.budgeting.budget-reallocation-recorded.v1",
            new BudgetReallocationRecordedPayload(
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                BudgetId,
                new DateOnly(2026, 6, 13),
                "Move budget",
                [
                    new BudgetReallocationAdjustmentPayload(BudgetItemId, 10m, BudgetAdjustmentType.Debit),
                    new BudgetReallocationAdjustmentPayload(SecondBudgetItemId, 10m, BudgetAdjustmentType.Credit)
                ])
        },
        {
            "budgetytzar.transactions.transaction-manually-created.v1",
            new TransactionManuallyCreatedPayload(
                TransactionId,
                BudgetId,
                new DateOnly(2026, 6, 14),
                "Manual transaction",
                12.50m,
                TransactionDirection.Debit,
                "Current",
                "manual-001",
                null,
                false)
        },
        {
            "budgetytzar.transactions.transaction-edited.v1",
            new TransactionEditedPayload(
                TransactionId,
                BudgetId,
                new DateOnly(2026, 6, 15),
                "Edited transaction",
                15.75m,
                TransactionDirection.Debit,
                null,
                "external-001",
                "Corrected amount",
                false)
        },
        {
            "budgetytzar.transactions.transaction-ignored.v1",
            new TransactionIgnoredPayload(
                TransactionId,
                BudgetId,
                new DateOnly(2026, 6, 16),
                "Ignored transaction",
                9.99m,
                TransactionDirection.Credit,
                "Savings",
                null,
                "Duplicate import",
                true)
        },
        {
            "budgetytzar.transactions.transaction-allocations-replaced.v1",
            new TransactionAllocationsReplacedPayload(
                TransactionId,
                BudgetId,
                35m,
                [
                    new TransactionAllocationPayload(BudgetItemId, 10m, null),
                    new TransactionAllocationPayload(SecondBudgetItemId, 15m, "Shared cost")
                ])
        },
        {
            "budgetytzar.transactions.transaction-allocations-cleared.v1",
            new TransactionAllocationsClearedPayload(
                TransactionId,
                BudgetId,
                [
                    new TransactionAllocationPayload(BudgetItemId, 10m, null),
                    new TransactionAllocationPayload(SecondBudgetItemId, 15m, "Removed split")
                ])
        }
    };

    [Theory]
    [MemberData(nameof(PayloadSamples))]
    public void EventPayloadRecordsSerializeToTheirJsonSchemas(string eventType, object payload)
    {
        var json = JsonSerializer.Serialize(payload, EventSerialization.Options);
        using var document = JsonDocument.Parse(json);
        using var schema = JsonDocument.Parse(File.ReadAllText(PayloadSchemaPath(FindRepoRoot(), eventType)));

        JsonSchemaTestAssertions.AssertElementMatchesSchema(schema.RootElement, document.RootElement, eventType);
    }

    private static string PayloadSchemaPath(string root, string eventType)
    {
        var parts = eventType.Split('.');
        Assert.Equal("budgetytzar", parts[0]);
        return Path.Combine(root, "contracts/events", parts[1], $"{parts[2]}.{parts[3]}.schema.json");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BudgetyTzar.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repo root.");
    }
}
