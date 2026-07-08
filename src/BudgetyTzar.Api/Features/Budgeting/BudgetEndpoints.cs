using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
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

        if (Budget.Create(Guid.NewGuid(), request.Name, currency) is not CreateBudgetResult.Created created)
        {
            return Results.ValidationProblem(errors);
        }

        var budget = created.Budget;

        if (budgets.HasBudgetNamed(budget.Name))
        {
            return Results.Conflict();
        }

        return budgets.Save(budget) switch
        {
            BudgetSaveResult.Conflict => Results.Conflict(),
            BudgetSaveResult.Saved saved => Results.Created(
                $"/api/budgets/{saved.Budget.BudgetId}",
                BudgetResponse.FromBudget(saved.Budget)),
            _ => throw new InvalidOperationException("Unexpected save budget result.")
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
        var budgetState = budgets.Get(budgetId);

        return budgetState is null
            ? Results.NotFound()
            : Results.Ok(BudgetResponse.FromBudget(budgetState.Value));
    }

    private static IResult RenameBudget(Guid budgetId, RenameBudgetRequest request, InMemoryBudgetRepository budgets)
    {
        var errors = ValidateName(request.Name, "Budget name is required.");

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var budgetState = budgets.Get(budgetId);

        if (budgetState is null)
        {
            return Results.NotFound();
        }

        if (budgets.HasBudgetNamed(request.Name, budgetId))
        {
            return Results.Conflict();
        }

        if (budgetState.Value.Rename(request.Name) is not RenameBudgetResult.Renamed renamed)
        {
            return Results.ValidationProblem(errors);
        }

        return budgets.Save(budgetState.Update(renamed.Budget)) switch
        {
            BudgetSaveResult.Conflict => Results.Conflict(),
            BudgetSaveResult.NotFound => Results.NotFound(),
            BudgetSaveResult.Saved saved => Results.Ok(BudgetResponse.FromBudget(saved.Budget)),
            _ => throw new InvalidOperationException("Unexpected save budget result.")
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
        var budgetState = budgets.Get(budgetId);

        if (budgetState is null)
        {
            return Results.NotFound();
        }

        return budgetState.Value.AddBudgetItem(
            budgetItemId,
            valid.Name,
            valid.Kind,
            valid.PlannedAmount) switch
        {
            AddBudgetItemResult.DuplicateName => Results.Conflict(),
            AddBudgetItemResult.InvalidIdentity => Results.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["budgetItemId"] = ["Budget item identity is required."]
                }),
            AddBudgetItemResult.InvalidName => Results.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["name"] = ["Budget item name is required."]
                }),
            AddBudgetItemResult.Added added => budgets.Save(budgetState.Update(added.Budget)) switch
            {
                BudgetSaveResult.Conflict => Results.Conflict(),
                BudgetSaveResult.NotFound => Results.NotFound(),
                BudgetSaveResult.Saved => Results.Created(
                    $"/api/budgets/{budgetId}/budget-items/{budgetItemId}",
                    BudgetItemResponse.FromBudgetItem(added.BudgetItem)),
                _ => throw new InvalidOperationException("Unexpected save budget result.")
            },
            _ => throw new InvalidOperationException("Unexpected add budget item result.")
        };
    }

    private static IResult GetBudgetItems(Guid budgetId, InMemoryBudgetRepository budgets)
    {
        var budgetState = budgets.Get(budgetId);

        if (budgetState is null)
        {
            return Results.NotFound();
        }

        var budgetItems = budgetState.Value.BudgetItems
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
        var budgetState = budgets.Get(budgetId);

        if (budgetState is null)
        {
            return Results.NotFound();
        }

        return budgetState.Value.RenameBudgetItem(budgetItemId, valid.Name) switch
        {
            RenameBudgetItemResult.NotFound => Results.NotFound(),
            RenameBudgetItemResult.DuplicateName => Results.Conflict(),
            RenameBudgetItemResult.InvalidName => Results.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["name"] = ["Budget item name is required."]
                }),
            RenameBudgetItemResult.Renamed renamed => budgets.Save(budgetState.Update(renamed.Budget)) switch
            {
                BudgetSaveResult.Conflict => Results.Conflict(),
                BudgetSaveResult.NotFound => Results.NotFound(),
                BudgetSaveResult.Saved => Results.Ok(BudgetItemResponse.FromBudgetItem(renamed.BudgetItem)),
                _ => throw new InvalidOperationException("Unexpected save budget result.")
            },
            _ => throw new InvalidOperationException("Unexpected rename budget item result.")
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
        var budgetState = budgets.Get(budgetId);

        if (budgetState is null)
        {
            return Results.NotFound();
        }

        return budgetState.Value.ChangeBudgetItemPlannedAmount(budgetItemId, valid.PlannedAmount) switch
        {
            ChangeBudgetItemPlannedAmountResult.NotFound => Results.NotFound(),
            ChangeBudgetItemPlannedAmountResult.Changed changed => budgets.Save(budgetState.Update(changed.Budget)) switch
            {
                BudgetSaveResult.Conflict => Results.Conflict(),
                BudgetSaveResult.NotFound => Results.NotFound(),
                BudgetSaveResult.Saved => Results.Ok(BudgetItemResponse.FromBudgetItem(changed.BudgetItem)),
                _ => throw new InvalidOperationException("Unexpected save budget result.")
            },
            _ => throw new InvalidOperationException("Unexpected change budget item planned amount result.")
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

        var budgetState = budgets.Get(budgetId);

        if (budgetState is null)
        {
            return Results.NotFound();
        }

        return budgetState.Value.RemoveBudgetItem(budgetItemId) switch
        {
            RemoveBudgetItemResult.NotFound => Results.NotFound(),
            RemoveBudgetItemResult.Removed removed => budgets.Save(budgetState.Update(removed.Budget)) switch
            {
                BudgetSaveResult.Conflict => Results.Conflict(),
                BudgetSaveResult.NotFound => Results.NotFound(),
                BudgetSaveResult.Saved => Results.NoContent(),
                _ => throw new InvalidOperationException("Unexpected save budget result.")
            },
            _ => throw new InvalidOperationException("Unexpected remove budget item result.")
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
