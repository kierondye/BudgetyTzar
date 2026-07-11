# Contributing

Before changing or reviewing behaviour, read the relevant parts of
[the product specification](SPECIFICATION.md). It is the source of truth for product
rules and externally observable behaviour. Read
[the architecture guide](docs/architecture.md) for where code belongs and why. Use the [read-me](README.md) for local setup guidance.

## Principles

- Represent domain and application concepts as first-class internal models, distinct from transport, storage, framework, external-system, or integration-specific representations.
- Prefer immutable, valid-by-construction types that enforce their own invariants and make invalid, insecure, or ambiguous states difficult to represent.
- Model expected failures as explicit outcomes rather than hidden control flow.
- Keep domain logic pure and independent of transport, persistence, framework, and infrastructure concerns.
- Keep handlers focused on coordinating use cases, not owning domain, persistence, identity, or integration rules.
- Keep persistence concerns behind repositories or equivalent persistence boundaries.
- Keep production composition free of test-only behaviour, defaults, identities, schemes, and shortcuts.
- Test externally observable behaviour through public boundaries before relying on lower-level tests.
- Exercise the real behaviour under test rather than replacing it with fakes, mocks, shortcuts, or test-only paths.
- Keep identity and ownership concepts explicit and stable, with each rule modeled in the boundary that owns it.

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

### Internal models at system boundaries

Principles: Represent domain and application concepts as first-class internal models,
distinct from external-system representations. Keep identity and ownership concepts
explicit and stable, with each rule modeled in the boundary that owns it.

Prefer:

```csharp
public sealed record ExternalIdentity(string Provider, string Subject);

public sealed record CurrentUser(ApplicationUserId UserId) : ICurrentUser;

return ExternalIdentity.TryCreate(provider, subject, out var externalIdentity)
    ? identityResolver.Resolve(externalIdentity)
    : new CurrentUserResolution.Unauthenticated();
```

Avoid:

```csharp
public sealed record CurrentUser(string Subject) : ICurrentUser;

return new CurrentUserResolution.Authenticated(new CurrentUser(subject));
```

### Valid-by-construction domain types

Principles: Prefer immutable, valid-by-construction types that make invalid, insecure,
or ambiguous states difficult to represent. Represent domain and application concepts
as first-class internal models.

Prefer:

```csharp
public BudgetItem Create(NormalizedName name, PositiveMoneyAmount plannedAmount)
{
    return new BudgetItem(name, plannedAmount);
}
```

Avoid:

```csharp
public BudgetItem Create(string name, decimal plannedAmount)
{
    if (string.IsNullOrWhiteSpace(name))
    {
        throw new ArgumentException("Budget item name is required.", nameof(name));
    }

    if (plannedAmount <= 0.00m)
    {
        throw new ArgumentException("Planned amount must be positive.", nameof(plannedAmount));
    }

    return new BudgetItem(name, plannedAmount);
}
```

### Immutable types

Principles: Prefer immutable, valid-by-construction types that enforce their own
invariants and make invalid, insecure, or ambiguous states difficult to represent.
Keep identity and ownership concepts explicit and stable.

Prefer:

```csharp
public sealed record CurrentUser(ApplicationUserId UserId) : ICurrentUser;
```

Avoid:

```csharp
public sealed class CurrentUser : ICurrentUser
{
    public ApplicationUserId UserId { get; set; }
}
```

### Explicit domain outcomes

Principles: Model expected failures as explicit outcomes rather than hidden control
flow. Keep domain logic pure.

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

Principles: Keep persistence concerns behind repositories or equivalent persistence
boundaries. Keep domain logic pure.

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

Principles: Test externally observable behaviour through public boundaries before
relying on lower-level tests. Keep handlers focused on coordinating use cases.

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

Principles and guidelines: Exercise the real behaviour under test rather than
replacing it with fakes, mocks, shortcuts, or test-only paths. User-facing
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
- Review whether the implementation follows the principles above even when the public
  behaviour is covered by passing tests.
- Add the smallest useful set of tests for the risk: public-boundary behaviour tests
  for user-visible workflows, domain tests for invariant logic, and repository tests
  for persistence guarantees.
- Run the build and full test suite, and update the architecture guide if
  responsibilities, request flow, or persistence boundaries changed.
