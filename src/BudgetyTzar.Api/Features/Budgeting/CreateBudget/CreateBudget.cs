using BudgetyTzar.Api.Application.Common;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;

namespace BudgetyTzar.Api.Features;

public sealed record CreateBudgetRequest(string Name, string Currency);

public sealed class CreateBudgetValidator : AbstractValidator<CreateBudgetRequest>
{
    public CreateBudgetValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Currency).Currency();
    }
}

public sealed class CreateBudgetHandler(BudgetDbContext db, DomainEventOutboxWriter events)
{
    public async Task<CommandResult<Budget>> Handle(string name, string currency, CancellationToken ct)
    {
        var budget = Budget.Create(name, currency);
        db.Budgets.Add(budget);
        var eventId = events.Add(budget.CreatedEvent());
        await db.SaveChangesAsync(ct);
        return CommandResult<Budget>.Created(budget, eventId);
    }
}
