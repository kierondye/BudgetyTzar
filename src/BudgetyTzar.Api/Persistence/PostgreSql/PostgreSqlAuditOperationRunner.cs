using BudgetyTzar.Api.Features.Audit;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class PostgreSqlAuditOperationRunner(ApplicationDbContext context, IAuditRecorder audit) : IAuditOperationRunner
{
    public T Execute<T>(Func<T> operation, Func<T, AuditEntry?> auditEntry)
    {
        using var transaction = context.Database.CurrentTransaction is null
            ? context.Database.BeginTransaction()
            : null;

        try
        {
            var result = operation();
            var entry = auditEntry(result);

            if (entry is null)
            {
                transaction?.Rollback();
                return result;
            }

            audit.Record(entry);
            transaction?.Commit();
            return result;
        }
        catch
        {
            transaction?.Rollback();
            throw;
        }
    }
}
