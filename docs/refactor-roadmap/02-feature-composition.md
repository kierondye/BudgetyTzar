# 02 - Feature Composition

## Goal

Move startup registration toward feature and infrastructure composition without changing runtime behavior.

## Scope

- Group DI registration by feature and infrastructure responsibility.
- Keep endpoint root mapping stable.
- Reduce direct service lists in `Program.cs`.

## Out of scope

- Moving business logic.
- Renaming public request/response types.
- Introducing module frameworks or scanning conventions beyond existing validator registration.

## Files likely affected

- `src/BudgetyTzar.Api/Program.cs`
- `src/BudgetyTzar.Api/Features/DependencyInjection.cs`
- `src/BudgetyTzar.Api/Infrastructure/DependencyInjection.cs`

## Invariants to preserve

- Same services registered with same lifetimes.
- Same hosted services registered.
- Same options bound from configuration.
- Same endpoint root under `/api`.

## Implementation checklist

- Create concrete extension methods for feature registrations.
- Create concrete extension methods for infrastructure registrations.
- Keep validator discovery behavior unchanged.
- Keep JSON enum converter registration unchanged.
- Run tests after moving registrations.

## Tests to run

- `dotnet test --no-restore`

## Completion notes

- This step has been implemented: `Program.cs` delegates to feature and infrastructure registration extensions.
