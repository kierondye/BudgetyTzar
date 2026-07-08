using BudgetyTzar.Api.Authentication;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Reporting;
using BudgetyTzar.Api.Features.Transactions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;

namespace BudgetyTzar.Api;

public static class ApiApplication
{
    public const string BusinessApiPolicy = "BusinessApi";

    public static WebApplication Create(
        string[] args,
        Action<WebApplicationBuilder>? configureBuilder = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configureBuilder?.Invoke(builder);

        var version = RuntimeVersion.Current;
        var authentication = builder.Configuration
            .GetSection(AuthenticationOptions.SectionName)
            .Get<AuthenticationOptions>() ?? new AuthenticationOptions();

        builder.Services.AddHealthChecks();
        builder.Services.Configure<AuthenticationOptions>(
            builder.Configuration.GetSection(AuthenticationOptions.SectionName));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<AuthenticatedUser>();
        builder.Services.AddAuthentication(authentication.Scheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                builder.Configuration
                    .GetSection($"{AuthenticationOptions.SectionName}:JwtBearer")
                    .Bind(options);
                options.MapInboundClaims = false;
            });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(BusinessApiPolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(authentication.UserIdClaimType);
            });
        });
        builder.Services.AddBudgeting();
        builder.Services.AddTransactions();
        builder.Services.AddReporting();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "BudgetyTzar API",
                Version = version.ProductVersion
            });
            options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = JwtBearerDefaults.AuthenticationScheme.ToLowerInvariant(),
                BearerFormat = "JWT",
                Description = "A bearer token containing the configured stable user identity claim."
            });
            options.OperationFilter<AuthorizationOperationFilter>();
        });

        var app = builder.Build();

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
