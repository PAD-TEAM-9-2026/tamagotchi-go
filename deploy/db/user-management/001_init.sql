-- The schema of user-management 2.0.1, one statement block for every migration in order, with the history rows, so a later
-- `dotnet ef database update` runs cleanly against a volume that was initialized from this file instead of via EF.
-- Generated from the service's migrations with `dotnet ef migrations script`; regenerate it, never edit it by hand.
-- A volume that already exists is brought up to date with upgrade.sql in this folder.

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE "Users" (
    "Id" uuid NOT NULL,
    "Username" character varying(32) NOT NULL,
    "Email" text NOT NULL,
    "PasswordHash" text NOT NULL,
    "MembershipVersion" integer NOT NULL,
    CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
);

CREATE TABLE "Boosts" (
    "UserId" uuid NOT NULL,
    "BoostId" text NOT NULL,
    "Charges" integer NOT NULL,
    CONSTRAINT "PK_Boosts" PRIMARY KEY ("UserId", "BoostId"),
    CONSTRAINT "FK_Boosts_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);

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

CREATE TABLE "PackageMemberships" (
    "UserId" uuid NOT NULL,
    "PackageId" uuid NOT NULL,
    CONSTRAINT "PK_PackageMemberships" PRIMARY KEY ("UserId", "PackageId"),
    CONSTRAINT "FK_PackageMemberships_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Relationships" (
    "UserId" uuid NOT NULL,
    "OtherUserId" uuid NOT NULL,
    "Type" integer NOT NULL,
    "Version" integer NOT NULL,
    CONSTRAINT "PK_Relationships" PRIMARY KEY ("UserId", "OtherUserId"),
    CONSTRAINT "FK_Relationships_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Wallets" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "PackageId" uuid,
    "Amount" bigint NOT NULL,
    CONSTRAINT "PK_Wallets" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Wallets_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_FriendRequests_FromUserId" ON "FriendRequests" ("FromUserId");

CREATE INDEX "IX_FriendRequests_ToUserId" ON "FriendRequests" ("ToUserId");

CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");

CREATE UNIQUE INDEX "IX_Users_Username" ON "Users" ("Username");

CREATE INDEX "IX_Wallets_UserId" ON "Wallets" ("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260922184540_InitialCreate', '10.0.12');

COMMIT;

START TRANSACTION;
ALTER TABLE "Wallets" ADD "DailyCreditedAmount" bigint NOT NULL DEFAULT 0;

ALTER TABLE "Wallets" ADD "DailyCreditedOn" date;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260923202809_AddWalletDailyCreditTracking', '10.0.12');

COMMIT;

START TRANSACTION;
CREATE TABLE "UserRoles" (
    "UserId" uuid NOT NULL,
    "Role" character varying(64) NOT NULL,
    CONSTRAINT "PK_UserRoles" PRIMARY KEY ("UserId", "Role"),
    CONSTRAINT "FK_UserRoles_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261006181859_AddUserRoles', '10.0.12');

COMMIT;

START TRANSACTION;
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

CREATE INDEX "IX_RefreshTokens_FamilyId" ON "RefreshTokens" ("FamilyId");

CREATE UNIQUE INDEX "IX_RefreshTokens_TokenHash" ON "RefreshTokens" ("TokenHash");

CREATE INDEX "IX_RefreshTokens_UserId" ON "RefreshTokens" ("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261006185410_AddRefreshTokens', '10.0.12');

COMMIT;

START TRANSACTION;
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261006190022_MarkRefreshTokenUsedAtAsConcurrencyToken', '10.0.12');

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
VALUES ('20261007211808_AddIdempotencyReceipts', '10.0.12');

COMMIT;

START TRANSACTION;
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

CREATE INDEX "IX_OutboxMessages_NextAttemptAt_Id" ON "OutboxMessages" ("NextAttemptAt", "Id") WHERE "PublishedAt" IS NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261007214819_AddOutboxMessages', '10.0.12');

COMMIT;

START TRANSACTION;
CREATE TABLE "ConsumedBoosts" (
    "BattleId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "BoostId" character varying(64) NOT NULL,
    "RemainingCharges" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ConsumedBoosts" PRIMARY KEY ("BattleId", "UserId", "BoostId")
);

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

CREATE UNIQUE INDEX "IX_CurrencyCredits_UserId_Reason_ReferenceId" ON "CurrencyCredits" ("UserId", "Reason", "ReferenceId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261008193531_AddEffectLedgers', '10.0.12');

COMMIT;

