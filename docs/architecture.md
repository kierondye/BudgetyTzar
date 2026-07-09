# Architecture

This guide explains how the code is arranged, how its parts collaborate, and where to
put new functionality. It describes the implementation rather than defining
product behaviour. Use [the specification](../SPECIFICATION.md) for product rules and
externally observable behaviour, and [the contributing guide](../CONTRIBUTING.md) for
the short development checklist.

## System shape

BudgetyTzar uses Vertical Slice Architecture in a single .NET 9 Minimal API and a
single xUnit test project. Each feature folder brings together the HTTP endpoints,
application coordination, persistence, and contracts needed by that slice. Shared
domain concepts remain under `Domain` so feature slices can use the same ubiquitous
language and invariant-protecting types.

The API process owns the domain model, request coordination, HTTP endpoints, reporting,
and in-memory persistence.

```text
HTTP request
    |
    v
Authentication / identity boundary
    |
    v
Endpoint delegate / handler  ---> response contract ---> HTTP response
    |           |
    |           +-----------> reporting service (queries only)
    v
Domain aggregate or entity
    |
    v
Repository ---> shared InMemoryDataStore
```

Dependencies point inward from HTTP and persistence coordination toward the domain:

- Domain code does not depend on endpoints, repositories, ASP.NET Core, or storage.
- Feature code depends on domain types to execute use cases.
- Repositories depend on domain types because they store and return them.
- Endpoints coordinate domain and repository results and translate them into HTTP.
- Tests may exercise any boundary, but API behaviour tests use only the public HTTP API.

This is a logical separation inside one deployable project, not a set of separately
compiled architectural projects. Do not introduce a new project or abstraction merely
to make the folder tree look layered. Add a boundary when it has a concrete
responsibility and tests that benefit from it.

## Repository map

| Path | Responsibility |
| --- | --- |
| `src/BudgetyTzar.Api/Program.cs` | Process entry point. Creates and runs the web application. |
| `src/BudgetyTzar.Api/ApiApplication.cs` | Composition root. Registers feature services and maps endpoint groups. |
| `src/BudgetyTzar.Api/Domain/Entities` | Immutable entities, aggregates, their operations, and operation-specific result types. |
| `src/BudgetyTzar.Api/Domain/ValueTypes` | Validated domain values such as names, currencies, money amounts, and kinds. |
| `src/BudgetyTzar.Api/Features/Budgeting` | Budget HTTP contracts, endpoint handlers, and budget persistence. |
| `src/BudgetyTzar.Api/Features/Transactions` | Transaction and allocation HTTP contracts, endpoint handlers, filters, and persistence. |
| `src/BudgetyTzar.Api/Features/Reporting` | Budget summary query model, calculation service, contracts, and endpoint. |
| `src/BudgetyTzar.Api/Features/InMemoryDataStore.cs` | Shared in-memory state and the synchronization boundary used by repositories. |
| `src/BudgetyTzar.Api/Authentication` | Authentication registration, external provider/subject validation, and mapping to an internal application user identity. |
| `tests/BudgetyTzar.Tests/Support` | Test-only API host and shared test support. |
| `tests/BudgetyTzar.Tests/<Feature>` | Domain, repository, and API behaviour tests grouped by feature. |
| `SPECIFICATION.md` | Product rules and externally observable behaviour. |
| `CONTRIBUTING.md` | Contributor workflow and the checklist for adding functionality. |
| `scripts` and `.githooks` | Versioning, release, and commit-message tooling. |

Application handling lives in private methods on endpoint classes, persistence
implementations live beside their feature, and HTTP contracts use `*Request` and
`*Response` records in `*Contracts.cs`. Reflect structural changes in this guide.

## Responsibilities and placement

### Value types

Create a value type in `Domain/ValueTypes` when a value has domain-wide validation or
semantics that should not be represented by a primitive. Existing value types use
`TryCreate` and an `Empty` value or nullable out value to report parsing failure without
throwing.

Value types must not know about JSON, HTTP status codes, repositories, or persistence
versions.

### Entities and aggregates

Create an entity in `Domain/Entities` when the concept has an identity and domain
behaviour. Put an operation on the aggregate that owns the invariant it changes.
`Budget`, for example, owns its collection of `BudgetItem` values and protects
collection-level rules.

