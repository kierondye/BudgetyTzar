using BudgetyTzar.Api.Authentication;
using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Api.Features;

public sealed class InMemoryDataStore
{
    internal object SyncRoot { get; } = new();

    internal Dictionary<Guid, Budget> BudgetsById { get; } = [];

    internal Dictionary<Guid, ApplicationUserId> BudgetOwnersById { get; } = [];

    internal Dictionary<Guid, long> BudgetVersionsById { get; } = [];

    internal Dictionary<(ApplicationUserId UserId, NormalizedName Name), Guid> BudgetIdsByName { get; } = [];

    internal List<Guid> BudgetIds { get; } = [];

    internal Dictionary<Guid, Transaction> TransactionsById { get; } = [];

    internal Dictionary<Guid, ApplicationUserId> TransactionOwnersById { get; } = [];

    internal List<Guid> TransactionIds { get; } = [];

    internal Dictionary<Guid, TransactionAllocation> AllocationsByTransactionId { get; } = [];
}
