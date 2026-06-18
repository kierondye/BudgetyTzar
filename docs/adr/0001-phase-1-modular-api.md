# ADR 0001: Phase 1 Modular API

## Status

Accepted, superseded in part by the ledger-first product model in `SPECIFICATION.md`

## Context

The specification calls for a portfolio architecture with separate services, Kafka, PostgreSQL, Kubernetes, and equivalent .NET and Go implementations. Phase 1 is scoped to a local MVP with .NET, PostgreSQL persistence, durable local audit records, and tests.

The product model has been refined again so budgets are the root resource and budgeting is represented as a dated ledger. Budget items are named buckets, not fixed debit or credit lines. Budget adjustments and transaction allocations can be debits or credits against any budget item. Budget item balances are cumulative by default, and date-range reports replace period state as the way to answer monthly or custom-period questions.

## Decision

Implement Phase 1 as a single ASP.NET Core API backed by PostgreSQL. Keep explicit domain entities and endpoint boundaries for budgets, budget items, budget adjustments, budget reallocations, transactions, transaction allocations, CSV imports, audit records, snapshots, reconciliation, reporting, and CSV exports so the code can be split into services in Phase 2 without changing the product language.

## Consequences

- The MVP is easier to run locally and validate quickly.
- PostgreSQL persistence is present from the start.
- Durable audit records are local PostgreSQL records in Phase 1; Kafka-published audit events remain future Phase 2 work.
- Kafka, outbox records, service decomposition, service-owned schemas, and projection-backed reporting remain future Phase 2 work.
- Startup uses `EnsureCreated` for local schema creation until migrations can be generated with the .NET SDK.
- The first version intentionally avoids separate income-source modelling and mixed-currency budget complexity.
- Transaction allocation validation must allow debit and credit allocations against any selected budget item.
- The older budget-period, budget-line direction, and rollover-type implementation should be migrated toward the ledger-first model rather than expanded.
- Phase 1 includes CSV import preview and commit, duplicate detection, adjustments with notes, archived-item history, snapshots, reconciliation, basic date-range reports, and CSV export within the modular API.
