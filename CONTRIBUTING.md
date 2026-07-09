# Contributing

Thank you for contributing to BudgetyTzar.

Before changing behaviour, read the relevant parts of
[the product specification](SPECIFICATION.md). It is the source of truth for product
rules and externally observable behaviour. Read
[the architecture guide](docs/architecture.md) for the code structure,
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

## Domain, persistence, and application patterns

Use these constraints when deciding where behaviour belongs:

1. Data access components, such as repositories, own concurrency and provide the
   final atomic guard for stale state, uniqueness, referential integrity, and other
   storage conflicts.
2. Endpoint handlers and reporting services depend on narrow persistence contracts
   owned by their feature, while storage-specific adapters implement those contracts.
   Do not introduce generic repositories, speculative operations, or a unit of work.
3. Domain aggregates protect the invariants they can decide from their own state.
4. Domain classes are immutable; successful operations return new instances while
   preserving entity identity.
5. Domain classes express intent through named operations rather than general
   setters or mutable collections.
6. Expected domain and persistence outcomes use explicit, operation-specific result
   types rather than exceptions, `null`, or ambiguous booleans.
7. Domain methods do not return `null`; absence and rejection are named result cases.
8. Domain properties are non-nullable, using validated value types and immutable
   collections to keep constructed objects valid.

Aggregate updates follow `Get -> domain operation -> Save`. `EntityState<T>` carries
persistence-owned, opaque concurrency state through that workflow; aggregates neither
track nor inspect persistence versions. Handlers coordinate request validation, domain
and persistence outcomes, and API response mapping.

Friendly pre-checks can improve feedback, but they are never the consistency guarantee.
In-memory repositories must preserve the same observable consistency rules as a
transactional database, using the shared synchronization boundary when an invariant
spans repositories or collections.

Cover the complete workflow with API behaviour tests, supplemented by focused domain
tests for invariants and repository contract tests for concurrency and storage
conflicts. See [Architecture](docs/architecture.md) for the detailed rationale and
examples.

## Adding functionality

- Confirm the intended behaviour and language in `SPECIFICATION.md`; update it when
  externally observable behaviour changes.
- Put validated domain values in `Domain/ValueTypes` and invariant-protecting,
  immutable behaviour in the owning aggregate under `Domain/Entities`.
- Give expected domain outcomes an operation-specific result type.
- Follow `Get -> domain operation -> Save` for aggregate updates, carrying
  `EntityState<T>` through the workflow.
- Extend a feature persistence contract only when a current use case gives persistence
  a new responsibility. Implement it in each adapter and preserve concurrency,
  uniqueness, and referential checks atomically.
- In the endpoint handler, coordinate validation, domain and persistence outcomes, and
  HTTP mapping. If a handler is extracted, return application outcomes from it while
  retaining HTTP mapping in the endpoint. Keep request and response records in the
  feature's contracts file.
- Register the endpoint and dependencies through the feature's `Map*` and `Add*`
  methods.
- Add domain tests for invariant logic, repository contract tests for every expected
  persistence outcome, and API behaviour tests for the public workflow.
- Run the build and full test suite, and update
  [the architecture guide](docs/architecture.md) if responsibilities or request flow
  changed.

The detailed rationale and an end-to-end example are in
[Architecture](docs/architecture.md#worked-vertical-slice-change-a-budget-items-planned-amount).
