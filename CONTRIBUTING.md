# Contributing

Thank you for contributing to BudgetyTzar.

Before changing behaviour, read the relevant parts of
[the product specification](SPECIFICATION.md). It is the source of truth for product
rules and externally observable behaviour. Read
[the architecture guide](docs/architecture.md) for where code belongs and why.

## Local Checks

Build and test the solution:

```bash
dotnet build BudgetyTzar.sln
dotnet test BudgetyTzar.sln
```

Enable the repository's Conventional Commit hook once per clone:

```bash
git config core.hooksPath .githooks
```

## Principles

- Prefer immutable, valid-by-construction types where practical.
- Make expected failures explicit.
- Keep domain logic pure and independent of transport, persistence, and framework
  concerns.
- Keep handlers as coordinators.
- Keep persistence concerns behind repositories.
- Test externally observable behaviour through public boundaries first.
- Do not fake the behaviour under test.
- Keep identity and ownership concepts explicit.

## Specific Guidelines

- Aggregate updates follow `Get -> domain operation -> Save`.
- Use `EntityState<T>` to carry repository-owned concurrency state through that flow.
- Do not put persistence versions, database concerns, or storage tokens on domain
  entities.
- User-facing repositories are scoped to the current internal application user.
- Use a separate explicitly user-aware API for admin, migration, support, or background
  cross-user workflows.
- Exercise the real authentication and authorisation path when authentication or
  ownership is the behaviour under test.

## Examples

### Immutable current user

Principles: Prefer immutable, valid-by-construction types. Keep identity and ownership
concepts explicit.

Prefer:

```csharp
public sealed record CurrentUser(ApplicationUserId UserId) : ICurrentUser;

return ApplicationUserId.TryCreate(providerSubject, out var userId)
    ? new CurrentUserResolution.Authenticated(new CurrentUser(userId))
    : new CurrentUserResolution.Unauthenticated();
```

Avoid:

```csharp
public sealed class CurrentUser : ICurrentUser
{
    public string? ProviderSubject { get; set; }
    public Guid? UserId { get; set; }
}
```

### Explicit domain outcomes

Principles: Make expected failures explicit. Keep domain logic pure.

Prefer:

```csharp
if (store.AllocationsByTransactionId.ContainsKey(transactionId))
{
    return new TransactionDeleteResult.TransactionHasAllocation();
}

return new TransactionDeleteResult.Deleted();
```

Avoid:

```csharp
if (store.AllocationsByTransactionId.ContainsKey(transactionId))
{
    throw new InvalidOperationException("Transaction has an allocation.");
}

return true;
```

### Repository-owned persistence state

Principles: Keep persistence concerns behind repositories. Keep domain logic pure.

Prefer:

```csharp
var budgetState = budgets.Get(budgetId);

if (budgetState?.Value.Rename(name) is not RenameBudgetResult.Renamed renamed)
{
    return Results.NotFound();
}

var save = budgets.Save(budgetState.Update(renamed.Budget));
```

Avoid:

```csharp
budget.Version++;
budget.Name = name;

var save = budgets.Save(budget, expectedVersion: budget.Version);
```

### Behaviour-first tests through the public API

Principles: Test externally observable behaviour through public boundaries first. Keep
handlers as coordinators.

Prefer:

```csharp
using var renameResponse = await server.Client.PutAsJsonAsync(
    $"/api/budgets/{createdBudget.BudgetId}/name",
    new RenameBudgetRequest("UK 2026"));

Assert.Equal(HttpStatusCode.OK, renameResponse.StatusCode);

var retrievedBudget = await server.Client.GetFromJsonAsync<BudgetResponse>(
    $"/api/budgets/{createdBudget.BudgetId}");

Assert.Equal("UK 2026", retrievedBudget?.Name);
```

Avoid:

```csharp
var handler = new RenameBudgetHandler(budgets);

await handler.RenameBudget(createdBudget.BudgetId, "UK 2026");

Assert.Equal("UK 2026", budgets.Get(createdBudget.BudgetId)?.Value.Name.ToString());
```

### Authentication and ownership tests

Principles and guidelines: Do not fake the behaviour under test. User-facing
repositories are scoped to the current internal application user.

Prefer:

```csharp
using var response = await userA.Client.GetAsync($"/api/budgets/{userBBudgetId}");

Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
```

Avoid:

```csharp
var currentUser = new FakeCurrentUser(userAId);
var budgets = new InMemoryBudgetRepository(currentUser);

Assert.Null(budgets.Get(userBBudgetId));
```

## Adding Functionality

- Confirm the intended behaviour and language in `SPECIFICATION.md`; update it when
  externally observable behaviour changes.
- Follow the placement and ownership guidance in [Architecture](docs/architecture.md).
- Apply the principles and specific guidelines above.
- Add the smallest useful set of tests for the risk: public-boundary behaviour tests
  for user-visible workflows, domain tests for invariant logic, and repository tests
  for persistence guarantees.
- Run the build and full test suite, and update the architecture guide if
  responsibilities, request flow, or persistence boundaries changed.
