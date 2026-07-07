using BudgetyTzar.Api.Features.Common;
using BudgetyTzar.Api.Features.Transactions;

namespace BudgetyTzar.Api.Features.Budgeting;

public static class BudgetEndpoints
{
    public static IServiceCollection AddBudgeting(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryBudgetRepository>();
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

        budgets.MapPut("/{budgetId:guid}/budget-items/{budgetItemId:guid}/name", RenameBudgetItem)
            .WithName("RenameBudgetItem");

        budgets.MapPut("/{budgetId:guid}/budget-items/{budgetItemId:guid}/planned-amount", ChangeBudgetItemPlannedAmount)
            .WithName("ChangeBudgetItemPlannedAmount");

        budgets.MapDelete("/{budgetId:guid}/budget-items/{budgetItemId:guid}", DeleteBudgetItem)
            .WithName("DeleteBudgetItem");

        return endpoints;
    }

    private static IResult CreateBudget(CreateBudgetRequest request, InMemoryBudgetRepository budgets)
    {
        var errors = Validate(request, out var currency);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var name = request.Name.Trim();

        var budget = Budget.Create(Guid.NewGuid(), name, currency);

        return budgets.Add(budget) switch
        {
            AddBudgetResult.DuplicateName => Results.Conflict(),
            AddBudgetResult.Added added => Results.Created(
                $"/api/budgets/{added.Budget.BudgetId}",
                BudgetResponse.FromBudget(added.Budget)),
            _ => throw new InvalidOperationException("Unexpected add budget result.")
        };
    }

    private static IResult GetBudgets(InMemoryBudgetRepository budgets)
    {
        var response = budgets.GetAll()
            .Select(BudgetListItemResponse.FromBudget)
            .ToList();

        return Results.Ok(response);
    }

    private static IResult GetBudget(Guid budgetId, InMemoryBudgetRepository budgets)
    {
        var budget = budgets.Get(budgetId);

        return budget is null
            ? Results.NotFound()
            : Results.Ok(BudgetResponse.FromBudget(budget));
    }

    private static IResult RenameBudget(Guid budgetId, RenameBudgetRequest request, InMemoryBudgetRepository budgets)
    {
        var errors = ValidateName(request.Name, "Budget name is required.");

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var name = request.Name.Trim();
        var result = budgets.TryUpdate(
            budgetId,
            (budget, allBudgets) =>
                allBudgets.Any(existingBudget =>
                    existingBudget.BudgetId != budgetId
                    && string.Equals(existingBudget.Name, name, StringComparison.Ordinal))
                    ? new BudgetUpdateResult.Conflict()
                    : new BudgetUpdateResult.Updated(budget.Rename(name)));

        return result switch
        {
            BudgetUpdateResult.NotFound => Results.NotFound(),
            BudgetUpdateResult.Conflict => Results.Conflict(),
            BudgetUpdateResult.Updated updated => Results.Ok(BudgetResponse.FromBudget(updated.Budget)),
            _ => throw new InvalidOperationException("Unexpected update budget result.")
        };
    }

    private static IResult CreateBudgetItem(Guid budgetId, CreateBudgetItemRequest request, InMemoryBudgetRepository budgets)
    {
        var validation = Validate(request);

        if (validation is BudgetItemValidationResult.Invalid invalid)
        {
            return Results.ValidationProblem(invalid.Errors);
        }

        var valid = (BudgetItemValidationResult.Valid)validation;
        var budgetItemId = Guid.NewGuid();
        var result = budgets.TryUpdate(
            budgetId,
            (budget, _) =>
                budget.HasBudgetItemNamed(valid.Name)
                    ? new BudgetUpdateResult.Conflict()
                    : new BudgetUpdateResult.Updated(budget.AddBudgetItem(
                        budgetItemId,
                        valid.Name,
                        valid.Kind,
                        valid.PlannedAmount)));

        return result switch
        {
            BudgetUpdateResult.NotFound => Results.NotFound(),
            BudgetUpdateResult.Conflict => Results.Conflict(),
            BudgetUpdateResult.Updated updated => Results.Created(
                $"/api/budgets/{budgetId}/budget-items/{budgetItemId}",
                BudgetItemResponse.FromBudgetItem(GetUpdatedBudgetItem(updated.Budget, budgetItemId, "Added"))),
            _ => throw new InvalidOperationException("Unexpected update budget result.")
        };
    }

    private static IResult GetBudgetItems(Guid budgetId, InMemoryBudgetRepository budgets)
    {
        var budget = budgets.Get(budgetId);

        if (budget is null)
        {
            return Results.NotFound();
        }

        var budgetItems = budget.BudgetItems
            .Select(BudgetItemResponse.FromBudgetItem)
            .ToList();

        return Results.Ok(budgetItems);
    }

    private static IResult GetBudgetItem(Guid budgetId, Guid budgetItemId, InMemoryBudgetRepository budgets)
    {
        var budgetItem = budgets.GetBudgetItem(budgetId, budgetItemId);

        return budgetItem is null
            ? Results.NotFound()
            : Results.Ok(BudgetItemResponse.FromBudgetItem(budgetItem));
    }