Domain types should make invalid states difficult or impossible to represent. Create
validated value types before invoking aggregate operations, so a method such as
`Budget.Rename` can accept a `NormalizedName` without also handling invalid strings.

Domain objects are immutable. An operation returns a new object with the same identity
instead of modifying the existing object. Collections exposed by aggregates are
immutable too.

Expected outcomes use operation-specific result types next to the operation:
`AddBudgetItemResult.Added`, `AddBudgetItemResult.DuplicateName`, and
`AddBudgetItemResult.InvalidIdentity` are examples. This keeps intent visible to callers
and avoids `null`, booleans with ambiguous meaning, or exceptions for expected domain
failures.

The aggregate protects rules it can decide from its own state. A rule requiring a
storage-wide view belongs to the repository's atomic save boundary.

### Endpoint delegates and handlers

The private methods in `BudgetEndpoints`, `TransactionEndpoints`, and
`BudgetSummaryEndpoints` are the application's handlers. They:

1. parse and validate transport input;
2. load current state;
3. call a domain operation or query service;
4. persist the resulting state where required; and
5. map every expected outcome to an HTTP response.

Keep route registration in `Map*Endpoints` and dependency registration in
`Add*`. Keep request and response records in the feature's `*Contracts.cs`.

If coordination becomes substantial or is reused outside HTTP, extract a named handler
in the same feature. An extracted handler should return application outcomes, not
`IResult`; the endpoint remains responsible for HTTP mapping.

Handlers must not reimplement aggregate invariants or reach into
`InMemoryDataStore`. A pre-check such as `HasBudgetNamed` can provide friendly feedback,
but the repository save remains the final consistency guard.

Business endpoint groups require the `BusinessApi` authorization policy. Handlers
use scoped repositories that resolve ownership through `ICurrentUser`. Owner
identifiers never come from transport contracts and are never included in existing
response contracts.

### Repositories and the shared store

The repositories are concrete in-memory classes in their owning features.
Their public methods and result records form the persistence contract; there
are no repository interfaces yet.

Repositories:

- load and store domain objects;
- own persistence metadata and concurrency checks;
- enforce storage-wide uniqueness and referential integrity atomically;
- return explicit result types for expected conflicts; and
- preserve insertion order where the public list behaviour requires it.

`InMemoryDataStore` is registered once and shared by all repositories. Its `SyncRoot`
is the transaction boundary for operations involving budgets, transactions, and
allocations. This is important for checks such as preventing deletion of an allocated
transaction or budget item: the check and write happen under the same lock as the
related allocation state.

The shared InMemoryDataStore synchronization boundary allows the in-memory
repositories to emulate referential-integrity constraints across repository boundaries.
This also introduces cross-boundary coupling: for example, Budgeting must know whether
Transaction Allocations reference a budget item. That dependency should be acknowledged
and kept explicit, without moving ownership of allocations into Budgeting.

Keep direct access to the shared dictionaries inside repositories. An eventual
database implementation should preserve the same observable outcomes using database
transactions, constraints, and concurrency tokens.

User-facing repositories are scoped and read the current internal
`ApplicationUserId` from `ICurrentUser`. The shared in-memory store remains a
singleton, and it keeps ownership metadata separately from domain entities, so the
domain and HTTP resource representations remain unchanged. Repository lookups apply
identity and resource identity together. This makes a cross-user lookup
indistinguishable from a missing resource and provides the final atomic guard for
allocations, where the transaction and budget item must share the calling identity.

Do not add administrative or background cross-user operations to these user-facing
repository methods. Those workflows must use a separate, explicitly user-aware
interface so cross-user access remains visible at every call site.

### `EntityState<T>`

`EntityState<T>` pairs a loaded aggregate with opaque concurrency state:

```csharp
var state = budgets.Get(budgetId);

if (state is null)
{
    return Results.NotFound();
}

if (state.Value.ChangeBudgetItemPlannedAmount(budgetItemId, amount)
    is not ChangeBudgetItemPlannedAmountResult.Changed changed)
{
    return Results.NotFound();
}

var saveResult = budgets.Save(state.Update(changed.Budget));
```

`Update` replaces the immutable value while retaining the concurrency state returned by
`Get`. `Save(EntityState<Budget>)` gives that state back to the repository when
attempting the write.

