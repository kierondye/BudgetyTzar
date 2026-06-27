# 13 - Architecture Boundary Hardening

## Goal

Strengthen modular-monolith boundaries where the current implementation can violate bounded-context ownership or runtime invariants, without splitting physical databases or rewriting `SPECIFICATION.md`.

## Scope

- Preserve current HTTP API behavior, event contracts, envelope shape, topics, table names, migrations, and projection behavior.
- Harden budget invariants against concurrent writes.
- Introduce compile-time persistence ownership boundaries inside the modular monolith.
- Standardise event validation, deserialization, and typed handler dispatch.
- Clarify event metadata, payload data, and projection source metadata usage.
- Reduce endpoint and naming ambiguity where it weakens ownership boundaries.

## Out of scope

- Physical database split.
- Service extraction.
- Project-per-context split unless explicitly approved later.
- Rewriting `SPECIFICATION.md`.
- Generic mediator, repository, or event-sourcing framework.
- Broad namespace churn or EF migration churn.
- Containerisation, identity/authorization, Kubernetes, or cloud deployment.

## Increment 1 - Concurrent Budget Invariant Correctness

### Architectural goal

Make budget adjustment invariants correct under concurrent writes.

### Boundary strengthened

Budgeting command writes must enforce budgeting invariants at the persistence boundary, not only in pre-save application memory.

### Must remain unchanged

- Adjustment API route, status codes, request/response JSON.
- Existing validation message: `Net planned spending must not exceed net planned income.`
- Event names, payloads, envelope shape, outbox behavior.
- Table names and existing migrations.

### Likely files affected

- `src/BudgetyTzar.Api/Features/Budgeting/Adjustments/Adjustments.cs`
- `src/BudgetyTzar.Api/Infrastructure/Persistence/BudgetDbContext.cs`
- Possibly a small budgeting persistence helper under `Features/Budgeting` or `Infrastructure/Persistence`.
- `tests/BudgetyTzar.Tests/Budgeting/**`
- `tests/BudgetyTzar.Tests/Support/**`

### Tests required

- PostgreSQL-backed concurrent adjustment test where two debits race against the same available planned income.
- Existing budget adjustment tests.
- Event/outbox tests proving only successful writes emit events.
- Full `dotnet test`.

### Risks

- Serializable transaction retries may introduce provider-specific behavior.
- Locking too broadly could reduce write throughput.
- SQLite-backed tests may not reproduce PostgreSQL behavior.

### Deferred

- Event-store aggregate rehydration.
- Physical database or schema split.
- Broad transaction wrapper across all command handlers.

## Increment 2 - Compile-Time Persistence Ownership Boundaries

### Architectural goal

Prevent bounded-context table crossover at compile time inside the modular monolith.

### Boundary strengthened

Feature/application code should not depend on the unrestricted `BudgetDbContext` when a narrower persistence boundary can express the tables or operations that code is allowed to use.

### First task

Before changing constructors or adding abstractions, propose the narrowest persistence boundary shape for the current codebase. The proposal should identify:

- Which bounded context owns each current table/DbSet.
- Which feature, projection, consumer, or infrastructure component is allowed to access each table.
- Which dependencies should be forbidden, especially direct feature-handler dependencies on unrestricted `BudgetDbContext`.
- Whether the safest first boundary is separate bounded-context `DbContext` types over the same physical database, narrow operation-specific interfaces, internal persistence modules, or another concrete compile-time mechanism.
- How the chosen shape preserves EF mappings, migrations, and model identity.

Do not commit to broad `IBudgetingDb`, `ITransactionsDb`, `IReportingDb`, or `IAuditDb` interfaces until that proposal has been reviewed.

### Must remain unchanged

- One physical PostgreSQL database.
- Existing EF model, table names, migrations, and model snapshot.
- Existing runtime behavior.
- Existing query results and projection behavior.

### Likely files affected

- `src/BudgetyTzar.Api/Infrastructure/Persistence/BudgetDbContext.cs`
- A new design note or completion section in this roadmap file.
- Later implementation may affect feature handlers under:
  - `src/BudgetyTzar.Api/Features/Budgeting/**`
  - `src/BudgetyTzar.Api/Features/Transactions/**`
  - `src/BudgetyTzar.Api/Features/Reporting/**`
  - `src/BudgetyTzar.Api/Features/Audit/**`
- Later implementation may affect infrastructure event stores/consumers under `src/BudgetyTzar.Api/Infrastructure/Events/**`.
- Later implementation may affect DI registration.

### Tests required

- Existing full suite.
- Compile-time coverage through changed constructor dependencies once implementation starts.
- Optional architecture tests that prevent feature handlers from directly depending on unrestricted `BudgetDbContext`.

### Risks

- Broad interfaces can become leaky mini-DbContexts.
- Too many tiny interfaces could add noise without meaningful ownership.
- Separate bounded-context DbContext types may duplicate mapping and complicate migrations if introduced too early.
- Reporting may still need carefully approved read access to projection state only.

### Deferred

- Separate physical databases.
- Separate EF migrations per bounded context.
- Splitting bounded contexts into separate assemblies.
- Repositories unless a specific use case justifies one.

## Increment 3 - Typed Event Validation, Deserialization, and Handler Boundary

### Architectural goal

Create one concrete event-consumption pipeline used by reporting and audit.

### Boundary strengthened

