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

- Prefer: Construct an `ICurrentUser` implementation only after authentication has
  resolved a valid internal application user.
- Avoid: Creating a mutable current-user holder that middleware or handlers populate
  later with raw provider claim values.

### Explicit domain outcomes

Principles: Make expected failures explicit. Keep domain logic pure.

- Prefer: Return `RenameBudgetItemResult.DuplicateName` or
  `TransactionDeleteResult.TransactionHasAllocation` for expected business-rule
  failures.
- Avoid: Throwing exceptions, returning `null`, or returning `false` for ordinary
  domain outcomes that callers must map deliberately.

### Repository-owned persistence state

Principles: Keep persistence concerns behind repositories. Keep domain logic pure.

- Prefer: Carry `EntityState<T>` from repository `Get`, through aggregate `Update`, and
  back to repository `Save`.
- Avoid: Adding concurrency tokens, database versions, or storage metadata to domain
  entities.

### Behaviour-first tests through the public API

Principles: Test externally observable behaviour through public boundaries first. Keep
handlers as coordinators.

- Prefer: Send an HTTP command, assert the response, then use a public read endpoint to
  verify observable state.
- Avoid: Treating a repository or handler test as the only proof for behaviour that a
  real API client, user, or operator depends on.

### Authentication and ownership tests

Principles and guidelines: Do not fake the behaviour under test. User-facing
repositories are scoped to the current internal application user.

- Prefer: Exercise the real authentication and authorisation path when the requirement
  is about current-user identity, ownership, privacy, or cross-user access.
- Avoid: Replacing authentication with a fake current user as the primary proof for an
  ownership or privacy rule.

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
