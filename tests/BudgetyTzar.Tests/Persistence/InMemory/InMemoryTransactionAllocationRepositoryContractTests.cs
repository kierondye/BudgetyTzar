using BudgetyTzar.Tests.Support.Persistence;

namespace BudgetyTzar.Tests.Persistence.InMemory;

public sealed class InMemoryTransactionAllocationRepositoryContractTests : TransactionAllocationRepositoryContractTests
{
    protected override ValueTask<RepositoryContractContext> CreateContextAsync()
    {
        return ValueTask.FromResult<RepositoryContractContext>(new InMemoryRepositoryContractContext());
    }
}
