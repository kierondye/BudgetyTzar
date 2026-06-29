# 14 - Budget Item Kind

## Goal

Tighten the budget item domain model by introducing `BudgetItemKind` before continuing concurrency hardening. It is safer to start with explicit funding and consumption semantics and relax them later than to allow loose financial data that may be difficult to migrate safely.

## Status

Increment 7 implemented and awaiting review. Increment 1 documentation and specification semantics were approved before implementation, Increment 2 introduced the command/API/event contract language, Increment 3 carried kind through read models, Increment 4 updated tests and fixtures, Increment 5 added budget adjustment kind invariants, Increment 6 introduced date-effective command validation state, and Increment 7 added transaction allocation interpretation tests.

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

Status: complete.

- Update all salary, bonus, and income-source fixtures to `Funding`.
- Update groceries, mortgage, petrol, eating out, incidentals, holiday funds, car maintenance, Christmas, and similar fixtures to `Consumption`.
- Update API, domain, projection, audit, and event contract tests to assert kind where exposed.

Implementation notes:

- Reviewed budget item creation call sites across tests and production helpers; all budget item fixtures now create items explicitly as `Funding` or `Consumption`.
- Existing salary and funding-source fixtures use `Funding`.
- Existing groceries, mortgage, dining, household, retired, old category, savings, and similar spending bucket fixtures use `Consumption`.
- API, domain, event contract, projection, and snapshot tests assert kind where those surfaces expose budget item identity.
- No production code changes were needed for this increment after the Increment 2 and Increment 3 work.

Architectural decisions:

- Fixture kind is explicit domain language, not inferred from item name at runtime or from debit/credit direction.
- Test helper APIs require callers to choose the kind so new fixtures cannot silently create semantically vague budget items.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetSnapshotsTests|FullyQualifiedName~ProjectionProcessingTests|FullyQualifiedName~KafkaProjectionConsumerTests|FullyQualifiedName~PostgresCompatibilityTests|FullyQualifiedName~EventPayloadRecordContractTests|FullyQualifiedName~EventContractTests"` - passed, 29 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 96 tests.

Deferred work:

- Budget adjustment kind validation.
- Transaction allocation interpretation rules.
- Reallocation availability rules and `AvailableBudget`.
- Step 13 concurrency work.

### Increment 5 - Budget Adjustment Kind Invariants

Status: implemented, awaiting review.

- Prevent consumption items from becoming funding sources through budget adjustments.
- Prevent funding items from becoming consumption items through budget adjustments.
- Preserve legitimate corrections, refunds, reversals, underpayments, and overpayments.
- Keep the existing budget-level invariant that budget adjustment credits minus budget adjustment debits must be greater than or equal to zero as of the relevant date.

Implementation notes:

- Added command-side validation for single budget adjustment commands so each budget item's net planned adjustment position, as of the adjustment date, remains consistent with its `BudgetItemKind`.
- Consumption items may receive credit budget adjustments only while their net planned position remains consumption-side.
- Funding items may receive debit budget adjustments only while their net planned position remains funding-side.
- Existing opposite-direction corrections remain valid when they reduce prior same-kind budget without crossing zero.
- Updated older audit, archive, and event-contract fixtures that used standalone consumption credits as setup data so those credits are now valid corrections against existing consumption budget.

Architectural decisions:

- `BudgetItemKind` remains authoritative command state and is not inferred from debit or credit direction.
- The invariant is scoped to budget adjustment commands and does not define `AvailableBudget`.
- Reallocation availability and consumption-only reallocation policy remain deferred because they depend on the still-undefined `AvailableBudget` command calculation.
- Snapshot formulas, reporting presentation, event contracts, and persistence schema were not changed.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetTests|FullyQualifiedName~BudgetAdjustmentsTests|FullyQualifiedName~BudgetItemsTests|FullyQualifiedName~AuditEventProjectionTests|FullyQualifiedName~AuditAndOutboxTests|FullyQualifiedName~EventContractTests"` - passed, 25 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 101 tests.

Deferred work:

- Transaction allocation interpretation tests.
- Reallocation availability rules and `AvailableBudget`.
- Consumption-only reallocation policy, if still desired after `AvailableBudget` is defined.
- Step 13 concurrency work.

