# ADR 0001: Phase 1 Modular API

## Status

Accepted

## Context

The specification calls for a portfolio architecture with separate services, Kafka, PostgreSQL, Kubernetes, and equivalent .NET and Go implementations. Phase 1 is scoped to a local MVP with .NET, PostgreSQL persistence, durable local audit records, and tests.

The product model has been refined so budgets are the root resource. Budget lines represent debit and credit planning, transactions belong to budgets, transaction dates determine their budget period, and each budget has one currency. Budget line direction describes planning/reporting intent rather than a hard assignment boundary, so refunds, reimbursements, reversals, and corrections can be assigned to the budget line they offset.

## Decision

Implement Phase 1 as a single ASP.NET Core API backed by PostgreSQL. Keep explicit domain entities and endpoint boundaries for budgets, budget periods, budget lines, allocations, transactions, CSV imports, reallocations, adjustments, audit records, reconciliation, reporting, and CSV exports so the code can be split into services in Phase 2 without changing the product language.

## Consequences

- The MVP is easier to run locally and validate quickly.
- PostgreSQL persistence is present from the start.
- Durable audit records are local PostgreSQL records in Phase 1; Kafka-published audit events remain future Phase 2 work.
- Kafka, outbox records, service decomposition, service-owned schemas, and projection-backed reporting remain future Phase 2 work.
- Startup uses `EnsureCreated` for local schema creation until migrations can be generated with the .NET SDK.
- The first version intentionally avoids separate income-source modelling and mixed-currency budget complexity.
- Transaction assignment validation must allow opposite-direction assignments where they offset the selected budget line.
- Phase 1 includes CSV import preview and commit, duplicate detection, adjustments with reasons, archived-line history, reconciliation, basic multi-period reports, and CSV export within the modular API.