    private static IResult RenameBudgetItem(
        Guid budgetId,
        Guid budgetItemId,
        RenameBudgetItemRequest request,
        InMemoryBudgetRepository budgets)
    {
        var validation = Validate(request);

        if (validation is RenameBudgetItemValidationResult.Invalid invalid)
        {
            return Results.ValidationProblem(invalid.Errors);
        }

        var valid = (RenameBudgetItemValidationResult.Valid)validation;
        var result = budgets.TryUpdate(
            budgetId,
            (budget, _) =>
            {
                if (budget.GetBudgetItem(budgetItemId) is null)
                {
                    return new BudgetUpdateResult.NotFound();
                }

                if (budget.HasBudgetItemNamed(valid.Name, budgetItemId))
                {
                    return new BudgetUpdateResult.Conflict();
                }

                return new BudgetUpdateResult.Updated(budget.RenameBudgetItem(budgetItemId, valid.Name));
            });

        return result switch
        {
            BudgetUpdateResult.NotFound => Results.NotFound(),
            BudgetUpdateResult.Conflict => Results.Conflict(),
            BudgetUpdateResult.Updated updated => Results.Ok(BudgetItemResponse.FromBudgetItem(
                GetUpdatedBudgetItem(updated.Budget, budgetItemId, "Renamed"))),
            _ => throw new InvalidOperationException("Unexpected update budget result.")
        };
    }

    private static IResult ChangeBudgetItemPlannedAmount(
        Guid budgetId,
        Guid budgetItemId,
        ChangeBudgetItemPlannedAmountRequest request,
        InMemoryBudgetRepository budgets)
    {
        var validation = Validate(request);

        if (validation is BudgetItemPlannedAmountValidationResult.Invalid invalid)
        {
            return Results.ValidationProblem(invalid.Errors);
        }

        var valid = (BudgetItemPlannedAmountValidationResult.Valid)validation;
        var result = budgets.TryUpdate(
            budgetId,
            (budget, _) =>
                budget.GetBudgetItem(budgetItemId) is null
                    ? new BudgetUpdateResult.NotFound()
                    : new BudgetUpdateResult.Updated(budget.ChangeBudgetItemPlannedAmount(
                        budgetItemId,
                        valid.PlannedAmount)));

        return result switch
        {
            BudgetUpdateResult.NotFound => Results.NotFound(),
            BudgetUpdateResult.Updated updated => Results.Ok(BudgetItemResponse.FromBudgetItem(
                GetUpdatedBudgetItem(updated.Budget, budgetItemId, "Updated"))),
            _ => throw new InvalidOperationException("Unexpected update budget result.")
        };
    }

    private static IResult DeleteBudgetItem(
        Guid budgetId,
        Guid budgetItemId,
        InMemoryBudgetRepository budgets,
        InMemoryTransactionAllocationRepository allocationRepository)
    {
        if (allocationRepository.HasAllocationForBudgetItem(budgetItemId))
        {
            return Results.Conflict();
        }

        var result = budgets.TryUpdate(
            budgetId,
            (budget, _) =>
                budget.GetBudgetItem(budgetItemId) is null
                    ? new BudgetUpdateResult.NotFound()
                    : new BudgetUpdateResult.Updated(budget.RemoveBudgetItem(budgetItemId)));

        return result switch
        {
            BudgetUpdateResult.NotFound => Results.NotFound(),
            BudgetUpdateResult.Updated => Results.NoContent(),
            _ => throw new InvalidOperationException("Unexpected update budget result.")
        };
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
            : new BudgetItemValidationResult.Valid(request.Name.Trim(), kind, plannedAmount!);
    }

    private static RenameBudgetItemValidationResult Validate(RenameBudgetItemRequest request)
    {
        var errors = ValidateName(request.Name, "Budget item name is required.");

        return errors.Count > 0
            ? new RenameBudgetItemValidationResult.Invalid(errors)
            : new RenameBudgetItemValidationResult.Valid(request.Name.Trim());
    }

    private static BudgetItemPlannedAmountValidationResult Validate(ChangeBudgetItemPlannedAmountRequest request)
    {
        var hasPlannedAmount = PositiveMoneyAmount.TryCreate(request.PlannedAmount, out var plannedAmount);

        if (!hasPlannedAmount)
        {
            return new BudgetItemPlannedAmountValidationResult.Invalid(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["plannedAmount"] = ["Planned amount must be greater than 0.00, no more than 99999999.99, and use exactly two decimal places."]
                });
        }

        return new BudgetItemPlannedAmountValidationResult.Valid(plannedAmount!);
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

    private static BudgetItem GetUpdatedBudgetItem(Budget budget, Guid budgetItemId, string action)
    {
        return budget.GetBudgetItem(budgetItemId)
            ?? throw new InvalidOperationException($"{action} budget item was not found.");
    }

    private abstract record BudgetItemValidationResult
    {
        public sealed record Valid(string Name, BudgetItemKind Kind, PositiveMoneyAmount PlannedAmount) : BudgetItemValidationResult;

        public sealed record Invalid(Dictionary<string, string[]> Errors) : BudgetItemValidationResult;
    }

    private abstract record RenameBudgetItemValidationResult
    {
        public sealed record Valid(string Name) : RenameBudgetItemValidationResult;

        public sealed record Invalid(Dictionary<string, string[]> Errors) : RenameBudgetItemValidationResult;
    }

    private abstract record BudgetItemPlannedAmountValidationResult
    {
        public sealed record Valid(PositiveMoneyAmount PlannedAmount) : BudgetItemPlannedAmountValidationResult;

        public sealed record Invalid(Dictionary<string, string[]> Errors) : BudgetItemPlannedAmountValidationResult;
    }
}
