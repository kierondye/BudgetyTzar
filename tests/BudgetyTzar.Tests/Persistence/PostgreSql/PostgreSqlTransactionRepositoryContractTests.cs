using BudgetyTzar.Tests.Support.Persistence;

namespace BudgetyTzar.Tests.Persistence.PostgreSql;

public sealed class PostgreSqlTransactionRepositoryContractTests : TransactionRepositoryContractTests
{
    protected override async ValueTask<RepositoryContractContext> CreateContextAsync()
    {
        return await PostgreSqlRepositoryContractContext.CreateAsync();
    }
}
