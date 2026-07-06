using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Transactions;
using Microsoft.OpenApi.Models;

namespace BudgetyTzar.Api;

public static class ApiApplication
{
    public static WebApplication Create(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var version = RuntimeVersion.Current;

        builder.Services.AddHealthChecks();
        builder.Services.AddBudgeting();
        builder.Services.AddTransactions();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "BudgetyTzar API",
                Version = version.ProductVersion
            });
        });

        var app = builder.Build();

        app.UseSwagger();
        app.UseSwaggerUI();

        app.MapHealthChecks("/health");
        app.MapGet("/api/version", () => version)
            .WithName("GetVersion");
        app.MapBudgetEndpoints();
        app.MapTransactionEndpoints();

        return app;
    }
}
