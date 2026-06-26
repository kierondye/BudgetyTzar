# 11 - Event-Sourced Aggregate Direction

## Goal

Move gradually toward aggregates making decisions and emitting domain events as first-class outputs.

## Scope

- Improve touched aggregates so business decisions live in domain methods.
- Keep commands as the write path.
- Leave extension points for event-store-backed loading/saving where useful.

## Out of scope

- New event-store-backed repository abstractions unless required by a specific slice.
- Aggregate base classes.
- Generic event dispatch frameworks.
- Rehydrating all aggregates from streams in one pass.
- Replacing current EF persistence in a broad refactor.

## Files likely affected

- `src/BudgetyTzar.Api/Domain/Budgeting/**`
- `src/BudgetyTzar.Api/Domain/Transactions/**`
- Touched command slices under `src/BudgetyTzar.Api/Features/**`
- `src/BudgetyTzar.Api/Infrastructure/Events/**`

## Invariants to preserve

- Current persistence behavior until a dedicated slice changes it.
- Existing domain event names and payloads.
- Command API behavior.
- Read models remain projection-owned.
- Queries do not use aggregates as the source of reporting truth.

## Implementation checklist

- Move obvious business rules from handlers into aggregate/value-object methods.
- Prefer aggregate methods that return or expose domain events where already natural.
- Keep handlers focused on load, invoke domain behavior, persist, return result.
- Add TODOs for future event-store loading/saving instead of speculative infrastructure.
- Avoid creating generic event-sourcing machinery.

## Tests to run

- Domain tests for moved rules.
- Focused command tests for touched slices.
- Event contract tests if event creation changes internally.
- `dotnet test --no-restore`

## Completion notes

- Previously not started as a dedicated step. Some existing aggregate methods already emitted domain events.
- Started with a narrow reallocation aggregate increment by moving the reallocation adjustment count and zero-sum
  balancing rules from `RecordReallocationHandler` into `BudgetReallocation`.
- Decision: add a concrete `BudgetReallocationAdjustment` domain input record and keep the handler responsible for
  mapping API request items into that domain shape. This avoids introducing mediator, repository, aggregate base, or
  event-sourcing framework abstractions.
- Decision: let `BudgetReallocation` validate the grouped adjustment invariant and create linked `BudgetAdjustment`
  rows for persistence, while the handler continues to own budget existence checks, budget-item lookup, archived-item
  eligibility, EF persistence, outbox writing, and HTTP result mapping.
- Preserved behavior: `/api/budgets/{budgetId}/reallocations`, request/response JSON, validation messages, event names,
  event payload records, JSON schemas, outbox behavior, EF mappings, migrations, database schema, projections, and
  snapshot results remain unchanged.
- Validation before implementation: baseline `dotnet test` compiled and ran 77 tests; the known flaky Kafka projection
  consumer timeout occurred in `ReportingProjectionConsumerProjectsEventsConsumedFromKafka` with 76 tests passing.
- Validation during implementation: focused `dotnet test --filter "FullyQualifiedName~BudgetReallocation"` passed with
  4 tests.
- Final validation: `dotnet build BudgetyTzar.sln` passed with 0 warnings and 0 errors; `dotnet test` passed with
  80 tests.
- Deferred follow-on: net planned spending validation in the budget adjustment handler is another candidate for a future
  Step 11 increment, but it was left unchanged to keep this increment focused on reallocations.
- Deferred follow-on: broader event-store loading/saving, identifier value objects, and cleanup of existing feature
  DTO/domain coupling remain out of scope for this increment.
- Remaining Step 11 work: continue moving obvious command-handler business rules into the owning aggregate or value
  object one slice at a time while preserving current persistence and contracts.
- Continued with a budget adjustment planning increment by moving the net planned spending invariant out of
  `RecordAdjustmentHandler` and into the budgeting domain model.
- Decision: treat signed planned adjustment value as a `BudgetAdjustment` concept through `SignedPlannedAmount`, and
  treat the cumulative planned-income/planned-spending check as a `Budget` aggregate rule through
  `CanRecordAdjustment`.
- Decision: keep the handler responsible for request primitive mapping, loading the budget and budget item, archived
  item eligibility, querying existing persisted adjustments, EF persistence, outbox writing, and HTTP result mapping.
  The handler still returns the same validation message through the existing command-result path.
- Grouping rationale: `BudgetAdjustment.SignedPlannedAmount`, `Budget.CanRecordAdjustment`, and the adjustment handler
  call-site are one tightly coupled invariant around recording planned budget movement. Moving them together avoids
  leaving half of the same planning rule split across domain and handler code.
- Preserved behavior: `/api/budgets/{budgetId}/budget-items/{budgetItemId}/adjustments`, request/response JSON, status
  codes, Swagger metadata, validation messages, event names, event payload records, JSON schemas, outbox behavior, EF
  mappings, migrations, database schema, projections, and snapshot results remain unchanged.
