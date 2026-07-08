using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Features.Transactions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BudgetyTzar.Api.Features.Budgeting;

public static class BudgetEndpoints
{
    public static IServiceCollection AddBudgeting(this IServiceCollection services)
    {
        services.TryAddSingleton<InMemoryDataStore>();
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
        var validation = Validate(request);

        if (validation is CreateBudgetValidationResult.Invalid invalid)
        {
            return Results.ValidationProblem(invalid.Errors);
        }

        var valid = (CreateBudgetValidationResult.Valid)validation;

        return Budget.Create(Guid.NewGuid(), valid.Name, valid.Currency) switch
        {
            CreateBudgetResult.InvalidIdentity => Results.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["budgetId"] = ["Budget identity is required."]
                }),
            CreateBudgetResult.Created created => SaveNewBudget(created.Budget),
            _ => throw new InvalidOperationException("Unexpected create budget result.")
        };

        IResult SaveNewBudget(Budget budget)
        {
            if (budgets.HasBudgetNamed(budget.Name))
            {
                return BudgetNameAlreadyInUse();
            }

            return budgets.Save(budget) switch
            {
                BudgetSaveResult.DuplicateIdentity => BudgetIdentityAlreadyExists(),
                BudgetSaveResult.DuplicateName => BudgetNameAlreadyInUse(),
                BudgetSaveResult.StaleState => BudgetWasModified(),
                BudgetSaveResult.Saved saved => Results.Created(
                    $"/api/budgets/{saved.Budget.BudgetId}",
                    BudgetResponse.FromBudget(saved.Budget)),
                _ => throw new InvalidOperationException("Unexpected save budget result.")
            };
        }
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
        var validation = Validate(request);

        if (validation is RenameBudgetValidationResult.Invalid invalid)
        {
            return Results.ValidationProblem(invalid.Errors);
        }

        var valid = (RenameBudgetValidationResult.Valid)validation;
        var budgetState = budgets.Get(budgetId);

        if (budgetState is null)
        {
            return Results.NotFound();
        }

        if (budgets.HasBudgetNamed(valid.Name, budgetId))
        {
            return BudgetNameAlreadyInUse();
        }

        return budgetState.Value.Rename(valid.Name) switch
        {
            RenameBudgetResult.Renamed renamed => budgets.Save(budgetState.Update(renamed.Budget)) switch
            {
                BudgetSaveResult.DuplicateName => BudgetNameAlreadyInUse(),
                BudgetSaveResult.StaleState => BudgetWasModified(),
                BudgetSaveResult.NotFound => Results.NotFound(),
                BudgetSaveResult.Saved saved => Results.Ok(BudgetResponse.FromBudget(saved.Budget)),
                _ => throw new InvalidOperationException("Unexpected save budget result.")
            },
            _ => throw new InvalidOperationException("Unexpected rename budget result.")
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
            AddBudgetItemResult.DuplicateName => BudgetItemNameAlreadyInUse(),
            AddBudgetItemResult.InvalidIdentity => Results.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["budgetItemId"] = ["Budget item identity is required."]
                }),
            AddBudgetItemResult.Added added => budgets.Save(budgetState.Update(added.Budget)) switch
            {
                BudgetSaveResult.DuplicateName => BudgetNameAlreadyInUse(),
                BudgetSaveResult.StaleState => BudgetWasModified(),
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
            RenameBudgetItemResult.DuplicateName => BudgetItemNameAlreadyInUse(),
            RenameBudgetItemResult.Renamed renamed => budgets.Save(budgetState.Update(renamed.Budget)) switch
            {
                BudgetSaveResult.DuplicateName => BudgetNameAlreadyInUse(),
                BudgetSaveResult.StaleState => BudgetWasModified(),
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
                BudgetSaveResult.DuplicateName => BudgetNameAlreadyInUse(),
                BudgetSaveResult.StaleState => BudgetWasModified(),
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
        InMemoryBudgetRepository budgets)
    {
        var budgetState = budgets.Get(budgetId);

        if (budgetState is null)
        {
            return Results.NotFound();
        }

        return budgetState.Value.RemoveBudgetItem(budgetItemId) switch
        {
            RemoveBudgetItemResult.NotFound => Results.NotFound(),
            RemoveBudgetItemResult.Removed removed => budgets.SaveRemovalIfBudgetItemHasNoAllocations(
                budgetState.Update(removed.Budget),
                budgetItemId) switch
            {
                BudgetSaveResult.BudgetItemHasAllocations => BudgetItemHasAllocations(),
                BudgetSaveResult.DuplicateName => BudgetNameAlreadyInUse(),
                BudgetSaveResult.StaleState => BudgetWasModified(),
                BudgetSaveResult.NotFound => Results.NotFound(),
                BudgetSaveResult.Saved => Results.NoContent(),
                _ => throw new InvalidOperationException("Unexpected save budget result.")
            },
            _ => throw new InvalidOperationException("Unexpected remove budget item result.")
        };
    }

    private static CreateBudgetValidationResult Validate(CreateBudgetRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!NormalizedName.TryCreate(request.Name, out var name))
        {
            errors["name"] = ["Budget name is required."];
        }

        if (!CurrencyCode.TryCreate(request.Currency, out var currency))
        {
            errors["currency"] = ["Currency must be an uppercase ISO 4217 alphabetic code."];
        }

        return errors.Count > 0
            ? new CreateBudgetValidationResult.Invalid(errors)
            : new CreateBudgetValidationResult.Valid(name, currency);
    }

    private static RenameBudgetValidationResult Validate(RenameBudgetRequest request)
    {
        if (!NormalizedName.TryCreate(request.Name, out var name))
        {
            return new RenameBudgetValidationResult.Invalid(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["name"] = ["Budget name is required."]
                });
        }

        return new RenameBudgetValidationResult.Valid(name);
    }

    private static BudgetItemValidationResult Validate(CreateBudgetItemRequest request)
    {
        var kind = BudgetItemKind.Empty;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!NormalizedName.TryCreate(request.Name, out var name))
        {
            errors["name"] = ["Budget item name is required."];
        }

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
            : new BudgetItemValidationResult.Valid(name, kind, plannedAmount!);
    }

    private static RenameBudgetItemValidationResult Validate(RenameBudgetItemRequest request)
    {
        if (!NormalizedName.TryCreate(request.Name, out var name))
        {
            return new RenameBudgetItemValidationResult.Invalid(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["name"] = ["Budget item name is required."]
                });
        }

        return new RenameBudgetItemValidationResult.Valid(name);
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

    private static IResult BudgetNameAlreadyInUse()
    {
        return Results.Conflict(new ConflictResponse(
            "BudgetNameAlreadyInUse",
            "Budget name is already in use."));
    }

    private static IResult BudgetIdentityAlreadyExists()
    {
        return Results.Conflict(new ConflictResponse(
            "BudgetIdentityAlreadyExists",
            "Budget identity already exists."));
    }

    private static IResult BudgetWasModified()
    {
        return Results.Conflict(new ConflictResponse(
            "BudgetWasModified",
            "Budget was modified by another request."));
    }

    private static IResult BudgetItemNameAlreadyInUse()
    {
        return Results.Conflict(new ConflictResponse(
            "BudgetItemNameAlreadyInUse",
            "Budget item name is already in use."));
    }

    private static IResult BudgetItemHasAllocations()
    {
        return Results.Conflict(new ConflictResponse(
            "BudgetItemHasAllocations",
            "Budget item has transaction allocations."));
    }

    private sealed record ConflictResponse(string Code, string Message);

    private abstract record CreateBudgetValidationResult
    {
        public sealed record Valid(NormalizedName Name, CurrencyCode Currency) : CreateBudgetValidationResult;

        public sealed record Invalid(Dictionary<string, string[]> Errors) : CreateBudgetValidationResult;
    }

    private abstract record RenameBudgetValidationResult
    {
        public sealed record Valid(NormalizedName Name) : RenameBudgetValidationResult;

        public sealed record Invalid(Dictionary<string, string[]> Errors) : RenameBudgetValidationResult;
    }

    private abstract record BudgetItemValidationResult
    {
        public sealed record Valid(NormalizedName Name, BudgetItemKind Kind, PositiveMoneyAmount PlannedAmount) : BudgetItemValidationResult;

        public sealed record Invalid(Dictionary<string, string[]> Errors) : BudgetItemValidationResult;
    }

    private abstract record RenameBudgetItemValidationResult
    {
        public sealed record Valid(NormalizedName Name) : RenameBudgetItemValidationResult;

        public sealed record Invalid(Dictionary<string, string[]> Errors) : RenameBudgetItemValidationResult;
    }

    private abstract record BudgetItemPlannedAmountValidationResult
    {
        public sealed record Valid(PositiveMoneyAmount PlannedAmount) : BudgetItemPlannedAmountValidationResult;

        public sealed record Invalid(Dictionary<string, string[]> Errors) : BudgetItemPlannedAmountValidationResult;
    }
}
