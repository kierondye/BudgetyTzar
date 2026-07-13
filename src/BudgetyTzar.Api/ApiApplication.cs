using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Identity;
using BudgetyTzar.Api.Features.Reporting;
using BudgetyTzar.Api.Features.Transactions;
using BudgetyTzar.Api.Observability;
using BudgetyTzar.Api.Persistence;
using Microsoft.OpenApi.Models;

namespace BudgetyTzar.Api;

public static class ApiApplication
{
    public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configureBuilder = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configureBuilder?.Invoke(builder);

        var version = RuntimeVersion.Current;

        builder.Services.AddHealthChecks();
        builder.Services.AddBudgetyTzarObservability(builder.Configuration);
        builder.Services.AddIdentityBoundary(builder.Configuration);
        builder.Services.AddBudgetyTzarPersistence(builder.Configuration);
        builder.Services.AddReporting();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "BudgetyTzar API",
                Version = version.ProductVersion
            });
            options.AddSecurityDefinition(IdentityBoundary.BearerSecuritySchemeName, new OpenApiSecurityScheme
            {
                Description = "JWT bearer token used by authenticated business API operations.",
                In = ParameterLocation.Header,
                Name = "Authorization",
                Scheme = "bearer",
                BearerFormat = "JWT",
                Type = SecuritySchemeType.Http
            });
            options.OperationFilter<RequireAuthorizationOperationFilter>();
        });

        var app = builder.Build();

        app.UseBudgetyTzarObservability();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks("/health");
        app.MapGet("/api/version", () => version)
            .WithName("GetVersion");
        app.MapBudgetEndpoints();
        app.MapTransactionEndpoints();
        app.MapReportingEndpoints();

        return app;
    }
}
