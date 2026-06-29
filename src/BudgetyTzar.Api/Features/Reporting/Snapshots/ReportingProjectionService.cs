using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BudgetyTzar.Api.Contracts.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Application.Reporting;

public sealed record ProjectionApplyResult(Guid BudgetId);

public sealed class ReportingProjectionService(BudgetDbContext db)
{
    public Task<ProjectionApplyResult> ApplyBudgetCreated(
        BudgetCreatedPayload payload,
        Guid eventId,
        string eventType,
        DateTimeOffset occurredAt,
        CancellationToken ct) =>
        ApplyProjectionState(payload.BudgetId, DateOnly.FromDateTime(occurredAt.UtcDateTime), occurredAt, ct);

    public async Task<ProjectionApplyResult> ApplyBudgetItemCreated(
        BudgetItemCreatedPayload payload,
        Guid eventId,
        string eventType,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await UpsertBudgetItemState(payload.BudgetItemId, payload.BudgetId, payload.Name, payload.Kind, isArchived: false, archivedAt: null, now, ct);
        return await ApplyProjectionState(payload.BudgetId, DateOnly.FromDateTime(occurredAt.UtcDateTime), occurredAt, ct);
    }

    public async Task<ProjectionApplyResult> ApplyBudgetItemArchived(
        BudgetItemArchivedPayload payload,
        Guid eventId,
        string eventType,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await UpsertBudgetItemState(payload.BudgetItemId, payload.BudgetId, payload.Name, payload.Kind, isArchived: true, payload.ArchivedAt, now, ct);
        return await ApplyProjectionState(payload.BudgetId, fromDate: null, occurredAt, ct);
    }

    public async Task<ProjectionApplyResult> ApplyBudgetAdjustmentRecorded(
        BudgetAdjustmentRecordedPayload payload,
        Guid eventId,
        string eventType,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await UpsertAdjustmentState(
            payload.BudgetAdjustmentId,
            payload.BudgetId,
            payload.BudgetItemId,
            eventId,
            payload.Date,
            payload.Amount,
            payload.Direction,
            now,
            ct);
        return await ApplyProjectionState(payload.BudgetId, payload.Date, occurredAt, ct);
    }

    public async Task<ProjectionApplyResult> ApplyBudgetReallocationRecorded(
        BudgetReallocationRecordedPayload payload,
        Guid eventId,
        string eventType,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db.BudgetAdjustmentProjectionStates
            .Where(x => x.SourceEventId == eventId)
            .ExecuteDeleteAsync(ct);

        var ordinal = 0;
        foreach (var adjustment in payload.Adjustments)
        {
            db.BudgetAdjustmentProjectionStates.Add(new BudgetAdjustmentProjectionState
            {
                ActivityId = DeterministicActivityId(eventId, ordinal++),
                BudgetId = payload.BudgetId,
                BudgetItemId = adjustment.BudgetItemId,
                SourceEventId = eventId,
                Date = payload.Date,
                Amount = adjustment.Amount,
                Direction = adjustment.Direction,
                UpdatedAt = now
            });
        }

        return await ApplyProjectionState(payload.BudgetId, payload.Date, occurredAt, ct);
    }

    public Task<ProjectionApplyResult> ApplyTransactionManuallyCreated(
        TransactionManuallyCreatedPayload payload,
        Guid eventId,
        string eventType,
        DateTimeOffset occurredAt,
        CancellationToken ct) =>
        ApplyTransactionState(
            payload.TransactionId,
            payload.BudgetId,
            payload.TransactionDate,
            payload.Amount,
            payload.Direction,
            payload.IsIgnored,
            occurredAt,
            ct);

    public Task<ProjectionApplyResult> ApplyTransactionEdited(
        TransactionEditedPayload payload,
        Guid eventId,
        string eventType,
        DateTimeOffset occurredAt,
        CancellationToken ct) =>
        ApplyTransactionState(
            payload.TransactionId,
            payload.BudgetId,
            payload.TransactionDate,
            payload.Amount,
            payload.Direction,
            payload.IsIgnored,
            occurredAt,
            ct);

    public Task<ProjectionApplyResult> ApplyTransactionIgnored(
        TransactionIgnoredPayload payload,
        Guid eventId,
        string eventType,
        DateTimeOffset occurredAt,
        CancellationToken ct) =>
        ApplyTransactionState(
            payload.TransactionId,
            payload.BudgetId,
            payload.TransactionDate,
            payload.Amount,
            payload.Direction,
            payload.IsIgnored,
            occurredAt,
            ct);

