using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Identity;
using BudgetyTzar.Api.Features.Transactions;
using BudgetyTzar.Tests.Support.Persistence;

namespace BudgetyTzar.Tests.Persistence.InMemory;

internal sealed class InMemoryRepositoryContractContext : RepositoryContractContext
{
    private readonly InMemoryDataStore store = new();
    private readonly InMemoryApplicationUserStore users = new();

    public override RepositorySet ForUser(string userId)
    {
        var currentUser = CurrentUser(userId);

        return new RepositorySet(
            new InMemoryBudgetRepository(store, currentUser),
            new InMemoryTransactionRepository(store, currentUser),
            new InMemoryTransactionAllocationRepository(store, currentUser));
    }

    private CurrentUser CurrentUser(string value)
    {
        return new CurrentUser(users.GetOrCreateApplicationUserId(UserKey(value)));
    }

    private static ApplicationUserKey UserKey(string value)
    {
        return ExternalIdentity.TryCreate("BudgetyTzar.Tests", value, out var externalIdentity)
            ? ApplicationUserKey.FromExternalIdentity(externalIdentity!)
            : throw new InvalidOperationException("Invalid test user.");
    }
}
