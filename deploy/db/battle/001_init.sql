-- The schema of battle 2.0.1, one statement block for every migration in order, with the history rows, so a later
-- `dotnet ef database update` runs cleanly against a volume that was initialized from this file instead of via EF.
-- Generated from the service's migrations with `dotnet ef migrations script`; regenerate it, never edit it by hand.
-- A volume that already exists is brought up to date with upgrade.sql in this folder.

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
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

CREATE INDEX "IX_BattleAttackRecords_BattleId" ON "BattleAttackRecords" ("BattleId");

CREATE INDEX "IX_Battles_ChallengerId" ON "Battles" ("ChallengerId");

CREATE INDEX "IX_Battles_OpponentId" ON "Battles" ("OpponentId");

CREATE INDEX "IX_Battles_Status" ON "Battles" ("Status");

CREATE UNIQUE INDEX "IX_BattleSides_BattleId_UserId" ON "BattleSides" ("BattleId", "UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260924051623_InitialCreate', '10.0.12');

COMMIT;

START TRANSACTION;
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

CREATE UNIQUE INDEX "IX_IdempotencyReceipts_Scope_Key" ON "IdempotencyReceipts" ("Scope", "Key");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261008161026_AddIdempotencyReceipts', '10.0.12');

COMMIT;

START TRANSACTION;
ALTER TABLE "BattleSides" ADD "AttackBps" integer NOT NULL DEFAULT 0;

ALTER TABLE "BattleSides" ADD "BaseDamage" integer NOT NULL DEFAULT 0;

ALTER TABLE "BattleSides" ADD "CombatType" integer NOT NULL DEFAULT 0;

ALTER TABLE "BattleSides" ADD "DefenseBps" integer NOT NULL DEFAULT 0;

ALTER TABLE "BattleSides" ADD "TypeMultiplierBps" integer NOT NULL DEFAULT 10000;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261008164524_AddCombatSnapshot', '10.0.12');

COMMIT;

START TRANSACTION;
ALTER TABLE "Battles" ADD "SettlementAttempts" integer NOT NULL DEFAULT 0;

ALTER TABLE "Battles" ADD "SettlementRetryAt" timestamp with time zone;

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

CREATE INDEX "IX_Battles_Status_ExpiresAt" ON "Battles" ("Status", "ExpiresAt");

CREATE INDEX "IX_Battles_Status_TurnExpiresAt" ON "Battles" ("Status", "TurnExpiresAt");

CREATE INDEX "IX_OutboxMessages_NextAttemptAt_Id" ON "OutboxMessages" ("NextAttemptAt", "Id") WHERE "PublishedAt" IS NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261008200213_AddOutboxAndLifecycle', '10.0.12');

COMMIT;

