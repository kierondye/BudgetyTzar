using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Application.Reporting;

public sealed class ReportingProjectionService(
    BudgetDbContext db,
    ProjectionNotificationService notifications)
{
    private static readonly string[] UpdatedReadModels = ["snapshot", "auditTimeline"];

    public async Task ApplyEnvelope(EventEnvelope envelope, bool markOutboxProjected, CancellationToken ct)
    {
        if (await db.ProcessedProjectionEvents.AnyAsync(x => x.EventId == envelope.EventId, ct))
        {
            return;
        }

        var budgetId = GetGuid(envelope.Payload, "budgetId");
        var affectedDate = await ApplyProjectionState(envelope, budgetId, ct);
        await UpsertAuditTimeline(envelope, budgetId, ct);
        await db.SaveChangesAsync(ct);

        var projectedAt = DateTimeOffset.UtcNow;
        await RecalculateSnapshots(budgetId, affectedDate, DateOnly.FromDateTime(envelope.OccurredAt.UtcDateTime), ct);

        db.ProcessedProjectionEvents.Add(new ProcessedProjectionEvent
        {
            EventId = envelope.EventId,
            EventType = envelope.EventType,
            BudgetId = budgetId,
            OccurredAt = envelope.OccurredAt,
            ProcessedAt = projectedAt
        });

        if (markOutboxProjected)
        {
            await db.OutboxMessages
                .Where(x => x.Id == envelope.EventId && x.ProjectedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProjectedAt, projectedAt), ct);
        }

        await db.SaveChangesAsync(ct);
        notifications.Publish(new ProjectionReadyNotification(budgetId, envelope.EventId, envelope.EventType, projectedAt, UpdatedReadModels));
    }

    private async Task<DateOnly?> ApplyProjectionState(EventEnvelope envelope, Guid budgetId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        switch (envelope.EventType)
        {
            case "budgetytzar.budgeting.budget-created.v1":
                return DateOnly.FromDateTime(envelope.OccurredAt.UtcDateTime);

            case "budgetytzar.budgeting.budget-item-created.v1":
                await UpsertBudgetItemState(
                    GetGuid(envelope.Payload, "budgetItemId"),
                    budgetId,
                    GetString(envelope.Payload, "name"),
                    isArchived: false,
                    archivedAt: null,
                    now,
                    ct);
                return DateOnly.FromDateTime(envelope.OccurredAt.UtcDateTime);

            case "budgetytzar.budgeting.budget-item-archived.v1":
                await UpsertBudgetItemState(
                    GetGuid(envelope.Payload, "budgetItemId"),
                    budgetId,
                    GetString(envelope.Payload, "name"),
                    isArchived: true,
                    GetDateTimeOffset(envelope.Payload, "archivedAt"),
                    now,
                    ct);
                return null;

            case "budgetytzar.budgeting.budget-adjustment-recorded.v1":
                var adjustmentDate = GetDate(envelope.Payload, "date");
                await UpsertAdjustmentState(
                    GetGuid(envelope.Payload, "budgetAdjustmentId"),
                    budgetId,
                    GetGuid(envelope.Payload, "budgetItemId"),
                    envelope.EventId,
                    adjustmentDate,
                    GetDecimal(envelope.Payload, "amount"),
                    GetEnum<BudgetAdjustmentType>(envelope.Payload, "direction"),
                    now,
                    ct);
                return adjustmentDate;

            case "budgetytzar.budgeting.budget-reallocation-recorded.v1":
                var reallocationDate = GetDate(envelope.Payload, "date");
                await db.BudgetAdjustmentProjectionStates
                    .Where(x => x.SourceEventId == envelope.EventId)
                    .ExecuteDeleteAsync(ct);
                var ordinal = 0;
                foreach (var adjustment in GetArray(envelope.Payload, "adjustments"))
                {
                    var item = adjustment!.AsObject();
                    db.BudgetAdjustmentProjectionStates.Add(new BudgetAdjustmentProjectionState
                    {
                        ActivityId = DeterministicActivityId(envelope.EventId, ordinal++),
                        BudgetId = budgetId,
                        BudgetItemId = GetGuid(item, "budgetItemId"),
                        SourceEventId = envelope.EventId,
                        Date = reallocationDate,
                        Amount = GetDecimal(item, "amount"),
                        Direction = GetEnum<BudgetAdjustmentType>(item, "direction"),
                        UpdatedAt = now
                    });
                }
                return reallocationDate;

            case "budgetytzar.transactions.transaction-manually-created.v1":
            case "budgetytzar.transactions.transaction-edited.v1":
            case "budgetytzar.transactions.transaction-ignored.v1":
                var transactionId = GetGuid(envelope.Payload, "transactionId");
                var transactionDate = GetDate(envelope.Payload, "transactionDate");
                var existingTransaction = await db.TransactionProjectionStates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TransactionId == transactionId, ct);
                await UpsertTransactionState(transactionId, budgetId, transactionDate, now, envelope.Payload, ct);
                return existingTransaction is null
                    ? transactionDate
                    : Min(existingTransaction.TransactionDate, transactionDate);

            case "budgetytzar.transactions.transaction-allocations-replaced.v1":
                var replacedTransactionId = GetGuid(envelope.Payload, "transactionId");
                await ReplaceAllocationState(replacedTransactionId, budgetId, envelope.Payload, now, ct);
                return await GetTransactionDateOrOccurredDate(replacedTransactionId, envelope, ct);

            case "budgetytzar.transactions.transaction-allocations-cleared.v1":
                var clearedTransactionId = GetGuid(envelope.Payload, "transactionId");
                await db.TransactionAllocationProjectionStates
                    .Where(x => x.TransactionId == clearedTransactionId)
                    .ExecuteDeleteAsync(ct);
                return await GetTransactionDateOrOccurredDate(clearedTransactionId, envelope, ct);

            default:
                throw new PermanentProjectionException($"No reporting projector exists for event type '{envelope.EventType}'.");
        }
    }

    private async Task UpsertBudgetItemState(
        Guid budgetItemId,
        Guid budgetId,
        string name,
        bool isArchived,
        DateTimeOffset? archivedAt,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var state = await db.BudgetItemProjectionStates.FirstOrDefaultAsync(x => x.BudgetItemId == budgetItemId, ct);
        if (state is null)
        {
            db.BudgetItemProjectionStates.Add(new BudgetItemProjectionState
            {
                BudgetItemId = budgetItemId,
                BudgetId = budgetId,
                Name = name,
                IsArchived = isArchived,
                ArchivedAt = archivedAt,
                UpdatedAt = now
            });
            return;
        }

        state.Name = name;
        state.IsArchived = isArchived;
        state.ArchivedAt = archivedAt;
        state.UpdatedAt = now;
    }

    private async Task UpsertAdjustmentState(
        Guid activityId,
        Guid budgetId,
        Guid budgetItemId,
        Guid sourceEventId,
        DateOnly date,
        decimal amount,
        BudgetAdjustmentType direction,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var state = await db.BudgetAdjustmentProjectionStates.FirstOrDefaultAsync(x => x.ActivityId == activityId, ct);
        if (state is null)
        {
            db.BudgetAdjustmentProjectionStates.Add(new BudgetAdjustmentProjectionState
            {
                ActivityId = activityId,
                BudgetId = budgetId,
                BudgetItemId = budgetItemId,
                SourceEventId = sourceEventId,
                Date = date,
                Amount = amount,
                Direction = direction,
                UpdatedAt = now
            });
            return;
        }

        state.BudgetItemId = budgetItemId;
        state.Date = date;
        state.Amount = amount;
        state.Direction = direction;
        state.UpdatedAt = now;
    }

    private async Task UpsertTransactionState(Guid transactionId, Guid budgetId, DateOnly transactionDate, DateTimeOffset now, JsonObject payload, CancellationToken ct)
    {
        var state = await db.TransactionProjectionStates.FirstOrDefaultAsync(x => x.TransactionId == transactionId, ct);
        if (state is null)
        {
            db.TransactionProjectionStates.Add(new TransactionProjectionState
            {
                TransactionId = transactionId,
                BudgetId = budgetId,
                TransactionDate = transactionDate,
                Amount = GetDecimal(payload, "amount"),
                Direction = GetEnum<TransactionDirection>(payload, "direction"),
                IsIgnored = GetBool(payload, "isIgnored"),
                UpdatedAt = now
            });
            return;
        }

        state.TransactionDate = transactionDate;
        state.Amount = GetDecimal(payload, "amount");
        state.Direction = GetEnum<TransactionDirection>(payload, "direction");
        state.IsIgnored = GetBool(payload, "isIgnored");
        state.UpdatedAt = now;
    }

    private async Task ReplaceAllocationState(Guid transactionId, Guid budgetId, JsonObject payload, DateTimeOffset now, CancellationToken ct)
    {
        await db.TransactionAllocationProjectionStates
            .Where(x => x.TransactionId == transactionId)
            .ExecuteDeleteAsync(ct);

        db.TransactionAllocationProjectionStates.AddRange(GetArray(payload, "allocations")
            .Select(x => x!.AsObject())
            .Select(x => new TransactionAllocationProjectionState
            {
                TransactionId = transactionId,
                BudgetId = budgetId,
                BudgetItemId = GetGuid(x, "budgetItemId"),
                Amount = GetDecimal(x, "amount"),
                UpdatedAt = now
            }));
    }

    private async Task<DateOnly> GetTransactionDateOrOccurredDate(Guid transactionId, EventEnvelope envelope, CancellationToken ct)
    {
        var transactionDate = await db.TransactionProjectionStates
            .AsNoTracking()
            .Where(x => x.TransactionId == transactionId)
            .Select(x => (DateOnly?)x.TransactionDate)
            .FirstOrDefaultAsync(ct);
        return transactionDate ?? DateOnly.FromDateTime(envelope.OccurredAt.UtcDateTime);
    }

    private async Task UpsertAuditTimeline(EventEnvelope envelope, Guid budgetId, CancellationToken ct)
    {
        var auditEventId = GetGuid(envelope.Payload, "auditEventId");
        var existing = db.BudgetAuditTimelines.Local.FirstOrDefault(x => x.AuditEventId == auditEventId);
        if (existing is null)
        {
            var audit = await db.AuditEvents
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == auditEventId && x.BudgetId == budgetId, ct)
                ?? throw new PermanentProjectionException($"No durable audit event exists for audit event id '{auditEventId}'.");

            db.BudgetAuditTimelines.Add(new BudgetAuditTimelineProjection
            {
                AuditEventId = auditEventId,
                BudgetId = audit.BudgetId,
                OccurredAt = audit.OccurredAt,
                EventType = audit.EventType,
                EntityType = audit.EntityType,
                EntityId = audit.EntityId,
                Description = audit.Description,
                Details = audit.Details
            });
        }
    }

    private async Task RecalculateSnapshots(Guid budgetId, DateOnly? fromDate, DateOnly eventDate, CancellationToken ct)
    {
        var snapshotQuery = db.BudgetSnapshotProjections.Where(x => x.BudgetId == budgetId);
        if (fromDate.HasValue)
        {
            snapshotQuery = snapshotQuery.Where(x => x.Date >= fromDate.Value);
        }

        var snapshotIds = await snapshotQuery.Select(x => x.Id).ToListAsync(ct);
        await db.BudgetSnapshotItemProjections
            .Where(x => snapshotIds.Contains(x.SnapshotId))
            .ExecuteDeleteAsync(ct);
        await snapshotQuery.ExecuteDeleteAsync(ct);

        var dates = await GetSnapshotDates(budgetId, fromDate, eventDate, ct);
        foreach (var snapshot in await CalculateSnapshots(budgetId, dates, ct))
        {
            var projection = new BudgetSnapshotProjection
            {
                BudgetId = budgetId,
                Date = snapshot.Date,
                UnbudgetedBalance = snapshot.UnbudgetedBalance,
                TotalBalance = snapshot.TotalBalance,
                TotalTransactionBalance = snapshot.TotalTransactionBalance,
                TotalBudgetedBalance = snapshot.TotalBudgetedBalance
            };
            db.BudgetSnapshotProjections.Add(projection);
            db.BudgetSnapshotItemProjections.AddRange(snapshot.BudgetItems.Select(item => new BudgetSnapshotItemProjection
            {
                SnapshotId = projection.Id,
                BudgetId = budgetId,
                Date = snapshot.Date,
                BudgetItemId = item.BudgetItemId,
                Name = item.Name,
                Balance = item.Balance,
                PlannedCredit = item.PlannedCredit,
                PlannedDebit = item.PlannedDebit,
                ActualCredit = item.ActualCredit,
                ActualDebit = item.ActualDebit
            }));
        }
    }

    private async Task<IReadOnlyList<DateOnly>> GetSnapshotDates(Guid budgetId, DateOnly? fromDate, DateOnly eventDate, CancellationToken ct)
    {
        var adjustmentDates = await db.BudgetAdjustmentProjectionStates
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && (!fromDate.HasValue || x.Date >= fromDate.Value))
            .Select(x => x.Date)
            .ToListAsync(ct);
        var transactionDates = await db.TransactionProjectionStates
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && (!fromDate.HasValue || x.TransactionDate >= fromDate.Value))
            .Select(x => x.TransactionDate)
            .ToListAsync(ct);

        return adjustmentDates
            .Concat(transactionDates)
            .Append(eventDate)
            .Where(x => !fromDate.HasValue || x >= fromDate.Value)
            .Distinct()
            .Order()
            .ToList();
    }

    private async Task<IReadOnlyList<BudgetSnapshot>> CalculateSnapshots(Guid budgetId, IReadOnlyList<DateOnly> dates, CancellationToken ct)
    {
        var items = await db.BudgetItemProjectionStates
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        var adjustments = await db.BudgetAdjustmentProjectionStates
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .ToListAsync(ct);
        var transactions = await db.TransactionProjectionStates
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && !x.IsIgnored)
            .ToListAsync(ct);
        var transactionIds = transactions.Select(x => x.TransactionId).ToArray();
        var allocations = await db.TransactionAllocationProjectionStates
            .AsNoTracking()
            .Where(x => transactionIds.Contains(x.TransactionId))
            .ToListAsync(ct);
        var transactionsById = transactions.ToDictionary(x => x.TransactionId);

        return dates.Select(date => CalculateSnapshot(budgetId, date, items, adjustments, transactions, allocations, transactionsById)).ToList();
    }

    private static BudgetSnapshot CalculateSnapshot(
        Guid budgetId,
        DateOnly date,
        IReadOnlyList<BudgetItemProjectionState> items,
        IReadOnlyList<BudgetAdjustmentProjectionState> allAdjustments,
        IReadOnlyList<TransactionProjectionState> allTransactions,
        IReadOnlyList<TransactionAllocationProjectionState> allAllocations,
        IReadOnlyDictionary<Guid, TransactionProjectionState> transactionsById)
    {
        var adjustments = allAdjustments.Where(x => x.Date <= date).ToList();
        var transactions = allTransactions.Where(x => x.TransactionDate <= date).ToList();
        var transactionIds = transactions.Select(x => x.TransactionId).ToHashSet();
        var allocations = allAllocations.Where(x => transactionIds.Contains(x.TransactionId)).ToList();

        var calculatedItems = items
            .Select(item =>
            {
                var itemAdjustments = adjustments.Where(x => x.BudgetItemId == item.BudgetItemId).ToList();
                var itemAllocations = allocations.Where(x => x.BudgetItemId == item.BudgetItemId).ToList();
                var plannedCredit = itemAdjustments.Where(x => x.Direction == BudgetAdjustmentType.Credit).Sum(x => x.Amount);
                var plannedDebit = itemAdjustments.Where(x => x.Direction == BudgetAdjustmentType.Debit).Sum(x => x.Amount);
                var actualCredit = itemAllocations.Where(x => transactionsById[x.TransactionId].Direction == TransactionDirection.Credit).Sum(x => x.Amount);
                var actualDebit = itemAllocations.Where(x => transactionsById[x.TransactionId].Direction == TransactionDirection.Debit).Sum(x => x.Amount);
                var balance = actualCredit - plannedCredit + plannedDebit - actualDebit;

                return new
                {
                    item.BudgetItemId,
                    item.Name,
                    Balance = balance,
                    PlannedCredit = plannedCredit,
                    PlannedDebit = plannedDebit,
                    ActualCredit = actualCredit,
                    ActualDebit = actualDebit,
                    item.IsArchived
                };
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
            var latestTransactionDate = transactions.Max(x => x.TransactionDate);
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
            budgetId,
            date,
            unbudgetedBalance,
            totalBudgetedBalance + unbudgetedBalance,
            totalTransactionBalance,
            totalBudgetedBalance,
            calculatedItems.Select(x => new BudgetSnapshotItem(
                x.BudgetItemId,
                x.Name,
                x.Balance,
                x.PlannedCredit,
                x.PlannedDebit,
                x.ActualCredit,
                x.ActualDebit)).ToList());
    }

    private static Guid DeterministicActivityId(Guid eventId, int ordinal)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{eventId:N}:{ordinal.ToString(CultureInfo.InvariantCulture)}"));
        return new Guid(hash[..16]);
    }

    private static DateOnly Min(DateOnly left, DateOnly right) => left <= right ? left : right;

    private static JsonArray GetArray(JsonObject payload, string property) =>
        payload[property]?.AsArray()
        ?? throw new PermanentProjectionException($"Event payload is missing array property '{property}'.");

    private static Guid GetGuid(JsonObject payload, string property) =>
        payload[property]?.GetValue<Guid>()
        ?? throw new PermanentProjectionException($"Event payload is missing GUID property '{property}'.");

    private static string GetString(JsonObject payload, string property) =>
        payload[property]?.GetValue<string>()
        ?? throw new PermanentProjectionException($"Event payload is missing string property '{property}'.");

    private static string? GetNullableString(JsonObject payload, string property) =>
        payload[property]?.GetValue<string>();

    private static decimal GetDecimal(JsonObject payload, string property) =>
        payload[property]?.GetValue<decimal>()
        ?? throw new PermanentProjectionException($"Event payload is missing decimal property '{property}'.");

    private static bool GetBool(JsonObject payload, string property) =>
        payload[property]?.GetValue<bool>()
        ?? throw new PermanentProjectionException($"Event payload is missing boolean property '{property}'.");

    private static DateOnly GetDate(JsonObject payload, string property) =>
        DateOnly.Parse(GetString(payload, property), CultureInfo.InvariantCulture);

    private static DateTimeOffset GetDateTimeOffset(JsonObject payload, string property) =>
        DateTimeOffset.Parse(GetString(payload, property), CultureInfo.InvariantCulture);

    private static TEnum GetEnum<TEnum>(JsonObject payload, string property)
        where TEnum : struct =>
        Enum.Parse<TEnum>(GetString(payload, property), ignoreCase: true);
}
