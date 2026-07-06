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

        budgets.MapPost("/{budgetId:guid}/budget-items", CreateBudgetItem)
            .WithName("CreateBudgetItem");

        budgets.MapGet("/{budgetId:guid}/budget-items", GetBudgetItems)
            .WithName("GetBudgetItems");

        budgets.MapGet("/{budgetId:guid}/budget-items/{budgetItemId:guid}", GetBudgetItem)
            .WithName("GetBudgetItem");

        return endpoints;
    }

    private static IResult CreateBudget(CreateBudgetRequest request, BudgetStore store)
    {
        var errors = Validate(request, out var currency);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = store.Create(request.Name.Trim(), currency);

        return result switch
        {
            CreateBudgetResult.DuplicateName => Results.Conflict(),
            CreateBudgetResult.Created created => Results.Created(
                $"/api/budgets/{created.Budget.BudgetId}",
                BudgetResponse.FromBudget(created.Budget)),
            _ => throw new InvalidOperationException("Unexpected create budget result.")
        };
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
        var errors = ValidateName(request.Name, "Budget name is required.");

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = store.Rename(budgetId, request.Name.Trim());

        return result switch
        {
            RenameBudgetResult.NotFound => Results.NotFound(),
            RenameBudgetResult.DuplicateName => Results.Conflict(),
            RenameBudgetResult.Renamed renamed => Results.Ok(BudgetResponse.FromBudget(renamed.Budget)),
            _ => throw new InvalidOperationException("Unexpected rename budget result.")
        };
    }

    private static IResult CreateBudgetItem(Guid budgetId, CreateBudgetItemRequest request, BudgetStore store)
    {
        var validation = Validate(request);

        if (validation is BudgetItemValidationResult.Invalid invalid)
        {
            return Results.ValidationProblem(invalid.Errors);
        }

        var valid = (BudgetItemValidationResult.Valid)validation;
        var result = store.AddBudgetItem(budgetId, request.Name.Trim(), valid.Kind, valid.PlannedAmount);

        return result switch
        {
            AddBudgetItemResult.NotFound => Results.NotFound(),
            AddBudgetItemResult.DuplicateName => Results.Conflict(),
            AddBudgetItemResult.Added added => Results.Created(
                $"/api/budgets/{budgetId}/budget-items/{added.BudgetItem.BudgetItemId}",
                BudgetItemResponse.FromBudgetItem(added.BudgetItem)),
            _ => throw new InvalidOperationException("Unexpected add budget item result.")
        };
    }

    private static IResult GetBudgetItems(Guid budgetId, BudgetStore store)
    {
        var budget = store.Get(budgetId);

        if (budget is null)
        {
            return Results.NotFound();
        }

        var budgetItems = budget.BudgetItems
            .Select(BudgetItemResponse.FromBudgetItem)
            .ToList();

        return Results.Ok(budgetItems);
    }

    private static IResult GetBudgetItem(Guid budgetId, Guid budgetItemId, BudgetStore store)
    {
        var budgetItem = store.GetBudgetItem(budgetId, budgetItemId);

        return budgetItem is null
            ? Results.NotFound()
            : Results.Ok(BudgetItemResponse.FromBudgetItem(budgetItem));
    }

    private static Dictionary<string, string[]> Validate(CreateBudgetRequest request, out CurrencyCode currency)
    {
        var errors = ValidateName(request.Name, "Budget name is required.");
        currency = CurrencyCode.Empty;

        if (!CurrencyCode.TryCreate(request.Currency, out currency))
        {
            errors["currency"] = ["Currency must be an uppercase ISO 4217 alphabetic code."];
        }

        return errors;
    }

    private static BudgetItemValidationResult Validate(CreateBudgetItemRequest request)
    {
        var errors = ValidateName(request.Name, "Budget item name is required.");
        var kind = BudgetItemKind.Empty;

        if (!BudgetItemKind.TryCreate(request.Kind, out kind))
        {
            errors["kind"] = ["Budget item kind must be Funding or Consumption."];
        }

        var hasPlannedAmount = PositiveMoneyAmount.TryCreate(request.PlannedAmount, out var plannedAmount);

        if (!hasPlannedAmount)
        {
            errors["plannedAmount"] = ["Planned amount must be greater than 0.00, no more than 99999999.99, and use exactly two decimal places."];
        }

        return errors.Count > 0
            ? new BudgetItemValidationResult.Invalid(errors)
            : new BudgetItemValidationResult.Valid(kind, plannedAmount!);
    }

    private static Dictionary<string, string[]> ValidateName(string name, string message)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = [message];
        }

        return errors;
    }

    private abstract record BudgetItemValidationResult
    {
        public sealed record Valid(BudgetItemKind Kind, PositiveMoneyAmount PlannedAmount) : BudgetItemValidationResult;

        public sealed record Invalid(Dictionary<string, string[]> Errors) : BudgetItemValidationResult;
    }
}