- Validation during implementation: focused
  `dotnet test --filter "FullyQualifiedName~BudgetTests|FullyQualifiedName~BudgetAdjustmentTests|FullyQualifiedName~BudgetAdjustmentsTests"`
  passed with 5 tests.
- Final validation: `dotnet build BudgetyTzar.sln` passed with 0 warnings and 0 errors; `dotnet test` passed with
  83 tests.
- Deferred follow-on: the handler still queries persisted adjustment history directly. Moving toward event-stream
  rehydration or a richer budget aggregate state remains out of scope until a dedicated roadmap increment is ready.
- Deferred follow-on: identifier value objects and broader command-path cleanup remain out of scope because this
  increment only addressed the budget adjustment planning invariant.
- Remaining Step 11 work: continue reviewing command handlers for business rules that can move into the owning
  aggregate, value object, or concrete domain service without changing persistence, contracts, or projection behavior.
- Continued with a transaction edit aggregate increment by moving the "transaction amount cannot be less than current
  allocated total" invariant and `TransactionEdited` event creation from `UpdateTransactionHandler` into
  `FinancialTransaction`.
- Decision: keep `UpdateTransactionHandler` responsible for loading the transaction, querying the current allocation
  total, request primitive mapping, EF persistence, outbox writing, and HTTP result mapping. The handler now asks the
  aggregate for edit validation and writes the domain event returned by the aggregate edit operation.
- Decision: keep the validation message as `FinancialTransaction.AmountBelowAllocatedTotalMessage` so the same text is
  used by the handler's validation response and the aggregate's defensive exception path.
- Grouping rationale: edit validation, mutation, and edited event construction are one transaction aggregate concern.
  Moving them together avoids leaving previous/current transaction event details in the handler while the aggregate owns
  the edited state.
- Preserved behavior: `/api/budgets/{budgetId}/transactions/{transactionId}`, request/response JSON, status codes,
  Swagger metadata, validation messages, event names, event payload records, JSON schemas, outbox behavior, EF mappings,
  migrations, database schema, projections, and audit behavior remain unchanged.
- Validation during implementation: focused
  `dotnet test --filter "FullyQualifiedName~FinancialTransactionTests|FullyQualifiedName~TransactionEditingTests"`
  passed with 7 tests.
- Final validation: `dotnet build BudgetyTzar.sln` passed with 0 warnings and 0 errors; `dotnet test` passed with
  85 tests.
- Deferred follow-on: transaction creation and ignore event creation still happen outside aggregate methods. Revisit
  them only as focused command-path increments so API and event behavior remain stable.
- Deferred follow-on: transaction allocation replacement still has duplicate-item and archived-item eligibility checks
  in the handler; only allocation total validation currently lives in `FinancialTransaction`. Split or move those rules
  only if ownership is clear without changing validation response shape.
- Remaining Step 11 work: continue moving command-path business decisions and natural domain-event creation into
  aggregates one coherent command path at a time.
- Continued with a transaction ignore aggregate increment by moving `TransactionIgnored` event creation from
  `IgnoreTransactionHandler` into `FinancialTransaction.Ignore`.
- Decision: keep `IgnoreTransactionHandler` responsible for loading the transaction, not-found handling, EF
  persistence, outbox writing, and HTTP result mapping. The aggregate now owns the ignore state transition and returns
  the domain event that describes it.
- Grouping rationale: ignore mutation and ignored-event construction are one transaction aggregate concern. This follows
  the transaction edit pattern without batching unrelated transaction creation behavior.
- Preserved behavior: `/api/budgets/{budgetId}/transactions/{transactionId}/ignore`, status codes, response body,
  Swagger metadata, event name, event payload record, JSON schema, envelope/outbox behavior, EF mappings, migrations,
  database schema, projections, and audit behavior remain unchanged.
- Validation during implementation: focused
  `dotnet test --filter "FullyQualifiedName~FinancialTransactionTests|FullyQualifiedName~AuditEventProjectionTests|FullyQualifiedName~EventContractTests"`
  passed with 11 tests.
- Final validation: `dotnet build BudgetyTzar.sln` passed with 0 warnings and 0 errors; `dotnet test` passed with
  86 tests.
- Deferred follow-on: transaction manual creation event construction still happens in `CreateTransactionHandler`; move
  it only as its own transaction-creation increment.
- Deferred follow-on: transaction allocation replacement and clearing still construct allocation events in the feature
  handler. Revisit as a focused allocation command-path increment because duplicate item validation, archived-item
  eligibility, existing allocation details, and payload formatting need careful ownership decisions.
- Remaining Step 11 work: continue moving command-path business decisions and natural domain-event creation into
  aggregates one coherent command path at a time.
