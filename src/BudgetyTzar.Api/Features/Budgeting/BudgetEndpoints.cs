namespace BudgetyTzar.Api.Features.Budgeting;

public static class BudgetEndpoints
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

        budgets.MapPut("/{budgetId:guid}/name", RenameBudget)
            .WithName("RenameBudget");

        return endpoints;
    }

    private static IResult CreateBudget(CreateBudgetRequest request, BudgetStore store)
    {
        var errors = Validate(request, out var currency);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var budget = store.Create(request.Name.Trim(), currency);
        var response = BudgetResponse.FromBudget(budget);

        return Results.Created($"/api/budgets/{budget.BudgetId}", response);
    }

    private static IResult GetBudgets(BudgetStore store)
    {
        var budgets = store.GetAll()
            .Select(BudgetListItemResponse.FromBudget)
            .ToList();

        return Results.Ok(budgets);
    }

    private static IResult GetBudget(Guid budgetId, BudgetStore store)
    {
        var budget = store.Get(budgetId);

        return budget is null
            ? Results.NotFound()
            : Results.Ok(BudgetResponse.FromBudget(budget));
    }

    private static IResult RenameBudget(Guid budgetId, RenameBudgetRequest request, BudgetStore store)
    {
        var errors = ValidateName(request.Name);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var budget = store.Rename(budgetId, request.Name.Trim());

        return budget is null
            ? Results.NotFound()
            : Results.Ok(BudgetResponse.FromBudget(budget));
    }

    private static Dictionary<string, string[]> Validate(CreateBudgetRequest request, out CurrencyCode currency)
    {
        var errors = ValidateName(request.Name);
        currency = CurrencyCode.Empty;

        if (!CurrencyCode.TryCreate(request.Currency, out currency))
        {
            errors["currency"] = ["Currency must be an uppercase ISO 4217 alphabetic code."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateName(string name)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["Budget name is required."];
        }

        return errors;
    }
}
