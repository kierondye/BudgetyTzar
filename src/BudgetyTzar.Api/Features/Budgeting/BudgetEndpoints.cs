using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace BudgetyTzar.Api.Features.Budgeting;

public static partial class BudgetEndpoints
{
    public static IServiceCollection AddBudgeting(this IServiceCollection services)
    {
        services.AddSingleton<BudgetStore>();
        return services;
    }

    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var budgets = endpoints.MapGroup("/api/budgets")
            .WithTags("Budgets");

        budgets.MapPost("/", CreateBudget)
            .WithName("CreateBudget");

        budgets.MapGet("/", GetBudgets)
            .WithName("GetBudgets");

        budgets.MapGet("/{budgetId:guid}", GetBudget)
            .WithName("GetBudget");

        return endpoints;
    }

    private static IResult CreateBudget(CreateBudgetRequest request, BudgetStore store)
    {
        var errors = Validate(request);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var budget = store.Create(request.Name.Trim(), request.Currency.Trim());
        var response = BudgetResponse.FromBudget(budget);

        return Results.Created($"/api/budgets/{budget.BudgetId}", response);
    }

    private static IResult GetBudgets(BudgetStore store)
    {
        return Results.Ok(store.GetAll().Select(BudgetListItemResponse.FromBudget));
    }

    private static IResult GetBudget(Guid budgetId, BudgetStore store)
    {
        var budget = store.Get(budgetId);

        return budget is null
            ? Results.NotFound()
            : Results.Ok(BudgetResponse.FromBudget(budget));
    }

    private static Dictionary<string, string[]> Validate(CreateBudgetRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Budget name is required."];
        }

        if (!CurrencyCodePattern().IsMatch(request.Currency ?? string.Empty))
        {
            errors["currency"] = ["Currency must be an uppercase ISO 4217 alphabetic code."];
        }

        return errors;
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCodePattern();
}

public sealed record CreateBudgetRequest(string Name, string Currency);

public sealed record BudgetResponse(Guid BudgetId, string Name, string Currency, IReadOnlyList<BudgetItemResponse> BudgetItems)
{
    public static BudgetResponse FromBudget(Budget budget)
    {
        return new BudgetResponse(budget.BudgetId, budget.Name, budget.Currency, []);
    }
}

public sealed record BudgetListItemResponse(Guid BudgetId, string Name, string Currency)
{
    public static BudgetListItemResponse FromBudget(Budget budget)
    {
        return new BudgetListItemResponse(budget.BudgetId, budget.Name, budget.Currency);
    }
}

public sealed record BudgetItemResponse(Guid BudgetItemId, string Name, string Kind, string PlannedAmount);

public sealed record Budget(Guid BudgetId, string Name, string Currency);

public sealed class BudgetStore
{
    private readonly object syncRoot = new();
    private readonly ConcurrentDictionary<Guid, Budget> budgetsById = new();
    private readonly List<Guid> budgetIds = [];

    public Budget Create(string name, string currency)
    {
        var budget = new Budget(Guid.NewGuid(), name, currency);

        lock (syncRoot)
        {
            budgetsById[budget.BudgetId] = budget;
            budgetIds.Add(budget.BudgetId);
        }

        return budget;
    }

    public IReadOnlyList<Budget> GetAll()
    {
        lock (syncRoot)
        {
            return budgetIds
                .Select(budgetId => budgetsById[budgetId])
                .ToList();
        }
    }

    public Budget? Get(Guid budgetId)
    {
        return budgetsById.GetValueOrDefault(budgetId);
    }
}
