# 00 - Refactor Principles

## Goal

Guide the incremental refactor toward feature-oriented vertical slices and an event-sourced direction while preserving current behavior.

## Scope

- Apply to every roadmap step.
- Favor business capability organization over technical layering.
- Move existing code before rewriting it.
- Improve business logic placement when a touched slice has an obvious domain home.
- Keep `Shared` small and limited to genuine cross-cutting plumbing.

## Out of scope

- Big-bang restructuring.
- New mediator layers, base classes, generic repositories, factories, or event-sourcing frameworks.
- Event-store-backed repository abstractions unless required by the specific slice being refactored.
- API, event contract, or database schema changes unless explicitly planned later.

## Files likely affected

- `src/BudgetyTzar.Api/Features/**`
- `src/BudgetyTzar.Api/Domain/**`
- `src/BudgetyTzar.Api/Application/**`
- `src/BudgetyTzar.Api/Infrastructure/**`
- `tests/BudgetyTzar.Tests/**`

## Invariants to preserve

- HTTP routes, status codes, request bodies, and response bodies.
- Event type names, payload records, JSON schemas, topics, and envelope shape.
- Database table names, column names, indexes, and existing migrations.
- Current test behavior.
- Feature code should not depend on Kafka, JSON envelopes, retries, or transport formats.

## Implementation checklist

- Keep each change review-sized.
- Prefer concrete code over abstractions.
- Keep command handlers focused on orchestration.
- Move business rules toward aggregates, value objects, or concrete domain services.
- Keep projections free of business decisions.
- Leave TODOs for future event-sourcing extension points instead of speculative infrastructure.

## Tests to run

- Run focused tests for the touched capability.
- Run `dotnet test --no-restore` before completing a step.

## Completion notes

- Every increment should summarize what changed, why it changed, tests run, and what remains.
