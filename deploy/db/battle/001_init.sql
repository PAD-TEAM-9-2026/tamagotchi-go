CREATE TABLE "Battles" (
    "Id" uuid PRIMARY KEY,
    "ChallengerId" uuid NOT NULL,
    "OpponentId" uuid NOT NULL,
    "Status" integer NOT NULL,
    "TurnUserId" uuid,
    "TurnExpiresAt" timestamptz,
    "ExpiresAt" timestamptz NOT NULL,
    "WinnerId" uuid,
    "LoserId" uuid,
    "SettlementStatus" integer NOT NULL,
    "AccessGrantStatus" integer NOT NULL,
    "EngagementId" uuid,
    "Version" integer NOT NULL
);

CREATE TABLE "BattleSides" (
    "Id" uuid PRIMARY KEY,
    "BattleId" uuid NOT NULL REFERENCES "Battles" ("Id") ON DELETE CASCADE,
    "UserId" uuid NOT NULL,
    "PrimaryId" uuid NOT NULL,
    "SecondaryId" uuid NOT NULL,
    "CurrentHp" integer NOT NULL,
    "MaxHp" integer NOT NULL,
    "BoostIds" text [] NOT NULL
);

CREATE TABLE "BattleAttackRecords" (
    "Id" uuid PRIMARY KEY,
    "BattleId" uuid NOT NULL REFERENCES "Battles" ("Id") ON DELETE CASCADE,
    "AttackerId" uuid NOT NULL,
    "DamageDealt" integer NOT NULL,
    "OpponentHpRemaining" integer NOT NULL,
    "CreatedAt" timestamptz NOT NULL
);

CREATE TABLE "__EFMigrationsHistory" (
    "MigrationId" character varying(150) PRIMARY KEY,
    "ProductVersion" character varying(32) NOT NULL
);

CREATE INDEX "IX_Battles_ChallengerId" ON "Battles" ("ChallengerId");

CREATE INDEX "IX_Battles_OpponentId" ON "Battles" ("OpponentId");

CREATE INDEX "IX_Battles_Status" ON "Battles" ("Status");

CREATE UNIQUE INDEX "IX_BattleSides_BattleId_UserId" ON "BattleSides" ("BattleId", "UserId");

CREATE INDEX "IX_BattleAttackRecords_BattleId" ON "BattleAttackRecords" ("BattleId");

-- Lets a real `dotnet ef database update` run cleanly later against a
-- volume that was initialized from this file instead of via EF.
INSERT INTO
    "__EFMigrationsHistory" (
        "MigrationId",
        "ProductVersion"
    )
VALUES (
        '20260924051623_InitialCreate',
        '10.0.12'
    );