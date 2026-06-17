using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Data;

public static class DatabaseStartupExtensions
{
    public static async Task EnsureLocalSchemaCreatedAsync(this BudgetDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "AuditEvents" (
                "Id" uuid NOT NULL,
                "BudgetId" uuid NOT NULL,
                "BudgetPeriodId" uuid NULL,
                "AppliesToAllPeriods" boolean NOT NULL,
                "EntityType" character varying(80) NOT NULL,
                "EntityId" uuid NOT NULL,
                "EventType" character varying(120) NOT NULL,
                "Description" character varying(500) NOT NULL,
                "Details" character varying(4000) NULL,
                "OccurredAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_AuditEvents" PRIMARY KEY ("Id")
            );

            CREATE INDEX IF NOT EXISTS "IX_AuditEvents_BudgetId_BudgetPeriodId_OccurredAt"
                ON "AuditEvents" ("BudgetId", "BudgetPeriodId", "OccurredAt");

            CREATE INDEX IF NOT EXISTS "IX_AuditEvents_BudgetId_AppliesToAllPeriods_OccurredAt"
                ON "AuditEvents" ("BudgetId", "AppliesToAllPeriods", "OccurredAt");

            CREATE INDEX IF NOT EXISTS "IX_AuditEvents_EntityType_EntityId"
                ON "AuditEvents" ("EntityType", "EntityId");

            CREATE TABLE IF NOT EXISTS "TransactionImportBatches" (
                "Id" uuid NOT NULL,
                "BudgetId" uuid NOT NULL,
                "FileName" character varying(240) NOT NULL,
                "Status" character varying(24) NOT NULL,
                "RowCount" integer NOT NULL,
                "DuplicateCandidateCount" integer NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "CommittedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_TransactionImportBatches" PRIMARY KEY ("Id")
            );

            CREATE INDEX IF NOT EXISTS "IX_TransactionImportBatches_BudgetId_CreatedAt"
                ON "TransactionImportBatches" ("BudgetId", "CreatedAt");

            CREATE TABLE IF NOT EXISTS "TransactionImportRows" (
                "Id" uuid NOT NULL,
                "ImportBatchId" uuid NOT NULL,
                "RowNumber" integer NOT NULL,
                "TransactionDate" date NOT NULL,
                "Description" character varying(240) NOT NULL,
                "Amount" numeric(18,2) NOT NULL,
                "Direction" character varying(16) NOT NULL,
                "SourceAccount" character varying(120) NULL,
                "ExternalReference" character varying(160) NULL,
                "Notes" character varying(500) NULL,
                "IsDuplicateCandidate" boolean NOT NULL,
                "DuplicateReason" character varying(500) NULL,
                "IsCommitted" boolean NOT NULL,
                "TransactionId" uuid NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_TransactionImportRows" PRIMARY KEY ("Id")
            );

            CREATE INDEX IF NOT EXISTS "IX_TransactionImportRows_ImportBatchId"
                ON "TransactionImportRows" ("ImportBatchId");

            CREATE INDEX IF NOT EXISTS "IX_TransactionImportRows_TransactionId"
                ON "TransactionImportRows" ("TransactionId");
            """);
    }
}
