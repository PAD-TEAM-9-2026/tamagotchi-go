-- Brings an existing user-management database up to the schema of user-management 2.0.1. Safe to run again: each migration is applied only
-- if its row is not in "__EFMigrationsHistory" yet, so it adds what is missing and touches nothing that is there.
-- Generated with `dotnet ef migrations script --idempotent`. Run it as the service's database user, for example
--   docker compose exec -T database psql -U ${USER_MANAGEMENT_DB_USER} -d ${USER_MANAGEMENT_DB_NAME} -v ON_ERROR_STOP=1 < deploy/db/user-management/upgrade.sql
-- If migrations were applied by hand before, add their rows to "__EFMigrationsHistory" first or this will try to apply them again.

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260922184540_InitialCreate') THEN
    CREATE TABLE "Users" (
        "Id" uuid NOT NULL,
        "Username" character varying(32) NOT NULL,
        "Email" text NOT NULL,
        "PasswordHash" text NOT NULL,
        "MembershipVersion" integer NOT NULL,
        CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260922184540_InitialCreate') THEN
    CREATE TABLE "Boosts" (
        "UserId" uuid NOT NULL,
        "BoostId" text NOT NULL,
        "Charges" integer NOT NULL,
        CONSTRAINT "PK_Boosts" PRIMARY KEY ("UserId", "BoostId"),
        CONSTRAINT "FK_Boosts_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260922184540_InitialCreate') THEN
    CREATE TABLE "FriendRequests" (
        "Id" uuid NOT NULL,
        "FromUserId" uuid NOT NULL,
        "ToUserId" uuid NOT NULL,
        "Status" integer NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_FriendRequests" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_FriendRequests_Users_FromUserId" FOREIGN KEY ("FromUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_FriendRequests_Users_ToUserId" FOREIGN KEY ("ToUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260922184540_InitialCreate') THEN
    CREATE TABLE "PackageMemberships" (
        "UserId" uuid NOT NULL,
        "PackageId" uuid NOT NULL,
        CONSTRAINT "PK_PackageMemberships" PRIMARY KEY ("UserId", "PackageId"),
        CONSTRAINT "FK_PackageMemberships_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260922184540_InitialCreate') THEN
    CREATE TABLE "Relationships" (
        "UserId" uuid NOT NULL,
        "OtherUserId" uuid NOT NULL,
        "Type" integer NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_Relationships" PRIMARY KEY ("UserId", "OtherUserId"),
        CONSTRAINT "FK_Relationships_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260922184540_InitialCreate') THEN
    CREATE TABLE "Wallets" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "PackageId" uuid,
        "Amount" bigint NOT NULL,
        CONSTRAINT "PK_Wallets" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Wallets_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260922184540_InitialCreate') THEN
    CREATE INDEX "IX_FriendRequests_FromUserId" ON "FriendRequests" ("FromUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260922184540_InitialCreate') THEN
    CREATE INDEX "IX_FriendRequests_ToUserId" ON "FriendRequests" ("ToUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260922184540_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260922184540_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_Users_Username" ON "Users" ("Username");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260922184540_InitialCreate') THEN
    CREATE INDEX "IX_Wallets_UserId" ON "Wallets" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260922184540_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260922184540_InitialCreate', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260923202809_AddWalletDailyCreditTracking') THEN
    ALTER TABLE "Wallets" ADD "DailyCreditedAmount" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260923202809_AddWalletDailyCreditTracking') THEN
    ALTER TABLE "Wallets" ADD "DailyCreditedOn" date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260923202809_AddWalletDailyCreditTracking') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260923202809_AddWalletDailyCreditTracking', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261006181859_AddUserRoles') THEN
    CREATE TABLE "UserRoles" (
        "UserId" uuid NOT NULL,
        "Role" character varying(64) NOT NULL,
        CONSTRAINT "PK_UserRoles" PRIMARY KEY ("UserId", "Role"),
        CONSTRAINT "FK_UserRoles_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261006181859_AddUserRoles') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261006181859_AddUserRoles', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261006185410_AddRefreshTokens') THEN
    CREATE TABLE "RefreshTokens" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "FamilyId" uuid NOT NULL,
        "TokenHash" character varying(64) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "UsedAt" timestamp with time zone,
        "RevokedAt" timestamp with time zone,
        CONSTRAINT "PK_RefreshTokens" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_RefreshTokens_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261006185410_AddRefreshTokens') THEN
    CREATE INDEX "IX_RefreshTokens_FamilyId" ON "RefreshTokens" ("FamilyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261006185410_AddRefreshTokens') THEN
    CREATE UNIQUE INDEX "IX_RefreshTokens_TokenHash" ON "RefreshTokens" ("TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261006185410_AddRefreshTokens') THEN
    CREATE INDEX "IX_RefreshTokens_UserId" ON "RefreshTokens" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261006185410_AddRefreshTokens') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261006185410_AddRefreshTokens', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261006190022_MarkRefreshTokenUsedAtAsConcurrencyToken') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261006190022_MarkRefreshTokenUsedAtAsConcurrencyToken', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007211808_AddIdempotencyReceipts') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007211808_AddIdempotencyReceipts') THEN
    CREATE UNIQUE INDEX "IX_IdempotencyReceipts_Scope_Key" ON "IdempotencyReceipts" ("Scope", "Key");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007211808_AddIdempotencyReceipts') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261007211808_AddIdempotencyReceipts', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007214819_AddOutboxMessages') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007214819_AddOutboxMessages') THEN
    CREATE INDEX "IX_OutboxMessages_NextAttemptAt_Id" ON "OutboxMessages" ("NextAttemptAt", "Id") WHERE "PublishedAt" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007214819_AddOutboxMessages') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261007214819_AddOutboxMessages', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008193531_AddEffectLedgers') THEN
    CREATE TABLE "ConsumedBoosts" (
        "BattleId" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "BoostId" character varying(64) NOT NULL,
        "RemainingCharges" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_ConsumedBoosts" PRIMARY KEY ("BattleId", "UserId", "BoostId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008193531_AddEffectLedgers') THEN
    CREATE TABLE "CurrencyCredits" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Reason" character varying(32) NOT NULL,
        "ReferenceId" uuid NOT NULL,
        "Amount" integer NOT NULL,
        "NewBalance" bigint NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CurrencyCredits" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008193531_AddEffectLedgers') THEN
    CREATE TABLE "SettledBattles" (
        "BattleId" uuid NOT NULL,
        "WinnerId" uuid NOT NULL,
        "LoserId" uuid NOT NULL,
        "WinnerCredit" integer NOT NULL,
        "LoserDebit" integer NOT NULL,
        "WinnerBalance" bigint NOT NULL,
        "LoserBalance" bigint NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_SettledBattles" PRIMARY KEY ("BattleId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008193531_AddEffectLedgers') THEN
    CREATE UNIQUE INDEX "IX_CurrencyCredits_UserId_Reason_ReferenceId" ON "CurrencyCredits" ("UserId", "Reason", "ReferenceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261008193531_AddEffectLedgers') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261008193531_AddEffectLedgers', '10.0.12');
    END IF;
END $EF$;
COMMIT;
