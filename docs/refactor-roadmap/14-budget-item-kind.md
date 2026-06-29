# 14 - Budget Item Kind

## Goal

Tighten the budget item domain model by introducing `BudgetItemKind` before continuing concurrency hardening. It is safer to start with explicit funding and consumption semantics and relax them later than to allow loose financial data that may be difficult to migrate safely.

## Status

Increment 3 implemented and awaiting review. Increment 1 documentation and specification semantics were approved before implementation, and Increment 2 introduced the command/API/event contract language.

## Ubiquitous Language

Use the language from `SPECIFICATION.md`:

- `BudgetItemKind`
- `Funding`
- `Consumption`
- `BudgetCapacity`
- `AvailableBudget`
- `BudgetLedger`
- `BudgetAdjustment`
- `BudgetReallocation`

Avoid language such as:

- Planned adjustment.
- Budget planning state.
- Planned ledger state.

The budget is already the plan. Budget adjustments change the BudgetLedger.

## Domain Decisions

### Initial Kinds

`Funding`
: Budget items that create BudgetCapacity, such as salary, bonus, or other funding sources.

`Consumption`
: Budget items that consume BudgetCapacity, such as groceries, mortgage, petrol, eating out, incidentals, holiday funds, car maintenance, or Christmas.

### Deferred Kinds

`Financing` is deferred. The current domain does not yet define borrowing, repayment, account, liability, interest, or transfer semantics. Do not introduce `Financing` as a placeholder.

## Semantic Rules

- Credit/debit movement direction is not the same as budget item kind.
- Transaction allocations never change or flip a budget item's kind.
- A funding item remains funding whether actual funding is above, equal to, or below budget.
- A consumption item remains consumption whether actual spending is above, equal to, or below budget.
- Corrections, refunds, reversals, underpayments, and overpayments are interpreted against the item's kind rather than changing the item's kind.
- A credit transaction allocation to a consumption item is interpreted as a refund, rebate, overpayment correction, or other consumption-side correction.
- A debit transaction allocation to a funding item is interpreted as a reversal, underpayment correction, or other funding-side correction.

## Contract Decisions

This is an intentional early-project contract tightening. Do not preserve backwards compatibility for old clients.

When implementation begins:

- `CreateBudgetItemRequest` must require `BudgetItemKind`.
- Budget item response DTOs must include `BudgetItemKind`.
- `BudgetItemCreatedPayload` must include `BudgetItemKind`.
- `contracts/events/budgeting/budget-item-created.v1.schema.json` must include `kind`.
- Existing tests and fixtures must be updated so salary, bonus, and similar funding-source items are `Funding`.
- Existing tests and fixtures must be updated so groceries, mortgage, petrol, eating out, incidentals, holiday funds, car maintenance, Christmas, and similar spending items are `Consumption`.

Because compatibility is intentionally not preserved, the v1 budget-item-created schema can be tightened rather than introducing a v2 event solely for this early correction.

## Expected Impact

### Budget Items

Budget item creation becomes semantically explicit. Kind belongs to authoritative budgeting command state, not reporting metadata.

### Budget Adjustments

Budget adjustments remain debit/credit movements on budget items.

New invariants:

- A consumption item must not become a funding source through budget adjustments.
- A funding item must not become a consumption item through budget adjustments.
- Opposite-direction adjustments remain valid for corrections, refunds, reversals, underpayments, and overpayments when they are interpreted against the item's kind.

### Budget Reallocations

Budget reallocations should initially move budget between consumption items.

Deferred until `AvailableBudget` is precisely defined:

- A reallocation must not move more budget away from a consumption item than its AvailableBudget as of the reallocation date.

### Transaction Allocations

Transaction allocations do not mutate budget item kind. They should be interpreted against the kind of the target budget item.

Tests should cover:

- Debit transaction allocations to consumption items.
- Credit transaction allocations to funding items.
- Credit transaction allocations to consumption items as refunds or corrections.
- Debit transaction allocations to funding items as reversals or corrections.
- Actual overpayment or underpayment does not change item kind.

### Snapshot And Reporting

Reporting and snapshot read models must carry item kind once the event and persistence changes exist, because reports need to explain item semantics. Reporting remains a read model and must not own the kind.

