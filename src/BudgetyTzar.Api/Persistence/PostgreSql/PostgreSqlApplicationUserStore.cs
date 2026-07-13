using BudgetyTzar.Api.Features.Identity;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class PostgreSqlApplicationUserStore(BudgetyTzarDbContext context) : IApplicationUserStore
{
    private const string UserKeyConstraint = "ux_application_users_user_key";

    public ApplicationUserId GetOrCreateApplicationUserId(ApplicationUserKey userKey)
    {
        var existingUserId = FindApplicationUserId(userKey);
        if (existingUserId is not null)
        {
            return existingUserId;
        }

        var applicationUserId = ApplicationUserId.New();
        context.ApplicationUsers.Add(new ApplicationUserRecord
        {
            ApplicationUserId = applicationUserId.Value,
            UserKey = userKey.Value
        });

        try
        {
            context.SaveChanges();
            context.ChangeTracker.Clear();
            return applicationUserId;
        }
        catch (DbUpdateException exception) when (IsConstraint(exception, UserKeyConstraint))
        {
            context.ChangeTracker.Clear();
            return FindApplicationUserId(userKey)
                ?? throw new InvalidOperationException("Application user lookup failed after a user key conflict.");
        }
    }

    private ApplicationUserId? FindApplicationUserId(ApplicationUserKey userKey)
    {
        var record = context.ApplicationUsers
            .AsNoTracking()
            .SingleOrDefault(user => user.UserKey == userKey.Value);

        if (record is null)
        {
            return null;
        }

        return ApplicationUserId.TryCreate(record.ApplicationUserId, out var userId)
            ? userId
            : throw new InvalidOperationException("Stored application user identity is invalid.");
    }

    private static bool IsConstraint(DbUpdateException exception, string constraintName)
    {
        return exception.InnerException is Npgsql.PostgresException postgresException
            && postgresException.ConstraintName == constraintName;
    }
}
