namespace BudgetyTzar.Api.Features.Identity;

public interface IApplicationUserStore
{
    ApplicationUserId GetOrCreateApplicationUserId(ApplicationUserKey userKey);
}

public sealed class InMemoryApplicationUserStore : IApplicationUserStore
{
    private readonly object syncRoot = new();
    private readonly Dictionary<ApplicationUserKey, ApplicationUserId> userIdsByKey = [];

    public ApplicationUserId GetOrCreateApplicationUserId(ApplicationUserKey userKey)
    {
        lock (syncRoot)
        {
            if (!userIdsByKey.TryGetValue(userKey, out var userId))
            {
                userId = ApplicationUserId.New();
                userIdsByKey[userKey] = userId;
            }

            return userId;
        }
    }
}
