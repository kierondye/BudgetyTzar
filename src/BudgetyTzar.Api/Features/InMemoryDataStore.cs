using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Api.Features;

public sealed class InMemoryDataStore
{
    internal object SyncRoot { get; } = new();

    internal Dictionary<Guid, Budget> BudgetsById { get; } = [];

    internal Dictionary<Guid, long> BudgetVersionsById { get; } = [];

    internal Dictionary<NormalizedName, Guid> BudgetIdsByName { get; } = [];

    internal List<Guid> BudgetIds { get; } = [];

    internal Dictionary<Guid, Transaction> TransactionsById { get; } = [];

    internal List<Guid> TransactionIds { get; } = [];

    internal Dictionary<Guid, TransactionAllocation> AllocationsByTransactionId { get; } = [];
}
