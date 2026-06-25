# 04 - Transaction Command Slices

## Goal

Organize transaction write operations as feature-owned command slices.

## Scope

- Create slices for create transaction, edit transaction, ignore transaction, replace allocations, and clear allocations.
- Keep transaction-specific helper code beside the allocation capability.
- Preserve existing request type names and route behavior.

## Out of scope

- Changing transaction query endpoints.
- Introducing repositories or event-sourcing infrastructure.
- Changing transaction event contracts.
- Changing test behavior except stale namespace imports if needed.

## Files likely affected

- `src/BudgetyTzar.Api/Features/Transactions/**`
- `src/BudgetyTzar.Api/Application/Transactions/**`
- `src/BudgetyTzar.Api/Features/DependencyInjection.cs`
- `tests/BudgetyTzar.Tests/**`

## Invariants to preserve

- Transaction routes and status codes.
- Transaction event payloads and outbox behavior.
- Allocation validation and formatting behavior.
- Existing request/validator/handler type names.
- Database writes and transaction boundaries.

## Implementation checklist

- Move create transaction request/validator/handler into a create slice.
- Move edit transaction request/validator/handler into an edit slice.
- Move ignore handler into an ignore slice.
- Move replace/clear allocations request/validator/handlers into an allocation slice.
- Move transaction allocation formatting beside allocation handlers.
- Remove stale technical-layer files once no behavior remains there.
- Remove stale test imports rather than keeping empty namespace markers.

## Tests to run

- Focused transaction, contract, domain, and budget-item tests.
- `dotnet test --no-restore`

## Completion notes

- This step has been implemented for current transaction commands. Query separation is tracked separately.
