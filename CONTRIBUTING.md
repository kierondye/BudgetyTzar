# Contributing

Thank you for contributing to BudgetyTzar.

Before changing behaviour, read the relevant parts of
[the product specification](SPECIFICATION.md). It is the source of truth for product
rules and externally observable behaviour. Read
[the architecture guide](docs/architecture.md) for the current code structure,
responsibility boundaries, request flow, and worked example.

## Local checks

Build and test the solution:

```bash
dotnet build BudgetyTzar.sln
dotnet test BudgetyTzar.sln
```

Enable the repository's Conventional Commit hook once per clone:

```bash
git config core.hooksPath .githooks
```

## Adding functionality

- Confirm the intended behaviour and language in `SPECIFICATION.md`; update it when
  externally observable behaviour changes.
- Put validated domain values in `Domain/ValueTypes` and invariant-protecting,
  immutable behaviour in the owning aggregate under `Domain/Entities`.
- Give expected domain outcomes an operation-specific result type.
- Follow `Get -> domain operation -> Save` for aggregate updates, carrying
  `EntityState<T>` through the workflow.
- Extend a repository only when persistence has a new responsibility, and preserve
  concurrency, uniqueness, and referential checks atomically.
- Coordinate validation and outcomes in the feature handler, then map them to HTTP in
  the endpoint. Keep request and response records in the feature's contracts file.
- Register the endpoint and dependencies through the feature's `Map*` and `Add*`
  methods.
- Add domain tests for invariant logic, repository tests for persistence guarantees,
  and API behaviour tests for the public workflow.
- Run the build and full test suite, and update
  [the architecture guide](docs/architecture.md) if responsibilities or request flow
  changed.

The detailed rationale and an end-to-end example are in
[Architecture](docs/architecture.md#worked-vertical-slice-change-a-budget-items-planned-amount).
