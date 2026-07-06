# Repository Guidelines

## Project Structure & Module Organization

BudgetyTzar is a .NET 9 solution centered on `BudgetyTzar.sln`. The HTTP API lives in `src/BudgetyTzar.Api`, with `Domain`, `Application`, `Features`, `Infrastructure`, and `Contracts` areas. EF migrations are under `src/BudgetyTzar.Api/Infrastructure/Persistence/Migrations`.

Tests live in `tests/BudgetyTzar.Tests`, grouped by domain area with shared infrastructure in `Support`. Event JSON schemas are in `contracts/events`. Bruno API scenarios are in `bruno`, and operational scripts are in `scripts`.

## Specification, Bounded Contexts, and Services

`SPECIFICATION.md` is the source of application requirements. Align behavior, API shape, persistence rules, test coverage, and contract changes with it. If implementation and specification disagree, treat the specification as authoritative unless the task is explicitly to update the specification.

Before changing domain behavior, API shape, persistence, reporting, authentication/authorization, audit behavior, or UI workflows, read the relevant `SPECIFICATION.md` sections rather than relying on this file as a summary. In particular:

- Use the Core Concepts, Functional Requirements, and Domain Invariants sections for business behavior and ubiquitous language.
- Use the Architecture and Data Storage sections for boundary ownership and persistence rules.
- Use the APIs section for public route ownership, request/response shape, and HTTP contract decisions.
- Use the Development Process section for increment planning and documentation expectations.
- Use the Testing Strategy section for choosing API behavior tests, unit tests, PostgreSQL integration tests, contract tests, or end-to-end tests.

Keep cross-boundary dependencies explicit and avoid cross-boundary writes. The recurring architectural pressure point is the relationship between budgets, transactions, and allocations: do not reshape ownership in code to make a local implementation easier unless the specification is updated first.

## Build, Test, and Local Commands

- `dotnet build BudgetyTzar.sln`: restores and builds all projects; warnings fail the build.
- `dotnet test`: runs the xUnit suite, including API behavior, integration, and Testcontainers-backed PostgreSQL tests.
- `docker compose up -d postgres`: starts local PostgreSQL for database-focused work.
- `dotnet run --project src/BudgetyTzar.Api`: runs the API at `http://localhost:7070` with Swagger at `/swagger`.
- `scripts/release.sh`: runs the local SemVer release tag flow.

## Coding Style & Naming Conventions

Use C# with nullable reference types and implicit usings enabled. Keep four-space indentation, `PascalCase` for public types/members, `camelCase` for locals and parameters, and consistent suffixes such as `BudgetSummaryTests` or `TransactionEndpoints`. Preserve existing layering: domain logic in `Domain`, orchestration in `Application`, HTTP details in `Features`, and external concerns in `Infrastructure`.

Model the domain using the ubiquitous language in `SPECIFICATION.md`. Prefer simple, expressive DDD patterns and make illegal states difficult to represent. Domain types should be immutable whether they are entities or value objects; mutating methods of an entity should return a new instance with the same ID. Application handlers and infrastructure code can be mutable.

For persistence changes, follow the storage rules in `SPECIFICATION.md` and keep logical table ownership clear.

## Testing Guidelines

Tests use xUnit (`[Fact]`, `[Theory]`) and should be named after observable behavior. Add focused tests near the affected area and use `Support` factories/clients.

API behavior tests are the primary regression safety net for public behavior. Exercise use cases through HTTP/JSON APIs and verify through public responses instead of inspecting SQL state.

Use unit tests selectively when they give clearer feedback than API behavior tests. Use PostgreSQL integration tests for persistence-specific behavior. SQLite or in-memory tests may help with fast feedback, but they do not replace required PostgreSQL coverage.

Contract changes should update JSON schemas and include contract/API surface tests as described in `SPECIFICATION.md`. Run `dotnet test` before opening a PR.

## Commit & Pull Request Guidelines

Use Conventional Commits: `feat: add budget export`, `fix(api): preserve version metadata`, or `feat(events)!: rename transaction event`. Enable hooks with `git config core.hooksPath .githooks`.

PRs should explain the change, note migrations or event schema changes, link issues, and include test results. Include screenshots only for visible API documentation or tooling changes.
