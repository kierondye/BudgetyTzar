using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Infrastructure.Events;

public sealed class AuditEventWriter(BudgetDbContext db)
{
    public void Add(DomainEvent domainEvent) =>
        db.AuditEvents.Add(new AuditEvent
        {
            BudgetId = domainEvent.BudgetId,
            BudgetPeriodId = domainEvent.BudgetPeriodId,
            AppliesToAllPeriods = domainEvent.AppliesToAllPeriods,
            EntityType = domainEvent.EntityType,
            EntityId = domainEvent.EntityId,
            EventType = domainEvent.EventType,
            Description = domainEvent.Description,
            Details = domainEvent.Details
        });

    public async Task AddImportBatchPeriodAudits(
        Guid budgetId,
        Guid batchId,
        string fileName,
        IReadOnlyCollection<TransactionImportRow> rows,
        string eventType,
        string verb,
        CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var firstDate = rows.Min(x => x.TransactionDate);
        var lastDate = rows.Max(x => x.TransactionDate);
        var periods = await db.BudgetPeriods
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && x.StartDate <= lastDate && x.EndDate >= firstDate)
            .ToListAsync(ct);

        foreach (var period in periods.OrderBy(x => x.StartDate))
        {
            var affectedRowCount = rows.Count(x => x.TransactionDate >= period.StartDate && x.TransactionDate <= period.EndDate);
            if (affectedRowCount == 0)
            {
                continue;
            }

            Add(new DomainEvent(
                eventType,
                budgetId,
                period.Id,
                nameof(TransactionImportBatch),
                batchId,
                $"{verb} import batch {fileName} with {affectedRowCount} row(s) affecting period {period.Name}."));
        }
    }
}
