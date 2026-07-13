using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Transactions;

namespace BudgetyTzar.Tests.Support.Persistence;

public abstract class RepositoryContractContext : IAsyncDisposable
{
    public abstract RepositorySet ForUser(string userId);

    public virtual ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

public sealed record RepositorySet(
    IBudgetRepository Budgets,
    ITransactionRepository Transactions,
    ITransactionAllocationRepository Allocations);
