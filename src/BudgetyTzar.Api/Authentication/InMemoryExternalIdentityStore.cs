namespace BudgetyTzar.Api.Authentication;

public sealed class InMemoryExternalIdentityStore
{
    private readonly Dictionary<ExternalUserIdentity, ApplicationUserId> userIdsByExternalIdentity = [];
    private readonly object syncRoot = new();

    public ApplicationUserId GetOrCreateUserId(ExternalUserIdentity externalIdentity)
    {
        lock (syncRoot)
        {
            if (userIdsByExternalIdentity.TryGetValue(externalIdentity, out var userId))
            {
                return userId;
            }

            userId = ApplicationUserId.New();
            userIdsByExternalIdentity[externalIdentity] = userId;
            return userId;
        }
    }
}