    public async Task<ProjectionApplyResult> ApplyTransactionAllocationsReplaced(
        TransactionAllocationsReplacedPayload payload,
        Guid eventId,
        string eventType,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await ReplaceAllocationState(payload.TransactionId, payload.BudgetId, payload.Allocations, now, ct);
        var affectedDate = await GetTransactionDateOrOccurredDate(payload.TransactionId, occurredAt, ct);
        return await ApplyProjectionState(payload.BudgetId, affectedDate, occurredAt, ct);
    }

    public async Task<ProjectionApplyResult> ApplyTransactionAllocationsCleared(
        TransactionAllocationsClearedPayload payload,
        Guid eventId,
        string eventType,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        await db.TransactionAllocationProjectionStates
            .Where(x => x.TransactionId == payload.TransactionId)
            .ExecuteDeleteAsync(ct);
        var affectedDate = await GetTransactionDateOrOccurredDate(payload.TransactionId, occurredAt, ct);
        return await ApplyProjectionState(payload.BudgetId, affectedDate, occurredAt, ct);
    }

    private async Task<ProjectionApplyResult> ApplyProjectionState(
        Guid budgetId,
        DateOnly? fromDate,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);

        await RecalculateSnapshots(budgetId, fromDate, DateOnly.FromDateTime(occurredAt.UtcDateTime), ct);
        await db.SaveChangesAsync(ct);
        return new ProjectionApplyResult(budgetId);
    }

    private async Task UpsertBudgetItemState(
        Guid budgetItemId,
        Guid budgetId,
        string name,
        BudgetItemKind kind,
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
                Kind = kind,
                IsArchived = isArchived,
                ArchivedAt = archivedAt,
                UpdatedAt = now
            });
            return;
        }

        state.Name = name;
        state.Kind = kind;
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

    private async Task<ProjectionApplyResult> ApplyTransactionState(
        Guid transactionId,
        Guid budgetId,
        DateOnly transactionDate,
        decimal amount,
        TransactionDirection direction,
        bool isIgnored,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var existingTransaction = await db.TransactionProjectionStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TransactionId == transactionId, ct);
        await UpsertTransactionState(transactionId, budgetId, transactionDate, amount, direction, isIgnored, now, ct);
        var affectedDate = existingTransaction is null
            ? transactionDate
            : Min(existingTransaction.TransactionDate, transactionDate);
        return await ApplyProjectionState(budgetId, affectedDate, occurredAt, ct);
    }

    private async Task UpsertTransactionState(
        Guid transactionId,
        Guid budgetId,
        DateOnly transactionDate,
        decimal amount,
        TransactionDirection direction,
        bool isIgnored,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var state = await db.TransactionProjectionStates.FirstOrDefaultAsync(x => x.TransactionId == transactionId, ct);
        if (state is null)
        {
            db.TransactionProjectionStates.Add(new TransactionProjectionState
            {
                TransactionId = transactionId,
                BudgetId = budgetId,
                TransactionDate = transactionDate,
                Amount = amount,
                Direction = direction,
                IsIgnored = isIgnored,
                UpdatedAt = now
            });
            return;
        }

        state.TransactionDate = transactionDate;
        state.Amount = amount;
        state.Direction = direction;
        state.IsIgnored = isIgnored;
        state.UpdatedAt = now;
    }

    private async Task ReplaceAllocationState(
        Guid transactionId,
        Guid budgetId,
        IReadOnlyList<TransactionAllocationPayload> allocations,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await db.TransactionAllocationProjectionStates
            .Where(x => x.TransactionId == transactionId)
            .ExecuteDeleteAsync(ct);

        db.TransactionAllocationProjectionStates.AddRange(allocations
            .Select(allocation => new TransactionAllocationProjectionState
            {
                TransactionId = transactionId,
                BudgetId = budgetId,
                BudgetItemId = allocation.BudgetItemId,
                Amount = allocation.Amount,
                UpdatedAt = now
            }));
    }

    private async Task<DateOnly> GetTransactionDateOrOccurredDate(Guid transactionId, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var transactionDate = await db.TransactionProjectionStates
            .AsNoTracking()
            .Where(x => x.TransactionId == transactionId)
            .Select(x => (DateOnly?)x.TransactionDate)
            .FirstOrDefaultAsync(ct);
        return transactionDate ?? DateOnly.FromDateTime(occurredAt.UtcDateTime);
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
                Kind = item.Kind,
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
                    item.Kind,
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
                x.Kind,
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

}
