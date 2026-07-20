using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Features.Audit;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

internal static class PostgreSqlAuditRecords
{
    public static void AddAuditRecords(
        this ApplicationDbContext context,
        IEnumerable<AuditFact> auditFacts,
        Guid applicationUserId,
        IAuditRequestContext requestContext)
    {
        var facts = auditFacts.ToList();
        if (facts.Count == 0)
        {
            return;
        }

        var auditContext = new AuditRecordContext(
            applicationUserId,
            requestContext.OperationName,
            requestContext.CorrelationId,
            requestContext.PersistedAtUtc());

        context.AuditRecords.AddRange(facts.Select(fact => AuditRecord.From(fact, auditContext)));
    }
}

internal sealed class RepositoryAuditRequestContext : IAuditRequestContext
{
    public string OperationName { get; } = "repository";

    public string CorrelationId { get; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset PersistedAtUtc()
    {
        return DateTimeOffset.UtcNow;
    }
}
