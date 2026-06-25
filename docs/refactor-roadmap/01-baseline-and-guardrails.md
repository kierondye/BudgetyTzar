# 01 - Baseline and Guardrails

## Goal

Establish a reliable behavior baseline before each refactoring increment.

## Scope

- Confirm the workspace state.
- Run the existing test suite.
- Record any flaky or environment-sensitive failures separately from code failures.

## Out of scope

- Production code changes.
- Test rewrites.
- Schema, API, or event contract changes.

## Files likely affected

- No files should be changed in this step.

## Invariants to preserve

- The repository should start from a known git state.
- Existing tests define the behavior baseline.
- Any pre-existing failures should be documented before refactoring.

## Implementation checklist

- Run `git status --short`.
- Run `dotnet test`.
- If `dotnet build BudgetyTzar.sln` hangs, use the test run as compile verification and record the hang.
- Do not proceed with code movement until baseline status is understood.

## Tests to run

- `dotnet test`

## Completion notes

- Baseline observed during the refactor: full suite passed with 71 tests.
