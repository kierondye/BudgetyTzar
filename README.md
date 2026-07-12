# BudgetyTzar

BudgetyTzar is a personal budgeting application for planning how money should be used, recording what actually happened, and comparing the two.

The project is a .NET 9 HTTP API with in-memory persistence. It is currently focused on a small budgeting domain made up of budgets, budget items, transactions, and transaction allocations. The aim is to keep the model simple and expressive while evolving the architecture through small, well-tested changes.

## Documentation

- [Specification](SPECIFICATION.md) defines product rules and externally observable behaviour.
- [Architecture](docs/architecture.md) explains where code belongs and why.
- [Contributing](CONTRIBUTING.md) defines how to write code and tests in this repository.
- [Agent guidance](AGENTS.md) provides short routing and review guidance for GenAI and reviewers.

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

Business API endpoints require authentication. The built-in default authentication
scheme rejects unauthenticated requests safely until a deployment supplies its chosen
identity provider scheme. Health, runtime version, and Swagger endpoints remain public
for local and operational inspection.

Run the test suite:

```bash
dotnet test
```

Build the solution:

```bash
dotnet build BudgetyTzar.sln
```

## Container

Build the production API image from the repository root:

```bash
docker build --tag budgetytzar-api:local .
```

Run it in the background:

```bash
docker run --detach --rm \
  --name budgetytzar-api \
  --publish 8080:8080 \
  budgetytzar-api:local
```

The container runs as a non-root user, listens on port `8080`, and sets the
ASP.NET Core environment to `Production`. Swagger remains enabled in the
container and is available at `http://localhost:8080/swagger`.

Verify the health and build version endpoints:

```bash
curl --fail http://localhost:8080/health
curl --fail http://localhost:8080/api/version
```

The version response contains the product version derived from Git tags and
Conventional Commits, plus an informational version containing the source
commit. Stop the container after verification:

```bash
docker stop budgetytzar-api
```

Persistence is currently in memory. All budgets, transactions, and allocations
created through the container are lost when it stops or restarts.

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
