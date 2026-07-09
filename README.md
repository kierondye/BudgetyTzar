# BudgetyTzar

BudgetyTzar is a personal budgeting application for planning how money should be used, recording what actually happened, and comparing the two.

The project is a .NET 9 HTTP API with in-memory persistence. It is currently focused on a small budgeting domain made up of budgets, budget items, transactions, and transaction allocations. The aim is to keep the model simple and expressive while evolving the architecture through small, well-tested changes.

## Documentation

- [Specification](SPECIFICATION.md) defines product rules and externally observable behaviour.
- [Architecture](docs/architecture.md) explains the structure, responsibilities, request flow, and extension points.
- [Contributing](CONTRIBUTING.md) provides the development workflow and a concise checklist for adding functionality.

Read the relevant sections of the specification before changing domain behaviour or public APIs, and keep the architecture guide aligned with structural changes.

## Project Structure

- `src/BudgetyTzar.Api` contains the HTTP API, domain model, feature handlers, HTTP contracts, reporting, and in-memory persistence.
- `tests/BudgetyTzar.Tests` contains the automated test suite.
- `docs` contains detailed design and extension guides.
- `scripts` contains local development and release scripts.

## Local Development

Run the API:

```bash
dotnet run --project src/BudgetyTzar.Api
```

The API is available at `http://localhost:7070`. Swagger is available at `http://localhost:7070/swagger`, and the health check is available at `http://localhost:7070/health`.

Business endpoints require a bearer token. Configure the production JWT authority,
audience, external identity provider claim, and external subject claim under
`Authentication` in configuration or environment variables. The API maps each
unique provider/subject pair to an internal application user identity before
scoping budgets, transactions, allocations, and reports. Health, runtime version,
Swagger UI, and the OpenAPI document remain available without authentication.
Automated API tests use a deterministic test-only authentication scheme and do not
depend on an external identity provider.

Run the test suite:

```bash
dotnet test
```

Build the solution:

```bash
dotnet build BudgetyTzar.sln
```

## Versioning and Releases

BudgetyTzar uses product-wide SemVer derived from Git tags and Conventional Commits.

Enable the local commit message hook once per clone:

```bash
git config core.hooksPath .githooks
```

Create a local release tag with:

```bash
scripts/release.sh
```
