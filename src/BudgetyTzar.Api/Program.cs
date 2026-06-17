using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Application.Transactions;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BudgetDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("BudgetyTzar")));
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddScoped<AuditEventWriter>();
builder.Services.AddScoped<BudgetLineEligibilityService>();
builder.Services.AddScoped<CreateBudgetHandler>();
builder.Services.AddScoped<CreateBudgetPeriodHandler>();
builder.Services.AddScoped<CreateBudgetLineHandler>();
builder.Services.AddScoped<ArchiveBudgetLineHandler>();
builder.Services.AddScoped<ReplaceAllocationsHandler>();
builder.Services.AddScoped<RecordReallocationHandler>();
builder.Services.AddScoped<RecordAdjustmentHandler>();
builder.Services.AddScoped<PreviewTransactionImportHandler>();
builder.Services.AddScoped<CommitTransactionImportHandler>();
builder.Services.AddScoped<CreateTransactionHandler>();
builder.Services.AddScoped<UpdateTransactionHandler>();
builder.Services.AddScoped<IgnoreTransactionHandler>();
builder.Services.AddScoped<ReplaceTransactionAssignmentsHandler>();
builder.Services.AddScoped<ClearTransactionAssignmentsHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api");
api.MapBudgetEndpoints();

await app.RunAsync();

public partial class Program;
