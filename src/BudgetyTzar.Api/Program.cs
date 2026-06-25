using BudgetyTzar.Api;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Infrastructure;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.Converters.Add(new CamelCaseStringEnumConverter());
});
builder.Services.AddBudgetyTzarInfrastructure(builder.Configuration);
builder.Services.AddBudgetyTzarFeatures();
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
