using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Identity;
using BudgetyTzar.Api.Features.Transactions;
using BudgetyTzar.Tests.Support.Persistence;

namespace BudgetyTzar.Tests.Persistence.InMemory;

internal sealed class InMemoryRepositoryContractContext : RepositoryContractContext
{
    private readonly InMemoryDataStore store = new();

    public override RepositorySet ForUser(string userId)
    {
        var currentUser = CurrentUser(userId);

        return new RepositorySet(
            new InMemoryBudgetRepository(store, currentUser),
            new InMemoryTransactionRepository(store, currentUser),
            new InMemoryTransactionAllocationRepository(store, currentUser));
    }

    private static CurrentUser CurrentUser(string value)
    {
        return ApplicationUserId.TryCreate(value, out var userId)
            ? new CurrentUser(userId!)
            : throw new InvalidOperationException("Invalid test user.");
    }
}