Existing snapshot calculations should not be reworked until kind-aware report labels or AvailableBudget are explicitly specified.

### Audit

Audit projections should preserve the item kind from budget item creation events once the event payload includes it. Audit descriptions may remain concise, but event details must contain enough data to explain why the item was treated as funding or consumption.

## Smallest Safe Implementation Sequence

### Increment 1 - Glossary And Specification

Status: complete.

- Update `SPECIFICATION.md` with `BudgetItemKind`, `Funding`, `Consumption`, `BudgetCapacity`, `AvailableBudget`, and `BudgetLedger` language.
- Clarify that debit/credit direction is not kind.
- Clarify that transaction allocations never change kind.
- Clarify that corrections, refunds, reversals, underpayments, and overpayments are interpreted against kind.

### Increment 2 - Domain, API, And Event Contract Introduction

Status: implemented, awaiting review.

- Add `BudgetItemKind` with initial values `Funding` and `Consumption`.
- Require kind in `CreateBudgetItemRequest`.
- Include kind in budget item response DTOs.
- Include kind in `BudgetItemCreatedPayload`.
- Tighten `budget-item-created.v1` JSON schema to require `kind`.
- Update event payload contract tests and real outbox envelope contract tests.

Implementation notes:

- Added `BudgetItemKind` to the authoritative `BudgetItem` domain model and budget item creation path.
- API budget item creation now requires `kind`; responses include `kind`.
- `BudgetItemCreatedPayload` and `budget-item-created.v1` now carry required `kind`.
- `BudgetItemArchivedPayload` and `budget-item-archived.v1` also carry required `kind` so archived budget items remain fully described.
- Tests and API helpers now create budget items explicitly as `Funding` or `Consumption`.
- A narrow EF-generated `BudgetItems.Kind` migration was added because the command-side EF model now owns the authoritative kind and PostgreSQL-backed tests apply migrations. Reporting projection and snapshot/read-model persistence were not broadened in this increment.

Architectural decisions:

- Kind is stored as command-side budgeting state, not inferred from debit or credit movement direction.
- Existing debit/credit adjustment and transaction allocation behavior remains unchanged.
- The event contract was tightened in v1 because this is an intentional early-project correction.
- Existing migrated budget items receive `Consumption` as a migration default only to satisfy the non-null command-state column for legacy rows.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetItemsTests|FullyQualifiedName~BudgetTests|FullyQualifiedName~BudgetItemTests|FullyQualifiedName~EventPayloadRecordContractTests|FullyQualifiedName~EventContractTests"` - passed, 23 tests.
- `dotnet build tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false` - passed.
- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~PostgresCompatibilityTests"` - passed, 4 tests after regenerating the migration metadata.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 96 tests.
- `dotnet build BudgetyTzar.sln` was attempted earlier, but the default solution build hung with no output. The test project build above was used to verify compilation.

Deferred work:

- Budget adjustment kind validation.
- Transaction allocation interpretation rules.
- Reallocation availability rules.
- `AvailableBudget`.
- Reporting projection item kind, snapshot/read-model kind exposure, and projection rebuild semantics.
- Step 13 concurrency work.

### Increment 3 - Database And Read Model Updates

Status: implemented, awaiting review.

- Add kind to reporting projection item state.
- Add kind to snapshot/read models where budget item identity is exposed.
- Ensure projection rebuilds can reconstruct item kind from budget item events.

Command-side `BudgetItems.Kind` was pulled into Increment 2 as a narrow migration because the authoritative domain model is EF-backed and PostgreSQL test setup applies migrations.

- Add migrations rather than rewriting existing migrations for any remaining read-model persistence changes.

Implementation notes:

- Added `BudgetItemKind` to `BudgetItemProjectionState`, `BudgetSnapshotItemProjection`, and `BudgetSnapshotItem`.
- Reporting projection state now stores kind from budget item events, including archive events.
- Projection-backed snapshot rows carry kind from projection item state.
- Direct snapshot calculation carries kind from authoritative `BudgetItems`.
- Snapshot calculations, totals, archived-item filtering, and balance formulas were not changed.
- Added an EF-generated migration for reporting read-model kind columns. Existing read-model rows are backfilled from authoritative `BudgetItems.Kind` where matching command rows exist, with `Consumption` only as the non-null fallback.
- Kafka projection tests use a temporary file-backed SQLite database so concurrent audit and reporting consumer writes use separate connections instead of sharing one in-memory connection.

