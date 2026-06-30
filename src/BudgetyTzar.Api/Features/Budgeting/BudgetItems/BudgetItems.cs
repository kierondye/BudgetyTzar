using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record CreateBudgetItemRequest(string Name, BudgetItemKind? Kind);

public sealed record BudgetItemDto(
    Guid Id,
    Guid BudgetId,
    string Name,
    BudgetItemKind Kind,
    bool IsArchived,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset CreatedAt);

public sealed class CreateBudgetItemValidator : AbstractValidator<CreateBudgetItemRequest>
{
    public CreateBudgetItemValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Kind).NotNull().IsInEnum();
    }
}

public sealed class CreateBudgetItemHandler(
    IBudgetRepository budgets,
    BudgetDbContext db,
    DomainEventOutboxWriter events)
{
    public async Task<CommandResult<BudgetItem>> Handle(Guid budgetId, string name, BudgetItemKind kind, CancellationToken ct)
    {
        var loadResult = await budgets.GetBudgetWithItems(budgetId, ct);
        if (loadResult is BudgetLoadResult.BudgetNotFound)
        {
            return CommandResult<BudgetItem>.NotFound();
        }

        var budget = ((BudgetLoadResult.Success)loadResult).Budget;

        var result = budget.CreateBudgetItem(name, kind);
        if (result is CreateBudgetItemResult.DuplicateName duplicateName)
        {
            return CommandResult<BudgetItem>.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(name)] = [duplicateName.Error]
            });
        }

        if (result is not CreateBudgetItemResult.Success success)
        {
            throw new InvalidOperationException("Create budget item result was not handled.");
        }

        var item = success.Item;
        db.BudgetItems.Add(item);
        var eventId = events.Add(item.CreatedEvent());
        await db.SaveChangesAsync(ct);
        return CommandResult<BudgetItem>.Created(item, eventId);
    }
}

public sealed class ArchiveBudgetItemHandler(BudgetDbContext db, DomainEventOutboxWriter events)
{
    public async Task<CommandResult> Handle(Guid budgetId, Guid itemId, CancellationToken ct)
    {
        var item = await db.BudgetItems.FirstOrDefaultAsync(x => x.Id == itemId && x.BudgetId == budgetId, ct);
        if (item is null)
        {
            return CommandResult.NotFound();
        }

        var archivedItem = item.Archive(DateTimeOffset.UtcNow);
        db.Entry(item).CurrentValues.SetValues(archivedItem);

        var eventId = events.Add(archivedItem.ArchivedEvent());
        await db.SaveChangesAsync(ct);
        return CommandResult.NoContent(eventId);
    }
}
