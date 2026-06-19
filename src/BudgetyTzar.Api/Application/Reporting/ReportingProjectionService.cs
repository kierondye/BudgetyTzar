using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Application.Reporting;

public sealed class ReportingProjectionService(BudgetDbContext db)
{
    public async Task RebuildFromOutbox(CancellationToken ct)
    {
        db.BudgetSnapshotItemProjections.RemoveRange(db.BudgetSnapshotItemProjections);
        db.BudgetSnapshotProjections.RemoveRange(db.BudgetSnapshotProjections);
        db.BudgetAuditTimelines.RemoveRange(db.BudgetAuditTimelines);
        db.ProcessedProjectionEvents.RemoveRange(db.ProcessedProjectionEvents);
        await db.SaveChangesAsync(ct);

        var envelopes = await LoadOutboxEnvelopes(null, ct);
        var state = Replay(envelopes);
        PersistState(state);

        var projectedAt = DateTimeOffset.UtcNow;
        await db.OutboxMessages
            .Where(x => x.BudgetId != null && x.ProjectedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProjectedAt, projectedAt), ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task ProjectEnvelope(string envelopeJson, CancellationToken ct)
    {
        var envelope = DeserializeEnvelope(envelopeJson);
        if (envelope is null
            || await db.ProcessedProjectionEvents.AnyAsync(x => x.EventId == envelope.EventId, ct))
        {
            return;
        }

        var budgetId = TryGetGuid(envelope.Payload, "budgetId");
        if (budgetId.HasValue)
        {
            await RebuildBudget(budgetId.Value, envelope, ct);
        }

        db.ProcessedProjectionEvents.Add(new ProcessedProjectionEvent
        {
            EventId = envelope.EventId,
            EventType = envelope.EventType,
            BudgetId = budgetId,
            OccurredAt = envelope.OccurredAt
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task RebuildBudget(Guid budgetId, CancellationToken ct)
    {
        db.BudgetSnapshotItemProjections.RemoveRange(db.BudgetSnapshotItemProjections.Where(x => x.BudgetId == budgetId));
        db.BudgetSnapshotProjections.RemoveRange(db.BudgetSnapshotProjections.Where(x => x.BudgetId == budgetId));
        db.BudgetAuditTimelines.RemoveRange(db.BudgetAuditTimelines.Where(x => x.BudgetId == budgetId));
        await db.SaveChangesAsync(ct);

        var envelopes = await LoadOutboxEnvelopes(budgetId, ct);
        var state = Replay(envelopes);
        PersistState(state);

        var projectedAt = DateTimeOffset.UtcNow;
        await db.OutboxMessages
            .Where(x => x.BudgetId == budgetId && x.ProjectedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProjectedAt, projectedAt), ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task RebuildBudget(Guid budgetId, EventEnvelope additionalEnvelope, CancellationToken ct)
    {
        db.BudgetSnapshotItemProjections.RemoveRange(db.BudgetSnapshotItemProjections.Where(x => x.BudgetId == budgetId));
        db.BudgetSnapshotProjections.RemoveRange(db.BudgetSnapshotProjections.Where(x => x.BudgetId == budgetId));
        db.BudgetAuditTimelines.RemoveRange(db.BudgetAuditTimelines.Where(x => x.BudgetId == budgetId));
        await db.SaveChangesAsync(ct);

        var envelopes = (await LoadOutboxEnvelopes(budgetId, ct))
            .Append(additionalEnvelope)
            .GroupBy(x => x.EventId)
            .Select(x => x.First())
            .OrderBy(x => x.OccurredAt)
            .ToList();
        var state = Replay(envelopes);
        PersistState(state);

        var projectedAt = DateTimeOffset.UtcNow;
        await db.OutboxMessages
            .Where(x => x.BudgetId == budgetId && x.ProjectedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProjectedAt, projectedAt), ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<EventEnvelope>> LoadOutboxEnvelopes(Guid? budgetId, CancellationToken ct)
    {
        var query = db.OutboxMessages
            .AsNoTracking()
            .Where(x => x.BudgetId != null);
        if (budgetId.HasValue)
        {
            query = query.Where(x => x.BudgetId == budgetId.Value);
        }

        var messages = await query
            .Select(x => new { x.CreatedAt, x.EnvelopeJson })
            .ToListAsync(ct);

        return messages
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.EnvelopeJson)
            .Select(DeserializeEnvelope)
            .Where(x => x is not null)
            .Cast<EventEnvelope>()
            .ToList();
    }

    private static EventEnvelope? DeserializeEnvelope(string envelopeJson) =>
        JsonSerializer.Deserialize<EventEnvelope>(envelopeJson, EventSerialization.Options);

    private static ProjectionReplayState Replay(IEnumerable<EventEnvelope> envelopes)
    {
        var state = new ProjectionReplayState();
        foreach (var envelope in envelopes)
        {
            var budgetId = TryGetGuid(envelope.Payload, "budgetId");
            if (!budgetId.HasValue)
            {
                continue;
            }

            var budget = state.GetBudget(budgetId.Value);
            budget.SnapshotDates.Add(DateOnly.FromDateTime(envelope.OccurredAt.UtcDateTime));
            ProjectAuditTimeline(envelope, budget);

            switch (envelope.EventType)
            {
                case "budgetytzar.budgeting.budget-item-created.v1":
                    budget.BudgetItems[GetGuid(envelope.Payload, "budgetItemId")] = new ProjectedBudgetItem(
                        GetGuid(envelope.Payload, "budgetItemId"),
                        GetString(envelope.Payload, "name"),
                        false);
                    break;

                case "budgetytzar.budgeting.budget-item-archived.v1":
                    var archivedId = GetGuid(envelope.Payload, "budgetItemId");
                    var archivedName = GetString(envelope.Payload, "name");
                    budget.BudgetItems[archivedId] = budget.BudgetItems.TryGetValue(archivedId, out var existing)
                        ? existing with { IsArchived = true }
                        : new ProjectedBudgetItem(archivedId, archivedName, true);
                    break;

                case "budgetytzar.budgeting.budget-adjustment-recorded.v1":
                    budget.Adjustments.Add(new ProjectedAdjustment(
                        GetGuid(envelope.Payload, "budgetItemId"),
                        GetDecimal(envelope.Payload, "amount"),
                        GetEnum<BudgetAdjustmentType>(envelope.Payload, "direction"),
                        GetDate(envelope.Payload, "date")));
                    break;

                case "budgetytzar.budgeting.budget-reallocation-recorded.v1":
                    var date = GetDate(envelope.Payload, "date");
                    foreach (var adjustment in GetArray(envelope.Payload, "adjustments"))
                    {
                        var item = adjustment!.AsObject();
                        budget.Adjustments.Add(new ProjectedAdjustment(
                            GetGuid(item, "budgetItemId"),
                            GetDecimal(item, "amount"),
                            GetEnum<BudgetAdjustmentType>(item, "direction"),
                            date));
                    }

                    break;

                case "budgetytzar.transactions.transaction-manually-created.v1":
                case "budgetytzar.transactions.transaction-edited.v1":
                case "budgetytzar.transactions.transaction-ignored.v1":
                    budget.Transactions[GetGuid(envelope.Payload, "transactionId")] = new ProjectedTransaction(
                        GetGuid(envelope.Payload, "transactionId"),
                        GetDate(envelope.Payload, "transactionDate"),
                        GetDecimal(envelope.Payload, "amount"),
                        GetEnum<TransactionDirection>(envelope.Payload, "direction"),
                        GetBool(envelope.Payload, "isIgnored"));
                    break;

                case "budgetytzar.transactions.transaction-allocation-recorded.v1":
                case "budgetytzar.transactions.transaction-allocations-replaced.v1":
                    budget.AllocationsByTransaction[GetGuid(envelope.Payload, "transactionId")] =
                        GetArray(envelope.Payload, "allocations")
                            .Select(x => x!.AsObject())
                            .Select(x => new ProjectedAllocation(
                                GetGuid(x, "budgetItemId"),
                                GetDecimal(x, "amount")))
                            .ToList();
                    break;

                case "budgetytzar.transactions.transaction-allocations-cleared.v1":
                    budget.AllocationsByTransaction.Remove(GetGuid(envelope.Payload, "transactionId"));
                    break;
            }
        }

        return state;
    }

    private static void ProjectAuditTimeline(EventEnvelope envelope, ProjectedBudget budget)
    {
        var auditEventId = GetGuid(envelope.Payload, "auditEventId");
        budget.AuditTimeline[auditEventId] = new BudgetAuditTimelineProjection
        {
            AuditEventId = auditEventId,
            BudgetId = budget.BudgetId,
            OccurredAt = envelope.OccurredAt,
            EventType = GetString(envelope.Payload, "auditEventType"),
            EntityType = envelope.AggregateType,
            EntityId = envelope.AggregateId,
            Description = GetString(envelope.Payload, "auditDescription"),
            Details = GetNullableString(envelope.Payload, "auditDetails")
        };
    }

    private void PersistState(ProjectionReplayState state)
    {
        foreach (var budget in state.Budgets.Values)
        {
            db.BudgetAuditTimelines.AddRange(budget.AuditTimeline.Values);

            foreach (var snapshot in CalculateSnapshots(budget))
            {
                var projection = new BudgetSnapshotProjection
                {
                    BudgetId = budget.BudgetId,
                    Date = snapshot.Date,
                    UnbudgetedBalance = snapshot.UnbudgetedBalance,
                    TotalBalance = snapshot.TotalBalance
                };
                db.BudgetSnapshotProjections.Add(projection);
                db.BudgetSnapshotItemProjections.AddRange(snapshot.BudgetItems.Select(item => new BudgetSnapshotItemProjection
                {
                    SnapshotId = projection.Id,
                    BudgetId = budget.BudgetId,
                    Date = snapshot.Date,
                    BudgetItemId = item.BudgetItemId,
                    Name = item.Name,
                    Balance = item.Balance
                }));
            }
        }
    }

    private static IReadOnlyList<BudgetSnapshot> CalculateSnapshots(ProjectedBudget budget)
    {
        var dates = budget.Adjustments
            .Select(x => x.Date)
            .Concat(budget.Transactions.Values.Select(x => x.Date))
            .Concat(budget.SnapshotDates)
            .Distinct()
            .Order()
            .ToList();

        return dates
            .Select(date => CalculateSnapshot(budget, date))
            .ToList();
    }

    private static BudgetSnapshot CalculateSnapshot(ProjectedBudget budget, DateOnly date)
    {
        var adjustments = budget.Adjustments
            .Where(x => x.Date <= date)
            .ToList();
        var transactions = budget.Transactions.Values
            .Where(x => x.Date <= date && !x.IsIgnored)
            .ToList();
        var transactionsById = transactions.ToDictionary(x => x.Id);
        var allocations = budget.AllocationsByTransaction
            .Where(x => transactionsById.ContainsKey(x.Key))
            .SelectMany(x => x.Value.Select(allocation => new ProjectedTransactionAllocation(x.Key, allocation.BudgetItemId, allocation.Amount)))
            .ToList();

        var calculatedItems = budget.BudgetItems.Values
            .OrderBy(x => x.Name)
            .Select(item =>
            {
                var itemAdjustments = adjustments.Where(x => x.BudgetItemId == item.Id).ToList();
                var itemAllocations = allocations.Where(x => x.BudgetItemId == item.Id).ToList();
                var plannedCredit = itemAdjustments
                    .Where(x => x.Direction == BudgetAdjustmentType.Credit)
                    .Sum(x => x.Amount);
                var plannedDebit = itemAdjustments
                    .Where(x => x.Direction == BudgetAdjustmentType.Debit)
                    .Sum(x => x.Amount);
                var actualCredit = itemAllocations
                    .Where(x => transactionsById[x.TransactionId].Direction == TransactionDirection.Credit)
                    .Sum(x => x.Amount);
                var actualDebit = itemAllocations
                    .Where(x => transactionsById[x.TransactionId].Direction == TransactionDirection.Debit)
                    .Sum(x => x.Amount);
                var balance = actualCredit - plannedCredit + plannedDebit - actualDebit;

                return new ProjectedSnapshotItem(
                    item.Id,
                    item.Name,
                    balance,
                    plannedCredit,
                    plannedDebit,
                    actualCredit,
                    actualDebit,
                    item.IsArchived);
            })
            .Where(x => !x.IsArchived
                || x.Balance != 0
                || x.PlannedCredit != 0
                || x.PlannedDebit != 0
                || x.ActualCredit != 0
                || x.ActualDebit != 0)
            .ToList();

        var totalTransactionBalance = transactions.Sum(x => x.Direction == TransactionDirection.Credit ? x.Amount : -x.Amount);
        var totalBudgetedBalance = calculatedItems.Sum(x => x.Balance);
        var unbudgetedBalance = 0m;
        if (transactions.Count > 0)
        {
            var latestTransactionDate = transactions.Max(x => x.Date);
            var budgetedBalanceAtLatestTransaction = calculatedItems.Sum(item =>
            {
                var plannedCreditAtLatestTransaction = adjustments
                    .Where(x => x.BudgetItemId == item.BudgetItemId
                        && x.Date <= latestTransactionDate
                        && x.Direction == BudgetAdjustmentType.Credit)
                    .Sum(x => x.Amount);
                var plannedDebitAtLatestTransaction = adjustments
                    .Where(x => x.BudgetItemId == item.BudgetItemId
                        && x.Date <= latestTransactionDate
                        && x.Direction == BudgetAdjustmentType.Debit)
                    .Sum(x => x.Amount);

                return item.ActualCredit - plannedCreditAtLatestTransaction + plannedDebitAtLatestTransaction - item.ActualDebit;
            });
            unbudgetedBalance = totalTransactionBalance - budgetedBalanceAtLatestTransaction;
        }

        return new BudgetSnapshot(
            budget.BudgetId,
            date,
            unbudgetedBalance,
            totalBudgetedBalance + unbudgetedBalance,
            calculatedItems
                .Select(x => new BudgetSnapshotItem(x.BudgetItemId, x.Name, x.Balance))
                .ToList());
    }

    private static JsonArray GetArray(JsonObject payload, string property) =>
        payload[property]?.AsArray()
        ?? throw new InvalidOperationException($"Event payload is missing array property '{property}'.");

    private static Guid? TryGetGuid(JsonObject payload, string property) =>
        payload[property] is { } value ? value.GetValue<Guid>() : null;

    private static Guid GetGuid(JsonObject payload, string property) =>
        payload[property]?.GetValue<Guid>()
        ?? throw new InvalidOperationException($"Event payload is missing GUID property '{property}'.");

    private static string GetString(JsonObject payload, string property) =>
        payload[property]?.GetValue<string>()
        ?? throw new InvalidOperationException($"Event payload is missing string property '{property}'.");

    private static string? GetNullableString(JsonObject payload, string property) =>
        payload[property]?.GetValue<string>();

    private static decimal GetDecimal(JsonObject payload, string property) =>
        payload[property]?.GetValue<decimal>()
        ?? throw new InvalidOperationException($"Event payload is missing decimal property '{property}'.");

    private static bool GetBool(JsonObject payload, string property) =>
        payload[property]?.GetValue<bool>()
        ?? throw new InvalidOperationException($"Event payload is missing boolean property '{property}'.");

    private static DateOnly GetDate(JsonObject payload, string property) =>
        DateOnly.Parse(GetString(payload, property), CultureInfo.InvariantCulture);

    private static TEnum GetEnum<TEnum>(JsonObject payload, string property)
        where TEnum : struct =>
        Enum.Parse<TEnum>(GetString(payload, property), ignoreCase: true);

    private sealed class ProjectionReplayState
    {
        public Dictionary<Guid, ProjectedBudget> Budgets { get; } = [];

        public ProjectedBudget GetBudget(Guid budgetId)
        {
            if (!Budgets.TryGetValue(budgetId, out var budget))
            {
                budget = new ProjectedBudget(budgetId);
                Budgets.Add(budgetId, budget);
            }

            return budget;
        }
    }

    private sealed class ProjectedBudget(Guid budgetId)
    {
        public Guid BudgetId { get; } = budgetId;
        public HashSet<DateOnly> SnapshotDates { get; } = [];
        public Dictionary<Guid, ProjectedBudgetItem> BudgetItems { get; } = [];
        public List<ProjectedAdjustment> Adjustments { get; } = [];
        public Dictionary<Guid, ProjectedTransaction> Transactions { get; } = [];
        public Dictionary<Guid, List<ProjectedAllocation>> AllocationsByTransaction { get; } = [];
        public Dictionary<Guid, BudgetAuditTimelineProjection> AuditTimeline { get; } = [];
    }

    private sealed record ProjectedBudgetItem(Guid Id, string Name, bool IsArchived);
    private sealed record ProjectedAdjustment(Guid BudgetItemId, decimal Amount, BudgetAdjustmentType Direction, DateOnly Date);
    private sealed record ProjectedTransaction(Guid Id, DateOnly Date, decimal Amount, TransactionDirection Direction, bool IsIgnored);
    private sealed record ProjectedAllocation(Guid BudgetItemId, decimal Amount);
    private sealed record ProjectedTransactionAllocation(Guid TransactionId, Guid BudgetItemId, decimal Amount);
    private sealed record ProjectedSnapshotItem(
        Guid BudgetItemId,
        string Name,
        decimal Balance,
        decimal PlannedCredit,
        decimal PlannedDebit,
        decimal ActualCredit,
        decimal ActualDebit,
        bool IsArchived);
}
