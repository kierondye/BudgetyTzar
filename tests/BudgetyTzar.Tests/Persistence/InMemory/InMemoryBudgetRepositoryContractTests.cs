using BudgetyTzar.Tests.Support.Persistence;

namespace BudgetyTzar.Tests.Persistence.InMemory;

public sealed class InMemoryBudgetRepositoryContractTests : BudgetRepositoryContractTests
{
    protected override ValueTask<RepositoryContractContext> CreateContextAsync()
    {
        return ValueTask.FromResult<RepositoryContractContext>(new InMemoryRepositoryContractContext());
    }
}
