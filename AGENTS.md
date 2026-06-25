# Repository Guidelines

## Project Structure & Module Organization

BudgetyTzar is a .NET 9 solution centered on `BudgetyTzar.sln`. The HTTP API lives in `src/BudgetyTzar.Api`, with `Domain`, `Application`, `Features`, `Infrastructure`, and `Contracts` areas. EF migrations are under `src/BudgetyTzar.Api/Infrastructure/Persistence/Migrations`.

Tests live in `tests/BudgetyTzar.Tests`, grouped by domain area with shared infrastructure in `Support`. Event JSON schemas are in `contracts/events`. Bruno API scenarios are in `bruno`, and operational scripts are in `scripts`.

## Architecture & Module Boundaries

The application follows vertical slice architecture, coined by Jimmy Bogard. `SPECIFICATION.md` is the source of application requirements; align behavior, API shape, and event contracts with it.

There are four modules: budgets, transactions, reporting, and audits. Each module should own its own database and database context; modules must not share data directly. Communicate across boundaries with events. Budgets and transactions are event producers with read/write endpoints. Reporting and audits are event consumers with read-only APIs.

## Build, Test, and Development Commands

- `dotnet build BudgetyTzar.sln`: restores and builds all projects; warnings fail the build.
- `dotnet test`: runs the xUnit suite, including integration and Testcontainers-backed PostgreSQL tests.
- `docker compose up -d`: starts local PostgreSQL, Redpanda, and Kafka UI dependencies.
- `docker compose up -d postgres`: starts only PostgreSQL for database-focused work.
- `dotnet run --project src/BudgetyTzar.Api`: runs the API at `http://localhost:7070` with Swagger at `/swagger`.
- `scripts/release.sh`: runs the local SemVer release tag flow.

To run without Kafka locally, set `Outbox__PublisherEnabled=false`, `Projections__ConsumerEnabled=false`, and `Projections__UseProjectionBackedReports=false`. Keep secrets and machine-specific connection strings in environment variables, not source files.

## Coding Style & Naming Conventions

Use C# with nullable reference types and implicit usings enabled. Keep four-space indentation, `PascalCase` for public types/members, `camelCase` for locals and parameters, and consistent suffixes such as `BudgetSnapshotsTests` or `TransactionEndpoints`. Preserve existing layering: domain logic in `Domain`, orchestration in `Application`, HTTP details in `Features`, and external concerns in `Infrastructure`.

## Testing Guidelines

Tests use xUnit (`[Fact]`, `[Theory]`) and should be named after observable behavior. Add focused tests near the affected area and use `Support` factories/clients. Contract changes should update JSON schemas and include contract/API surface tests. Run `dotnet test` before opening a PR.

## Commit & Pull Request Guidelines

Use Conventional Commits: `feat: add budget export`, `fix(api): preserve version metadata`, or `feat(events)!: rename transaction event`. Enable hooks with `git config core.hooksPath .githooks`.

PRs should explain the change, note migrations or event schema changes, link issues, and include test results. Include screenshots only for visible API documentation or tooling changes.
