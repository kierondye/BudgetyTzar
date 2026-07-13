using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

internal static class PostgreSqlPersistenceErrors
{
    public static bool IsUniqueViolation(DbUpdateException exception, string constraintName)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && postgresException.ConstraintName == constraintName;
    }

    public static bool IsForeignKeyViolation(DbUpdateException exception, string constraintName)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.ForeignKeyViolation
            && postgresException.ConstraintName == constraintName;
    }
}
