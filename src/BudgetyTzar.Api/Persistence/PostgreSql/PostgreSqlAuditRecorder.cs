using BudgetyTzar.Api.Features.Audit;
using BudgetyTzar.Api.Features.Identity;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class PostgreSqlAuditRecorder : IAuditRecorder
{
    private readonly ApplicationDbContext context;
    private readonly ApplicationUserId userId;

    public PostgreSqlAuditRecorder(ApplicationDbContext context, ICurrentUser currentUser)
    {
        this.context = context;
        userId = currentUser.UserId;
    }

    public void Record(AuditEntry entry)
    {
        var applicationUserId = userId.Value;

        context.AuditRecords.Add(new AuditRecord
        {
            AuditRecordId = Guid.NewGuid(),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            ApplicationUserId = applicationUserId,
            ActorApplicationUserId = applicationUserId,
            OperationName = entry.OperationName,
            ResourceType = entry.ResourceType,
            ResourceId = entry.ResourceId,
            BeforeState = entry.BeforeStateJson,
            AfterState = entry.AfterStateJson
        });

        context.SaveChanges();
    }
}
