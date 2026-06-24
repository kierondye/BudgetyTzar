using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Application.Transactions;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BudgetDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("BudgetyTzar")));
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.Converters.Add(new CamelCaseStringEnumConverter());
});
builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection("Kafka"));
builder.Services.Configure<KafkaTopicOptions>(builder.Configuration.GetSection("Kafka:TopicManagement"));
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
builder.Services.Configure<ProjectionOptions>(builder.Configuration.GetSection("Projections"));
builder.Services.Configure<EventTopicOptions>(options =>
{
    options.Budgeting = builder.Configuration["Kafka:Topics:BudgetingEvents"] ?? options.Budgeting;
    options.Transactions = builder.Configuration["Kafka:Topics:TransactionEvents"] ?? options.Transactions;
    options.Reporting = builder.Configuration["Kafka:Topics:ReportingEvents"] ?? options.Reporting;
});
builder.Services.AddScoped<AuditEventWriter>();
builder.Services.AddSingleton<EventSchemaValidator>();
builder.Services.AddScoped<ReportingProjectionService>();
builder.Services.AddSingleton<ReportingProjectionConsumerService>();
builder.Services.AddSingleton<ProjectionNotificationService>();
builder.Services.AddHostedService<KafkaTopicInitializerService>();
builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReportingProjectionConsumerService>());
builder.Services.AddScoped<BudgetItemEligibilityService>();
builder.Services.AddScoped<CreateBudgetHandler>();
builder.Services.AddScoped<CreateBudgetItemHandler>();
builder.Services.AddScoped<ArchiveBudgetItemHandler>();
builder.Services.AddScoped<RecordReallocationHandler>();
builder.Services.AddScoped<RecordAdjustmentHandler>();
builder.Services.AddScoped<CreateTransactionHandler>();
builder.Services.AddScoped<UpdateTransactionHandler>();
builder.Services.AddScoped<IgnoreTransactionHandler>();
builder.Services.AddScoped<ReplaceTransactionAllocationsHandler>();
builder.Services.AddScoped<ClearTransactionAllocationsHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = ProductVersion.ProductName,
        Version = ProductVersion.SemanticVersion
    });
    options.OrderActionsBy(apiDescription =>
        $"{apiDescription.RelativePath} {apiDescription.HttpMethod}");
    options.DocumentFilter<AlphabeticalPathsDocumentFilter>();
});

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
    await db.Database.ExecuteSqlRawAsync(
        """
        CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
            "MigrationId" character varying(150) NOT NULL,
            "ProductVersion" character varying(32) NOT NULL,
            CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
        );
        """);
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/version", (IHostEnvironment environment) => Results.Ok(new
{
    product = ProductVersion.ProductName,
    version = ProductVersion.SemanticVersion,
    informationalVersion = ProductVersion.InformationalVersion,
    buildMetadata = ProductVersion.BuildMetadata,
    environment = environment.EnvironmentName
}));

var api = app.MapGroup("/api");
api.MapBudgetEndpoints();

await app.RunAsync();

public partial class Program;

public sealed class AlphabeticalPathsDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        var sortedPaths = swaggerDoc.Paths
            .OrderBy(path => path.Key, StringComparer.Ordinal)
            .ToList();

        swaggerDoc.Paths.Clear();
        foreach (var path in sortedPaths)
        {
            swaggerDoc.Paths.Add(path.Key, path.Value);
        }
    }
}
