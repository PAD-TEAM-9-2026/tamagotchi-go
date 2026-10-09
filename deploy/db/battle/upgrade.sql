-- Brings an existing battle database up to the schema of battle 2.0.1. Safe to run again: each migration is applied only
-- if its row is not in "__EFMigrationsHistory" yet, so it adds what is missing and touches nothing that is there.
-- Generated with `dotnet ef migrations script --idempotent`. Run it as the service's database user, for example
--   docker compose exec -T database psql -U ${BATTLE_DB_USER} -d ${BATTLE_DB_NAME} -v ON_ERROR_STOP=1 < deploy/db/battle/upgrade.sql
-- If migrations were applied by hand before, add their rows to "__EFMigrationsHistory" first or this will try to apply them again.

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260924051623_InitialCreate') THEN
    CREATE TABLE "Battles" (
        "Id" uuid NOT NULL,
        "ChallengerId" uuid NOT NULL,
        "OpponentId" uuid NOT NULL,
        "Status" integer NOT NULL,
        "TurnUserId" uuid,
        "TurnExpiresAt" timestamp with time zone,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "WinnerId" uuid,
        "LoserId" uuid,
        "SettlementStatus" integer NOT NULL,
        "AccessGrantStatus" integer NOT NULL,
        "EngagementId" uuid,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_Battles" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260924051623_InitialCreate') THEN
    CREATE TABLE "BattleAttackRecords" (
        "Id" uuid NOT NULL,
        "BattleId" uuid NOT NULL,
        "AttackerId" uuid NOT NULL,
        "DamageDealt" integer NOT NULL,
        "OpponentHpRemaining" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_BattleAttackRecords" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_BattleAttackRecords_Battles_BattleId" FOREIGN KEY ("BattleId") REFERENCES "Battles" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260924051623_InitialCreate') THEN
    CREATE TABLE "BattleSides" (
        "Id" uuid NOT NULL,
        "BattleId" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "PrimaryId" uuid NOT NULL,
        "SecondaryId" uuid NOT NULL,
        "CurrentHp" integer NOT NULL,
        "MaxHp" integer NOT NULL,
        "BoostIds" text[] NOT NULL,
        CONSTRAINT "PK_BattleSides" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_BattleSides_Battles_BattleId" FOREIGN KEY ("BattleId") REFERENCES "Battles" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260924051623_InitialCreate') THEN
    CREATE INDEX "IX_BattleAttackRecords_BattleId" ON "BattleAttackRecords" ("BattleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260924051623_InitialCreate') THEN
    CREATE INDEX "IX_Battles_ChallengerId" ON "Battles" ("ChallengerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260924051623_InitialCreate') THEN
    CREATE INDEX "IX_Battles_OpponentId" ON "Battles" ("OpponentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260924051623_InitialCreate') THEN
    CREATE INDEX "IX_Battles_Status" ON "Battles" ("Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260924051623_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_BattleSides_BattleId_UserId" ON "BattleSides" ("BattleId", "UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260924051623_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260924051623_InitialCreate', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008161026_AddIdempotencyReceipts') THEN
    CREATE TABLE "IdempotencyReceipts" (
        "Id" uuid NOT NULL,
        "Scope" character varying(300) NOT NULL,
        "Key" character varying(128) NOT NULL,
        "Fingerprint" character varying(64) NOT NULL,
        "Response" text NOT NULL,
        "IsError" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_IdempotencyReceipts" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008161026_AddIdempotencyReceipts') THEN
    CREATE UNIQUE INDEX "IX_IdempotencyReceipts_Scope_Key" ON "IdempotencyReceipts" ("Scope", "Key");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008161026_AddIdempotencyReceipts') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261008161026_AddIdempotencyReceipts', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008164524_AddCombatSnapshot') THEN
    ALTER TABLE "BattleSides" ADD "AttackBps" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008164524_AddCombatSnapshot') THEN
    ALTER TABLE "BattleSides" ADD "BaseDamage" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008164524_AddCombatSnapshot') THEN
    ALTER TABLE "BattleSides" ADD "CombatType" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008164524_AddCombatSnapshot') THEN
    ALTER TABLE "BattleSides" ADD "DefenseBps" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008164524_AddCombatSnapshot') THEN
    ALTER TABLE "BattleSides" ADD "TypeMultiplierBps" integer NOT NULL DEFAULT 10000;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008164524_AddCombatSnapshot') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261008164524_AddCombatSnapshot', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008200213_AddOutboxAndLifecycle') THEN
    ALTER TABLE "Battles" ADD "SettlementAttempts" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008200213_AddOutboxAndLifecycle') THEN
    ALTER TABLE "Battles" ADD "SettlementRetryAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008200213_AddOutboxAndLifecycle') THEN
    CREATE TABLE "OutboxMessages" (
        "Id" uuid NOT NULL,
        "RoutingKey" character varying(128) NOT NULL,
        "Payload" text NOT NULL,
        "CorrelationId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "PublishedAt" timestamp with time zone,
        "Attempts" integer NOT NULL,
        "LastError" character varying(256),
        "NextAttemptAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_OutboxMessages" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008200213_AddOutboxAndLifecycle') THEN
    CREATE INDEX "IX_Battles_Status_ExpiresAt" ON "Battles" ("Status", "ExpiresAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008200213_AddOutboxAndLifecycle') THEN
    CREATE INDEX "IX_Battles_Status_TurnExpiresAt" ON "Battles" ("Status", "TurnExpiresAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008200213_AddOutboxAndLifecycle') THEN
    CREATE INDEX "IX_OutboxMessages_NextAttemptAt_Id" ON "OutboxMessages" ("NextAttemptAt", "Id") WHERE "PublishedAt" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008200213_AddOutboxAndLifecycle') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261008200213_AddOutboxAndLifecycle', '10.0.12');
    END IF;
END $EF$;
COMMIT;
