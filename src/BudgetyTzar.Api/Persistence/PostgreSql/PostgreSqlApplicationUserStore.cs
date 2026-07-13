using System.Security.Cryptography;
using System.Text;
using BudgetyTzar.Api.Features.Identity;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class PostgreSqlApplicationUserStore(BudgetyTzarDbContext context)
{
    public Guid GetOrCreateApplicationUserId(ApplicationUserId userId)
    {
        var applicationUserId = CreateStorageId(userId);
        var userKey = userId.Value;

        context.Database.ExecuteSqlInterpolated(
            $"""
             insert into budgetytzar.application_users (application_user_id, user_key)
             values ({applicationUserId}, {userKey})
             on conflict do nothing
             """);

        return applicationUserId;
    }

    public static Guid CreateStorageId(ApplicationUserId userId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(userId.Value));
        var guidBytes = bytes[..16];

        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

        return new Guid(guidBytes);
    }
}
