using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features.Identity;

namespace BudgetyTzar.Api.Features;

public sealed class InMemoryDataStore
{
    internal object SyncRoot { get; } = new();

    internal Dictionary<Guid, Budget> BudgetsById { get; } = [];

    internal Dictionary<Guid, ApplicationUserId> BudgetOwnersById { get; } = [];

    internal Dictionary<Guid, long> BudgetVersionsById { get; } = [];

    internal Dictionary<BudgetNameIndexKey, Guid> BudgetIdsByOwnerAndName { get; } = [];

    internal Dictionary<ApplicationUserId, List<Guid>> BudgetIdsByOwner { get; } = [];

    internal Dictionary<Guid, Transaction> TransactionsById { get; } = [];

    internal Dictionary<Guid, ApplicationUserId> TransactionOwnersById { get; } = [];

    internal Dictionary<ApplicationUserId, List<Guid>> TransactionIdsByOwner { get; } = [];

    internal Dictionary<Guid, TransactionAllocation> AllocationsByTransactionId { get; } = [];
}

internal sealed record BudgetNameIndexKey(ApplicationUserId OwnerId, NormalizedName Name);