Architectural decisions:

- Reporting read models carry `BudgetItemKind` so reporting surfaces can explain budget item semantics, but they do not own or infer it.
- Kind remains independent from debit/credit direction and is not derived from adjustments, reallocations, or transaction allocations.
- The budget item archived event was also tightened to require kind because archived budget items are not expected to exist without a kind.
- The Kafka SQLite fixture change is test infrastructure only; it does not alter production persistence, event handling, or projection semantics.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetSnapshotsTests|FullyQualifiedName~ProjectionProcessingTests|FullyQualifiedName~KafkaProjectionConsumerTests|FullyQualifiedName~PostgresCompatibilityTests"` - passed, 16 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 96 tests.
- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetItemTests|FullyQualifiedName~EventPayloadRecordContractTests|FullyQualifiedName~EventContractTests|FullyQualifiedName~ProjectionProcessingTests|FullyQualifiedName~BudgetSnapshotsTests"` - passed, 25 tests after adding kind to the archive event path.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 96 tests after adding kind to the archive event path.
- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~KafkaProjectionConsumerTests.ReportingProjectionConsumerDeadLettersPoisonEventAndContinuesWithLaterEvents"` - passed, 1 test after moving the Kafka test fixture to file-backed SQLite.
- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetSnapshotsTests|FullyQualifiedName~ProjectionProcessingTests|FullyQualifiedName~KafkaProjectionConsumerTests|FullyQualifiedName~PostgresCompatibilityTests|FullyQualifiedName~EventPayloadRecordContractTests|FullyQualifiedName~EventContractTests"` - passed, 29 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 96 tests after the Kafka test infrastructure fix.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 96 tests after replacing the shared in-memory Kafka test database with a file-backed SQLite database.
- `dotnet build BudgetyTzar.sln /nr:false /p:UseSharedCompilation=false` was attempted, but the solution build hung with no output and was stopped. The full test run compiled both projects successfully before executing.

Deferred work:

- Budget adjustment kind validation.
- Transaction allocation interpretation rules.
- Reallocation availability rules and `AvailableBudget`.
- Step 13 concurrency work.

### Increment 4 - Test And Fixture Updates

Status: partially complete for Increment 2 surfaces.

- Update all salary, bonus, and income-source fixtures to `Funding`.
- Update groceries, mortgage, petrol, eating out, incidentals, holiday funds, car maintenance, Christmas, and similar fixtures to `Consumption`.
- Update API, domain, projection, audit, and event contract tests to assert kind where exposed.

### Increment 5 - Budget Adjustment Kind Invariants

- Prevent consumption items from becoming funding sources through budget adjustments.
- Prevent funding items from becoming consumption items through budget adjustments.
- Preserve legitimate corrections, refunds, reversals, underpayments, and overpayments.
- Keep the existing budget-level invariant that budget adjustment credits minus budget adjustment debits must be greater than or equal to zero as of the relevant date.

### Increment 6 - Transaction Allocation Interpretation Tests

- Add tests proving transaction allocations do not change budget item kind.
- Add tests for normal funding and consumption transaction allocation cases.
- Add tests for refund and correction cases in the opposite direction.
- Avoid adding stricter transaction-allocation command invariants until the desired actual-activity semantics are fully specified.

### Increment 7 - Reallocation Availability Invariant

Start only after `AvailableBudget` is precisely defined.

- Define AvailableBudget as of a date.
- Prevent reallocations from moving more budget away from a consumption item than its AvailableBudget.
- Add PostgreSQL-backed concurrency tests for competing reallocations if the invariant depends on current available budget.

### Increment 8 - Return To Step 13

- Resume `docs/refactor-roadmap/13-architecture-boundary-hardening.md` after the item-kind invariants are explicit.
- Revisit concurrent budget invariant correctness using the tightened aggregate and BudgetLedger semantics.
- Prefer optimistic concurrency over broad row locking once the authoritative command state is clear.

## Out Of Scope

- Adding `Financing`.
- Reworking snapshot formulas or report presentation beyond carrying kind.
- Introducing event sourcing infrastructure.
- Introducing generic repositories, mediators, or broad persistence abstractions.