- Continued with a transaction manual creation aggregate increment by moving `TransactionManuallyCreated` event
  construction from `CreateTransactionHandler` into `FinancialTransaction.CreatedEvent`.
- Decision: keep `CreateTransactionHandler` responsible for budget existence lookup, request primitive mapping, EF
  persistence, outbox writing, and HTTP response mapping. The aggregate now owns the domain event that describes the
  transaction it created.
- Grouping rationale: this is a focused transaction-creation command-path increment. It follows the edit and ignore
  event-output pattern without mixing in transaction allocation behavior, which has separate validation and payload
  formatting concerns.
- Preserved behavior: `POST /api/budgets/{budgetId}/transactions`, request/response JSON, status codes, Swagger
  metadata, validation messages, event name, event payload record, JSON schema, envelope/outbox behavior, EF mappings,
  migrations, database schema, projections, and audit behavior remain unchanged.
- Validation during implementation: focused
  `dotnet test --filter "FullyQualifiedName~FinancialTransactionTests|FullyQualifiedName~EventContractTests|FullyQualifiedName~AuditAndOutboxTests"`
  passed with 15 tests.
- Final validation: `dotnet build BudgetyTzar.sln` passed with 0 warnings and 0 errors; `dotnet test` passed with
  87 tests.
- Deferred follow-on: transaction allocation replacement and clearing remain the main transaction command paths with
  feature-owned event construction. Review them as a single allocation-focused increment only if their validation,
  existing-allocation formatting, and event output can move without changing response shape or audit details.
- Remaining Step 11 work: continue moving command-path business decisions and natural domain-event creation into
  aggregates one coherent command path at a time.
- Continued with a transaction allocation aggregate increment by moving replacement allocation validation and
  allocation event construction into `FinancialTransaction`.
- Decision: treat duplicate budget-item allocations and total allocated amount as transaction aggregate invariants.
  `FinancialTransaction.ValidateReplacementAllocations` now exposes the same validation messages for HTTP command
  results, while `ReplaceAllocations` keeps a defensive exception path with the same text.
- Decision: move `TransactionAllocationsReplaced` and `TransactionAllocationsCleared` event construction, including
  payload mapping and audit/detail formatting, into `FinancialTransaction`. This keeps allocation event output beside
  the aggregate state and follows the transaction create/edit/ignore pattern.
- Decision: keep allocation handlers responsible for loading transactions and existing allocations, budget-item
  existence checks, archived-item eligibility, EF remove/add/save orchestration, outbox writing, and HTTP response
  mapping.
- Grouping rationale: replacement validation, replacement row creation, allocation event payloads, and allocation detail
  formatting are one transaction-allocation command-path concern. Moving them together avoids leaving half of the same
  allocation behavior split between the feature handler and aggregate.
- Preserved behavior: allocation replace and clear routes, request/response JSON, status codes, Swagger metadata,
  validation messages, event names, event payload records, JSON schemas, envelope/outbox behavior, EF mappings,
  migrations, database schema, projections, and audit details remain unchanged.
- Validation during implementation: focused
  `dotnet test --filter "FullyQualifiedName~FinancialTransactionTests|FullyQualifiedName~TransactionAllocationsTests|FullyQualifiedName~EventContractTests|FullyQualifiedName~AuditAndOutboxTests"`
  passed with 27 tests.
- Final validation: `dotnet build BudgetyTzar.sln` passed with 0 warnings and 0 errors. The first full `dotnet test`
  run had the known transient Kafka/SQLite projection-consumer timeout with 89 passed and 1 failed; the focused failing
  Kafka test passed on rerun; the final full `dotnet test` passed with 90 tests.
- Deferred follow-on: budget-item archived eligibility remains outside the transaction aggregate because it depends on
  budgeting-owned item state and retrospective activity rules.
- Deferred follow-on: moving allocation inputs away from the feature request record shape can be revisited later only if
  it reduces coupling without changing API or event behavior.
- Remaining Step 11 work: review whether any command handlers still construct natural aggregate events or hold
  aggregate-owned business decisions; otherwise consider a Step 11 closure review.
- Continued with a budget item creation aggregate increment by moving the duplicate budget-item name invariant from
  `CreateBudgetItemHandler` into `Budget`.
- Decision: treat budget item name uniqueness as a budget aggregate rule. `Budget.ValidateBudgetItemName` exposes the
  same validation message for HTTP command results, while `Budget.CreateBudgetItem` keeps a defensive exception path and
  creates the trimmed `BudgetItem` through the aggregate.
- Decision: keep `CreateBudgetItemHandler` responsible for loading the budget, querying existing persisted items, EF
  persistence, outbox writing, and HTTP response mapping. The database unique index remains an infrastructure backstop
  and was not changed.
