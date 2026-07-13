using System.Security.Cryptography;
using System.Text;
using BudgetyTzar.Api.Features.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class PostgreSqlApplicationUserStore(BudgetyTzarDbContext context)
{
    public Guid GetOrCreateApplicationUserId(ApplicationUserId userId)
    {
        Guid applicationUserId = CreateDeterministicStorageId(userId);
        string userKey = userId.Value;

        // The deterministic ID is still persisted because other tables enforce owner scope through FKs to application_users.
        context.Database.ExecuteSqlRaw(
            """
             insert into budgetytzar.application_users (application_user_id, user_key)
             values (@applicationUserId, @userKey)
             on conflict do nothing
             """,
            new NpgsqlParameter<Guid>("applicationUserId", applicationUserId),
            new NpgsqlParameter<string>("userKey", userKey));

        return applicationUserId;
    }

    private static Guid CreateDeterministicStorageId(ApplicationUserId userId)
    {
        var userKeyHash = HashUserKey(userId.Value);
        var guidBytes = TakeGuidBytes(userKeyHash);

        SetGuidVersionAndVariant(guidBytes);

        return new Guid(guidBytes);
    }

    private static byte[] HashUserKey(string userKey)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(userKey));
    }

    private static byte[] TakeGuidBytes(byte[] userKeyHash)
    {
        return userKeyHash[..16];
    }

    private static void SetGuidVersionAndVariant(byte[] guidBytes)
    {
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
    }
}
