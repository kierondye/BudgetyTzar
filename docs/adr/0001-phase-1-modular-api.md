# ADR 0001: Phase 1 Modular API

## Status

Accepted

## Context

The specification calls for a portfolio architecture with separate services, Kafka, PostgreSQL, Kubernetes, and equivalent .NET and Go implementations. Phase 1 is scoped to a local MVP with .NET, PostgreSQL persistence, and tests.

The product model has been refined so budgets are the root resource. Budget lines represent both debit and credit planning, transactions belong to budgets, transaction dates determine their budget period, and each budget has one currency.

## Decision

Implement Phase 1 as a single ASP.NET Core API backed by PostgreSQL. Keep explicit domain entities and endpoint boundaries for budgets, budget periods, budget lines, allocations, transactions, reallocations, and reporting so the code can be split into services in Phase 2 without changing the product language.

## Consequences

- The MVP is easier to run locally and validate quickly.
- PostgreSQL persistence is present from the start.
- Kafka, outbox records, audit timeline events, and service-owned schemas remain future Phase 2 work.
- Startup uses `EnsureCreated` for local schema creation until migrations can be generated with the .NET SDK.
- The first version intentionally avoids separate income-source modelling and mixed-currency budget complexity.
