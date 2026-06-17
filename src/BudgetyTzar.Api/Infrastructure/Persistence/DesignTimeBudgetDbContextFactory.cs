using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BudgetyTzar.Api.Infrastructure.Persistence;

public sealed class DesignTimeBudgetDbContextFactory : IDesignTimeDbContextFactory<BudgetDbContext>
{
    public BudgetDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BudgetDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=budgetytzar;Username=budgetytzar;Password=budgetytzar");

        return new BudgetDbContext(optionsBuilder.Options);
    }
}