`EntityState<T>` is opaque to application code: callers only carry it from `Get`,
through `Update`, to `Save`. Each repository can therefore encode whatever concurrency
state its persistence mechanism requires without exposing that choice to domain or
application code. A repository returns its own private `EntityState<T>` implementation
and rejects state created by a different repository implementation.

`InMemoryBudgetRepository` represents the concurrency state with its own private
numeric token and compares it under the repository lock. If another writer has already
saved the aggregate, the repository returns `BudgetSaveResult.StaleState` without
overwriting the newer value. This state belongs to persistence, not the domain: do not
add it to `Budget`, expose it through domain operations, or let the aggregate change
it.

### Reporting services

Reporting provides read-only views of domain data. It coordinates data from multiple
features and produces report-specific models without modifying domain or repository
state. Put calculations and query coordination in a service such as
`BudgetSummaryService`, with its models and result types in the reporting feature.

### Tests

Tests have distinct jobs:

| Test type | Place | Proves |
| --- | --- | --- |
| Domain test | `<Feature>/*DomainTests.cs` | An aggregate or value operation protects an invariant and remains immutable. |
| Repository test | `<Feature>/*RepositoryTests.cs` | Atomic uniqueness, stale-write, idempotency, or referential-integrity behaviour at the persistence boundary. |
| API behaviour test | `<Feature>/*ApiTests.cs` | The complete public request-to-response workflow, including status, body, and persisted observable state. |
| Bootstrap test | `ApiBootstrapTests.cs` | Cross-cutting host surfaces such as health, version, and OpenAPI metadata. |

API tests start a real application through `TestApiServer` and communicate through
`HttpClient`. Do not inspect repository dictionaries from an API test. Prefer an
existing public read endpoint to verify the result of a write.

## Request lifecycle

A budget update follows the project's standard `Get -> domain operation -> Save` flow:

1. ASP.NET Core authenticates the request and the `BusinessApi` policy requires the
   configured provider and subject claims to be present and non-blank.
2. `ICurrentUser` maps the authenticated external `(provider, subject)` identity to
   an internal `ApplicationUserId` for the request.
3. ASP.NET Core binds the route and JSON body to endpoint parameters.
4. The endpoint handler validates primitive transport values and creates value types.
   Invalid input becomes a validation problem.
5. The handler calls a scoped repository. A missing aggregate or one owned by another
   identity becomes `404 Not Found`.
6. The handler calls an operation on the immutable aggregate.
7. The domain returns a specific result. Expected failures are mapped without
   exceptions.
8. For a successful domain change, the handler calls `state.Update(newAggregate)` and
   saves it.
9. The repository rechecks ownership and its storage-wide guarantees while holding the
   shared lock.
10. The handler maps the repository result to a success, conflict, not-found, or other
   expected API response and converts domain data to a response contract.

Read-only endpoints skip the domain-operation and save steps. Cross-feature reports use
a query service between the handler and repositories.

## Consistency and failures

Different boundaries answer different questions:

- **Aggregate invariants:** decided by the aggregate from its own state. Duplicate
  budget-item names are detected by `Budget`, for example.
- **Uniqueness:** finally decided by the repository under its synchronization boundary.
  The budget name index makes the check and write atomic.
- **Optimistic concurrency:** represented by `EntityState<T>` and a repository
  `StaleState` result.
- **Referential integrity:** decided atomically where all involved stored state is
  visible. The shared store prevents an allocation racing with deletion of its
  transaction or budget item.
- **Idempotency:** made explicit in repository outcomes. Allocating a transaction to
  the same item returns the existing successful allocation.

Expected domain, application, and persistence outcomes are records in an
operation-specific result hierarchy and are mapped explicitly by the handler.
Exceptions are reserved for programming errors and genuinely unexpected results; the
existing switch defaults make that distinction visible.

HTTP conflict bodies use stable codes and human-readable messages. Keep transport
formatting out of domain and repository result types.

## Naming conventions

- Feature folders and endpoint classes use the ubiquitous feature name:
  `Budgeting/BudgetEndpoints`, `Transactions/TransactionEndpoints`.
