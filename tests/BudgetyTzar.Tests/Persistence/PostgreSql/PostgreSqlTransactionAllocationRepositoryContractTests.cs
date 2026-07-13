using BudgetyTzar.Tests.Support.Persistence;

namespace BudgetyTzar.Tests.Persistence.PostgreSql;

public sealed class PostgreSqlTransactionAllocationRepositoryContractTests : TransactionAllocationRepositoryContractTests
{
    protected override async ValueTask<RepositoryContractContext> CreateContextAsync()
    {
        return await PostgreSqlRepositoryContractContext.CreateAsync();
    }
}