- Grouping rationale: duplicate-name validation and budget-item creation are one budget item creation command-path
  concern. Moving them together avoids leaving the aggregate rule in the handler while still keeping persistence lookup
  outside the domain model.
- Preserved behavior: `POST /api/budgets/{budgetId}/budget-items`, request/response JSON, status codes, Swagger
  metadata, validation message, event name, event payload record, JSON schema, envelope/outbox behavior, EF mappings,
  migrations, database unique index, projections, and audit behavior remain unchanged.
- Validation during implementation: focused
  `dotnet test --filter "FullyQualifiedName~BudgetTests|FullyQualifiedName~BudgetItemsTests|FullyQualifiedName~PostgresCompatibilityTests.UniqueBudgetItemNameConstraintBehavesAgainstPostgreSql"`
  passed with 8 tests.
- Final validation: `dotnet build BudgetyTzar.sln` passed with 0 warnings and 0 errors; `dotnet test` passed with
  92 tests.
- Deferred follow-on: budget-item archived eligibility stays with `BudgetItem` plus application-level lookup because
  it depends on persisted budget-item state and cross-command use by adjustments and transaction allocations.
- Remaining Step 11 work: review whether Step 11 has any remaining non-speculative aggregate moves; otherwise close the
  step with a boundary review.
- Closed Step 11 with an aggregate-direction boundary review.
- Decision: treat Step 11 as complete for the current command surface. Natural aggregate event creation and obvious
  command-handler business decisions have been moved into `Budget`, `BudgetItem`, `BudgetAdjustment`,
  `BudgetReallocation`, and `FinancialTransaction` where ownership is clear.
- Decision: intentionally leave request validation, route/status/response mapping, EF loading and saving, outbox writing,
  budget item archived eligibility lookup, and read/query validation outside aggregates. These are application,
  persistence, cross-aggregate eligibility, or read-side concerns rather than aggregate decision logic.
- Decision: do not introduce event-store loading/saving, repositories, mediator layers, aggregate base classes, generic
  domain-event dispatch, or event-sourcing frameworks. The roadmap goal for this step was directional aggregate
  ownership, not a persistence model replacement.
- Grouping rationale: this increment is documentation-only because the scan found no remaining non-speculative code move
  that would improve Step 11 boundaries without pulling in deferred work or changing behavior.
- Preserved behavior: no runtime code changed. API routes, request/response JSON, status codes, Swagger metadata,
  validation messages, event names, event payload records, JSON schemas, envelope/outbox behavior, EF mappings,
  migrations, database schema, projections, and audit behavior remain unchanged.
- Validation: `dotnet build BudgetyTzar.sln` passed with 0 warnings and 0 errors. The first full `dotnet test` run had
  the known transient Kafka/SQLite projection-consumer timeout with 91 passed and 1 failed; the focused failing Kafka
  test rerun hit the same transient; the final full `dotnet test` passed with 92 tests.
- Deferred follow-on: event-stream rehydration, richer aggregate state, identifier value objects, and any event-store
  persistence direction remain future roadmap work and should be introduced only through dedicated, behavior-preserving
  increments.
- Deferred follow-on: if archived budget item eligibility is revisited, keep budgeting ownership explicit because the
  rule spans budget adjustments and transaction allocations and depends on persisted budget-item archive state.
- Completed a final budgeting event-method cleanup after closure review.
- Decision: `BudgetAdjustment.RecordedEvent` and `BudgetReallocation.RecordedEvent` now use their own `BudgetId` state
  instead of accepting a caller-supplied budget identifier. This aligns budgeting event methods with the transaction
  aggregate event methods introduced during Step 11.
- Decision: removed the unused `BudgetReallocation.RecordedEvent(Guid, IReadOnlyList<BudgetReallocationAdjustmentPayload>)`
  overload so the public aggregate API accepts domain-shaped reallocation adjustments rather than contract payloads.
- Grouping rationale: these changes are one consistency cleanup around aggregate-owned budgeting event output. They
  reduce the chance of mismatched event aggregate IDs without changing event names, payload shape, persistence, or API
  behavior.
- Preserved behavior: budget adjustment and budget reallocation routes, request/response JSON, status codes, Swagger
  metadata, validation messages, event names, event payload records, JSON schemas, envelope/outbox behavior, EF mappings,
  migrations, database schema, projections, and audit behavior remain unchanged.
- Validation during implementation: focused
  `dotnet test --filter "FullyQualifiedName~BudgetAdjustmentTests|FullyQualifiedName~BudgetReallocationTests|FullyQualifiedName~BudgetAdjustmentsTests|FullyQualifiedName~BudgetReallocationsTests|FullyQualifiedName~EventContractTests"`
  passed with 12 tests.
- Final validation: `dotnet build BudgetyTzar.sln` passed with 0 warnings and 0 errors; `dotnet test` passed with
  94 tests.
- Remaining Step 11 work: none known for the current command surface.
