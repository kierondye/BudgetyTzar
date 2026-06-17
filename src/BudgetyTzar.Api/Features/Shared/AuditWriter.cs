using BudgetyTzar.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static async Task AddImportBatchPeriodAudits(
        BudgetDbContext db,
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

            AddAudit(
                db,
                budgetId,
                period.Id,
                nameof(TransactionImportBatch),
                batchId,
                eventType,
                $"{verb} import batch {fileName} with {affectedRowCount} row(s) affecting period {period.Name}.");
        }
    }

    private static void AddAudit(
        BudgetDbContext db,
        Guid budgetId,
        Guid? periodId,
        string entityType,
        Guid entityId,
        string eventType,
        string description,
        string? details = null,
        bool appliesToAllPeriods = false) =>
        db.AuditEvents.Add(new AuditEvent
        {
            BudgetId = budgetId,
            BudgetPeriodId = periodId,
            AppliesToAllPeriods = appliesToAllPeriods,
            EntityType = entityType,
            EntityId = entityId,
            EventType = eventType,
            Description = description,
            Details = details
        });
}