### Increment 6 - Effective Budget Command Validation Refactor

Insert this refactoring increment before adding further business-rule validation. Increment 5 intentionally exposed that some validation methods on `Budget` now behave like static helper methods: they validate a passed-in `BudgetItem` and passed-in dated adjustment state while using little or no state from the `Budget` instance itself. That blurs the distinction between `Budget` as the long-lived identity/container and the date-effective command state needed to decide whether a new adjustment or reallocation is valid.

Goal:

- Keep `Budget` as the long-lived identity/container for budget items, adjustments, reallocations, transactions, and events.
- Introduce an `EffectiveBudget`-style object, or a better repository-local name if one emerges during implementation, to represent budget state effective on a specific date for command validation.
- Move item-level kind compatibility checks toward `BudgetItem`.
- Let `EffectiveBudget` coordinate dated budget adjustment and reallocation validation using only the item balances/state needed for the target date.
- Keep `BudgetReallocation` responsible for its own shape: at least two movements, credits equal debits, and initially consumption-only movements.
- Let repository/application code hydrate `EffectiveBudget` from command-side persistence for the target date instead of loading full adjustment history into `Budget` unless the command actually requires it.

Likely implementation shape:

- Add a small command-side model such as `EffectiveBudget`, `EffectiveBudgetItem`, or equivalent naming aligned with the code once inspected.
- Represent each budget item's effective planned position as of the command date, rather than passing full adjustment history into domain methods.
- Move `Budget.ValidateBudgetItemKindForAdjustment` behavior into item/effective-state collaboration.
- Move `Budget.CanRecordAdjustment` behavior into the effective budget object while preserving the existing net planned income invariant.
- Update adjustment and reallocation handlers only where needed to hydrate and use the effective object.
- Keep tests focused on unchanged behavior and clearer ownership boundaries.

Invariants to preserve:

- The total planned funding across all funding budget items must be greater than or equal to the total planned consumption across all consumption budget items as of the relevant date.
- Consumption items must not become funding sources through budget adjustments.
- Funding items must not become consumption items through budget adjustments.
- Opposite-direction corrections remain valid when interpreted against existing same-kind budget.
- Reallocations must contain at least two movements and credits must equal debits.
- Reallocations should initially move budget between consumption items, without enforcing `AvailableBudget` until it is precisely defined.

Out of scope:

- Defining or enforcing `AvailableBudget`.
- Reallocation availability checks.
- Transaction allocation interpretation tests.
- Snapshot/report formula changes.
- Event sourcing infrastructure.
- Generic repositories, mediators, or broad persistence abstractions.
- Persistence schema changes unless a narrow implementation detail proves unavoidable.

Status: implemented, awaiting review.

Implementation notes:

- Added `EffectiveBudget` and `EffectiveBudgetItem` as small command-side domain objects for date-effective budget adjustment validation.
- `EffectiveBudget` owns the budget-level date-effective planned funding versus planned consumption check.
- `EffectiveBudget` exposes scoped item lookup through `GetBudgetItem(budgetItemId)`.
- `EffectiveBudgetItem` creates budget adjustments through `CreateAdjustment(amount, type, notes)`, using the effective budget's budget id and date by construction.
- The final API shape is `effectiveBudget.GetBudgetItem(budgetItemId)` followed by `effectiveBudgetItem.CreateAdjustment(amount, type, notes)`.
- Item lookup and adjustment creation both return non-null domain results with distinct success and failure cases.
- `BudgetItem` owns the item-kind boundary check for effective planned position, so consumption items cannot become funding sources and funding items cannot become consumption items through budget adjustments.
- Removed adjustment validation helpers from `Budget`, leaving it focused on long-lived budget identity/container behavior and budget item name validation.
- Updated the budget adjustment command handler to hydrate grouped effective planned positions from command-side `BudgetAdjustments` as of the adjustment date instead of loading full adjustment history into `Budget`.
- The budget adjustment command handler no longer creates a free-floating pending `BudgetAdjustment` and then asks `EffectiveBudget` to validate it. It hydrates effective state, retrieves a scoped effective budget item, and asks that item to create the adjustment.
- The budget adjustment command handler no longer loads a `Budget` entity solely to prove existence; it uses a lightweight existence check before hydrating effective budget state.
- Extracted the handler's EF query details into small private query/hydration methods so the handler reads as command orchestration without introducing a repository abstraction.
- Kept `BudgetReallocation` responsibility unchanged in this increment: it still validates reallocation shape and creates linked adjustments. No new reallocation availability or kind policy was added.
- No persistence schema, event contract, reporting, snapshot, or API response changes were required.

