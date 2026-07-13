# BudgetyTzar

BudgetyTzar is a personal budgeting application for planning how money should be used, recording what actually happened, and comparing the two.

The project is a .NET 9 HTTP API with in-memory runtime persistence and a PostgreSQL persistence foundation. PostgreSQL currently has durable schema support plus transaction and allocation repository adapters, while the default application composition remains in memory. The application is focused on a small budgeting domain made up of budgets, budget items, transactions, and transaction allocations. The aim is to keep the model simple and expressive while evolving the architecture through small, well-tested changes.

## Documentation

- [Specification](SPECIFICATION.md) defines product rules and externally observable behaviour.
- [Architecture](docs/architecture.md) explains where code belongs and why.
- [Contributing](CONTRIBUTING.md) defines how to write code and tests in this repository.
- [Agent guidance](AGENTS.md) provides short routing and review guidance for GenAI and reviewers.

Read the relevant sections of the specification before changing domain behaviour or public APIs, and keep the architecture guide aligned with structural changes.

## Project Structure

- `src/BudgetyTzar.Api` contains the HTTP API, domain model, feature handlers, HTTP contracts, reporting, and in-memory persistence.
- `src/BudgetyTzar.Api/Persistence/PostgreSql` contains the EF Core DbContext, migrations, storage records, and PostgreSQL persistence adapters.
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

### Authentication Configuration

Local development needs no identity-provider setup. Without bearer configuration the
API uses a rejecting default scheme, so business endpoints return `401 Unauthorized`.

Deployed environments can enable JWT bearer/OIDC-compatible authentication with
configuration:

```bash
Authentication__Bearer__Enabled=true
Authentication__Bearer__Authority=https://identity.example.com/
Authentication__Bearer__Audience=budgetytzar-api
Authentication__Bearer__UserIdClaim=sub
```

`Authority` should point at the trusted issuer metadata endpoint root. `Audience`
identifies this API. `UserIdClaim` selects the stable authenticated claim used to derive
the internal BudgetyTzar application user; use the identity provider's stable
non-reassignable user identifier, such as `sub` or `oid`. If the issuer cannot be
discovered from authority metadata, set `Authentication__Bearer__Issuer` explicitly
alongside `Authority` or `Authentication__Bearer__MetadataAddress`; `Issuer` alone is
not a signing-key metadata source.
Additional accepted audiences can be configured as
`Authentication__Bearer__ValidAudiences__0`, `Authentication__Bearer__ValidAudiences__1`,
and so on. `Authentication__Bearer__RequireHttpsMetadata` defaults to `true`.

BudgetyTzar does not store passwords, issue login or refresh tokens, or expose owner
identity in existing budget, transaction, allocation, or summary response contracts.
Domain repositories continue to enforce ownership from the resolved internal current
user.

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

Persistence is currently in memory by default. All budgets, transactions, and
allocations created through the container are lost when it stops or restarts.
PostgreSQL migrations and selected adapters exist for durable storage groundwork, but
the default application composition still uses the in-memory repositories.

Run the PostgreSQL schema migration tests with Docker available:

```bash
dotnet test --filter PostgreSqlSchemaTests
```

Create or update EF Core migrations with a local tooling connection string:

```bash
BUDGETYTZAR_MIGRATIONS_CONNECTION_STRING="Host=localhost;Database=budgetytzar;Username=postgres;Password=postgres" \
  dotnet ef migrations add <MigrationName> \
  --project src/BudgetyTzar.Api \
  --startup-project src/BudgetyTzar.Api \
  --context BudgetyTzarDbContext \
  --output-dir Persistence/PostgreSql/Migrations
```

## Observability

Every API response includes an `X-Correlation-ID` header. Clients may send a single
correlation ID containing only ASCII letters, digits, hyphen, underscore, or period;
otherwise the API generates one and returns it in the response.

The API emits structured request logs, OpenTelemetry traces, and metrics under the
service and meter name `BudgetyTzar.Api`. Exporters are disabled by default so local
development and tests do not require a collector. For local inspection, set:

```bash
dotnet run --project src/BudgetyTzar.Api --Observability:ConsoleExporterEnabled=true
```

For production OpenTelemetry Protocol export, configure an endpoint:

```bash
dotnet run --project src/BudgetyTzar.Api --Observability:OtlpEndpoint=http://localhost:4317
```

The custom metric names are documented in [the specification](SPECIFICATION.md).
Telemetry must avoid transaction descriptions, monetary amounts, resource IDs, raw
owner identities, request bodies, response bodies, and raw resource URLs.

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