- Transport types end in `Request` or `Response`.
- Domain result families describe one operation:
  `RenameBudgetItemResult`, `TransactionDeleteResult`.
- Successful and failed variants use outcome names:
  `Renamed`, `NotFound`, `StaleState`, `DuplicateName`.
- In-memory adapters start with `InMemory`; a future adapter should identify its
  persistence technology without changing domain names.
- Tests name the unit and observable behaviour:
  `Save_rejects_stale_updates_without_overwriting_existing_budget`.
- Public C# types and members use `PascalCase`; locals and parameters use `camelCase`.
- Money and dates cross the HTTP boundary as invariant strings and are
  formatted in response contracts.

## Worked vertical slice: change a budget item's planned amount

The existing planned-amount update is a representative budget operation to copy when
adding a similar command.

### 1. Define intent and domain outcomes

`ChangeBudgetItemPlannedAmountResult` lives next to `Budget` and names the only domain
outcomes: `Changed` carries the new `Budget` and updated `BudgetItem`; `NotFound`
represents a missing item. The validated `PositiveMoneyAmount` makes an invalid amount
impossible at this boundary.

For a new operation, first identify the aggregate method's intent and enumerate its
expected outcomes. Add only outcomes the domain itself can decide.

### 2. Protect the invariant in the immutable aggregate

`Budget.ChangeBudgetItemPlannedAmount` finds the owned item, creates a replacement
through `BudgetItem.ChangePlannedAmount`, and returns a new `Budget` containing it.
Neither the original budget nor item is changed.

A similar operation belongs on `Budget` if it changes an owned budget item or depends
on the budget's state. Return the new aggregate so persistence receives a complete,
valid snapshot.

### 3. Extend persistence only when persistence has new work

This operation needs no new repository method: `Get` plus
`Save(EntityState<Budget>)` already expresses loading and replacing an aggregate with a
concurrency check. Do not add an operation-specific repository method merely because a
new endpoint exists.

Extend the repository contract when persistence has a new responsibility, such as a
new lookup, atomic cross-record check, or storage conflict. Give expected outcomes
specific result variants.

### 4. Preserve concurrency and integrity in memory

The handler retains the opaque persistence state loaded with the budget:

```csharp
var budgetState = budgets.Get(budgetId);

if (budgetState is null)
{
    return Results.NotFound();
}

if (budgetState.Value.ChangeBudgetItemPlannedAmount(budgetItemId, amount)
    is not ChangeBudgetItemPlannedAmountResult.Changed changed)
{
    return Results.NotFound();
}

var save = budgets.Save(budgetState.Update(changed.Budget));
```

`InMemoryBudgetRepository.Save` performs its concurrency, uniqueness, and referential
checks while holding `InMemoryDataStore.SyncRoot`. A new persistence rule must be
checked in the same critical section as its write and must leave stored state untouched
on failure.

### 5. Coordinate and map outcomes in the handler

`ChangeBudgetItemPlannedAmount` in `BudgetEndpoints` validates the request, loads the
budget, calls the domain method, saves the new state, and maps:

- invalid input to a validation problem;
- a missing budget or item to `404`;
- stale persistence state to a conflict; and
- a successful save to `200` with `BudgetItemResponse`.

Map every expected domain and repository result deliberately. Do not let repository
types or domain objects become the wire response.

### 6. Expose the endpoint

The `MapBudgetEndpoints` method registers the `PUT` route and gives it a stable OpenAPI
name. The body record lives in `BudgetContracts.cs`, and `BudgetItemResponse` performs
the outward formatting.

Place a new budget route in the same group, following the existing resource-oriented
route shape and naming style.

### 7. Test each boundary for its own responsibility

For this operation:

- `BudgetDomainTests` proves that item updates return a new aggregate without mutating
  the old one.
- `BudgetRepositoryTests` proves stale updates cannot overwrite newer stored state.
- `BudgetApiTests` sends the request over HTTP and verifies both the response and a
  subsequent public read.

A new operation does not automatically require all three test types. Add a domain test
for new invariant logic, a repository test for new persistence or race behaviour, and
an API behaviour test for every new or changed public workflow.

## Before changing the structure

Keep the implementation and this guide aligned. When responsibilities or request flow
change, update the project map, dependency rules, lifecycle, and worked example in the
same pull request.
