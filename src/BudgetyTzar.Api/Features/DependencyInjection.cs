using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Application.Transactions;
using FluentValidation;

namespace BudgetyTzar.Api.Features;

public static class DependencyInjection
{
    public static IServiceCollection AddBudgetyTzarFeatures(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<Program>();
        services.AddBudgetingFeature();
        services.AddTransactionsFeature();
        services.AddReportingFeature();
        services.AddAuditFeature();
        return services;
    }

    private static IServiceCollection AddBudgetingFeature(this IServiceCollection services)
    {
        services.AddScoped<BudgetItemEligibilityService>();
        services.AddScoped<CreateBudgetHandler>();
        services.AddScoped<CreateBudgetItemHandler>();
        services.AddScoped<ArchiveBudgetItemHandler>();
        services.AddScoped<RecordReallocationHandler>();
        services.AddScoped<RecordAdjustmentHandler>();
        return services;
    }

    private static IServiceCollection AddTransactionsFeature(this IServiceCollection services)
    {
        services.AddScoped<CreateTransactionHandler>();
        services.AddScoped<UpdateTransactionHandler>();
        services.AddScoped<IgnoreTransactionHandler>();
        services.AddScoped<ReplaceTransactionAllocationsHandler>();
        services.AddScoped<ClearTransactionAllocationsHandler>();
        return services;
    }

    private static IServiceCollection AddReportingFeature(this IServiceCollection services)
    {
        services.AddScoped<ReportingProjectionService>();
        return services;
    }

    private static IServiceCollection AddAuditFeature(this IServiceCollection services)
    {
        services.AddScoped<AuditEventProjectionService>();
        return services;
    }
}