Architectural decisions:

- The final naming is `EffectiveBudget` for the date-scoped command validation state, `EffectiveBudgetItemState` for hydrated item state, `EffectiveBudgetItemLookupResult` for item lookup, and `EffectiveBudgetAdjustmentResult` for adjustment creation.
- Date-effective state is hydrated by the application handler from command-side persistence and passed into domain validation, rather than making `Budget` behave like a static validation helper.
- Item-kind compatibility belongs with `BudgetItem`; cross-item planned funding coverage belongs with `EffectiveBudget`.
- Normal domain construction no longer allows callers to choose a mismatched budget id, date, or item id when creating a command-side budget adjustment through effective budget state.
- Missing budget items are normal domain outcomes from `GetBudgetItem`; archived budget items are normal outcomes from `EffectiveBudgetItem.CreateAdjustment`.
- Defensive validation remains at the hydration boundary: effective budget items supplied to `EffectiveBudget` must belong to the effective budget.
- Query extraction stayed local to the adjustment command slice because the query shape is not yet reused elsewhere.
- The existing validation messages and HTTP behavior were preserved.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetTests|FullyQualifiedName~EffectiveBudgetTests|FullyQualifiedName~BudgetAdjustmentsTests"` - passed, 14 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 104 tests.

Deferred work:

- Transaction allocation interpretation tests.
- Reallocation availability rules and `AvailableBudget`.
- Consumption-only reallocation policy remains deferred until the command-side reallocation semantics are clarified alongside `AvailableBudget`.
- Moving effective budget hydration behind a repository-local helper if more command handlers need the same date-effective state.
- Step 13 concurrency work.

### Increment 7 - Transaction Allocation Interpretation Tests

Status: implemented, awaiting review.

- Add tests proving transaction allocations do not change budget item kind.
- Add tests for normal funding and consumption transaction allocation cases.
- Add tests for refund and correction cases in the opposite direction.
- Avoid adding stricter transaction-allocation command invariants until the desired actual-activity semantics are fully specified.

Implementation notes:

- Added API-level transaction allocation coverage for normal consumption spending, normal funding receipt, consumption-side refund/correction, and funding-side reversal/correction.
- Each case records an allocation through the transaction allocation command path and then reloads budget items to prove the target budget item's `BudgetItemKind` remains unchanged.
- Replaced the narrower opposite-direction allocation test with a four-case theory covering both normal and opposite-direction allocation semantics.
- No production code changes were required.

Architectural decisions:

- Transaction allocation direction remains actual activity data and does not infer, mutate, or flip the authoritative budget item kind.
- The increment intentionally adds tests only; it does not introduce stricter transaction-allocation command invariants while actual-activity semantics remain deliberately permissive.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~TransactionAllocationsTests"` - passed, 12 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 107 tests.

Deferred work:

- Reallocation availability rules and `AvailableBudget`.
- Consumption-only reallocation policy remains deferred until command-side reallocation semantics are clarified alongside `AvailableBudget`.
- Step 13 concurrency work.

### Increment 8 - Reallocation Availability Invariant

Start only after `AvailableBudget` is precisely defined.

- Define AvailableBudget as of a date.
- Prevent reallocations from moving more budget away from a consumption item than its AvailableBudget.
- Add PostgreSQL-backed concurrency tests for competing reallocations if the invariant depends on current available budget.

### Increment 9 - Return To Step 13

- Resume `docs/refactor-roadmap/13-architecture-boundary-hardening.md` after the item-kind invariants are explicit.
- Revisit concurrent budget invariant correctness using the tightened aggregate and BudgetLedger semantics.
- Prefer optimistic concurrency over broad row locking once the authoritative command state is clear.

## Out Of Scope

- Adding `Financing`.
- Reworking snapshot formulas or report presentation beyond carrying kind.
- Introducing event sourcing infrastructure.
- Introducing generic repositories, mediators, or broad persistence abstractions.