Infrastructure owns raw JSON, envelope/schema validation, typed deserialization, retry, idempotency, and dead-letter flow. Feature handlers receive typed messages.

### Must remain unchanged

- Kafka topics and consumer groups.
- Dead-letter payload shape unless explicitly changed.
- Failure persistence semantics.
- Projection idempotency.
- Audit append-only behavior.
- Event schemas and envelope shape.

### Likely files affected

- `src/BudgetyTzar.Api/Infrastructure/Events/EventSchemaValidator.cs`
- `src/BudgetyTzar.Api/Infrastructure/Events/ReportingProjectionConsumerService.cs`
- `src/BudgetyTzar.Api/Infrastructure/Events/AuditEventConsumerService.cs`
- `src/BudgetyTzar.Api/Features/Reporting/Snapshots/ReportingProjectionDispatcher.cs`
- `src/BudgetyTzar.Api/Features/Audit/Projection/AuditEventProjectionService.cs`
- New concrete types such as:
  - `ValidatedEventEnvelope`
  - `ConsumedEvent<TPayload>`
  - `EventHandlerRegistry`
  - or explicit reporting/audit handler maps.

### Tests required

- Event schema validator tests.
- Real outbox envelope contract tests.
- Reporting projection processing tests.
- Audit projection tests.
- Kafka dead-letter tests.
- Tests proving unknown event types fail consistently.

### Risks

- Accidentally double-deserializing or changing error categories.
- Audit details may change if manual JSON reading is replaced too broadly.
- Handler ordering/dispatch mistakes could break projection rebuilds.

### Deferred

- Mediator framework.
- Dynamic assembly scanning if explicit registration is clearer.
- Schema registry.
- Generic repository or event-store abstraction.

## Increment 4 - Event Metadata, Payload, and Projection Metadata Policy

### Architectural goal

Make event method signatures express whether data is domain payload or envelope/projection metadata.

### Boundary strengthened

Projection handlers should not receive loose metadata parameters when those values are required by the projection action.

### Must remain unchanged

- Event payload records and JSON schemas.
- Envelope shape.
- Read-model values.
- Source-event tracking behavior.
- Rebuild and idempotency semantics.

### Likely files affected

- `src/BudgetyTzar.Api/Features/Reporting/Snapshots/ReportingProjectionService.cs`
- `src/BudgetyTzar.Api/Features/Reporting/Snapshots/ReportingProjectionDispatcher.cs`
- `src/BudgetyTzar.Api/Features/Audit/Projection/AuditEventProjectionService.cs`
- Event pipeline types introduced in Increment 3.

### Tests required

- Projection processing tests.
- Snapshot projection tests.
- Audit projection tests.
- Event contract tests.

### Risks

- Overcorrecting to payload-only would hide legitimate projection source metadata.
- Putting domain facts in envelope metadata would weaken event contracts.
- Renaming signatures may touch many tests.

### Deferred

- Event payload schema changes unless a specific domain fact is missing.
- Changing outbox storage.
- Changing event names or versions.

## Increment 5 - Endpoint Partial Class Removal

### Architectural goal

Reduce cross-feature coupling caused by one global partial endpoint class.

### Boundary strengthened

Each feature should own its endpoint mapper rather than contributing private methods to a shared `Endpoints` partial surface.

### Must remain unchanged

- All routes.
- Swagger metadata.
- Status codes.
- Request and response JSON.
- Budget-root grouping under `/api/budgets`.

### Likely files affected

- `src/BudgetyTzar.Api/Features/Budgeting/**Endpoints.cs`
- `src/BudgetyTzar.Api/Features/Transactions/**Endpoints.cs`
- `src/BudgetyTzar.Api/Features/Reporting/**`
- `src/BudgetyTzar.Api/Features/Audit/**`
- `src/BudgetyTzar.Api/Program.cs` or feature composition root.

### Tests required

- Canonical API surface tests.
- Focused endpoint tests for budgeting, transactions, reporting, audit.
- Full `dotnet test`.

### Risks

- Route registration order or tags could drift.
- Private shared helpers may need explicit homes.
- Large mechanical rename if done all at once.

### Deferred

- Route redesign.
- Versioned API routing.
- Authentication/authorization endpoint policies.

## Increment 6 - Misleading Names and Responsibility Cleanup

### Architectural goal

Remove names that imply the wrong boundary or responsibility.

### Boundary strengthened

Command and helper names should reflect actual ownership and behavior.

### Must remain unchanged

- Runtime behavior.
- Validation messages.
- Service lifetimes.
- API and event contracts.

### Likely files affected

- `src/BudgetyTzar.Api/Features/Budgeting/Adjustments/Adjustments.cs`
- `src/BudgetyTzar.Api/Features/Budgeting/Reallocations/Reallocations.cs`
- `src/BudgetyTzar.Api/Features/Budgeting/BudgetItems/BudgetItemEligibilityService.cs`
- Endpoint call sites and tests.

### Tests required

- Budget adjustment tests.
- Budget reallocation tests.
- Transaction allocation tests.
- Compile/full test suite.

### Risks

- Pure renames can create noisy diffs.
- `BudgetItemEligibilityService` may need either a clearer name or clearer return type; doing both at once could broaden the change.

### Deferred

- Broader domain model redesign.
- Moving archived-item rules away from `BudgetItem.CanAcceptActivityOn`.
- Cross-feature abstraction for budget item references.
