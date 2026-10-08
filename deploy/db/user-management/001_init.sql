CREATE TABLE "Users" (
    "Id" uuid PRIMARY KEY,
    "Username" character varying(32) NOT NULL,
    "Email" text NOT NULL,
    "PasswordHash" text NOT NULL,
    "MembershipVersion" integer NOT NULL
);

CREATE TABLE "Boosts" (
    "UserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE CASCADE,
    "BoostId" text NOT NULL,
    "Charges" integer NOT NULL,
    PRIMARY KEY ("UserId", "BoostId")
);

CREATE TABLE "FriendRequests" (
    "Id" uuid PRIMARY KEY,
    "FromUserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
    "ToUserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
    "Status" integer NOT NULL,
    "ExpiresAt" timestamptz NOT NULL
);

CREATE TABLE "IdempotencyReceipts" (
    "Id" uuid PRIMARY KEY,
    "Scope" character varying(300) NOT NULL,
    "Key" character varying(128) NOT NULL,
    "Fingerprint" character varying(64) NOT NULL,
    "Response" text NOT NULL,
    "IsError" boolean NOT NULL,
    "CreatedAt" timestamptz NOT NULL,
    "ExpiresAt" timestamptz NOT NULL
);

CREATE TABLE "OutboxMessages" (
    "Id" uuid PRIMARY KEY,
    "RoutingKey" character varying(128) NOT NULL,
    "Payload" text NOT NULL,
    "CorrelationId" uuid NOT NULL,
    "CreatedAt" timestamptz NOT NULL,
    "PublishedAt" timestamptz,
    "Attempts" integer NOT NULL,
    "LastError" character varying(256),
    "NextAttemptAt" timestamptz NOT NULL
);

CREATE TABLE "PackageMemberships" (
    "UserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE CASCADE,
    "PackageId" uuid NOT NULL,
    PRIMARY KEY ("UserId", "PackageId")
);

CREATE TABLE "RefreshTokens" (
    "Id" uuid PRIMARY KEY,
    "UserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE CASCADE,
    "FamilyId" uuid NOT NULL,
    "TokenHash" character varying(64) NOT NULL,
    "CreatedAt" timestamptz NOT NULL,
    "ExpiresAt" timestamptz NOT NULL,
    "UsedAt" timestamptz,
    "RevokedAt" timestamptz
);

CREATE TABLE "Relationships" (
    "UserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE CASCADE,
    "OtherUserId" uuid NOT NULL,
    "Type" integer NOT NULL,
    "Version" integer NOT NULL,
    PRIMARY KEY ("UserId", "OtherUserId")
);

CREATE TABLE "UserRoles" (
    "UserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE CASCADE,
    "Role" character varying(64) NOT NULL,
    PRIMARY KEY ("UserId", "Role")
);

CREATE TABLE "Wallets" (
    "Id" uuid PRIMARY KEY,
    "UserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE CASCADE,
    "PackageId" uuid,
    "Amount" bigint NOT NULL,
    "DailyCreditedAmount" bigint NOT NULL DEFAULT 0,
    "DailyCreditedOn" date
);

CREATE TABLE "__EFMigrationsHistory" (
    "MigrationId" character varying(150) PRIMARY KEY,
    "ProductVersion" character varying(32) NOT NULL
);

CREATE INDEX "IX_FriendRequests_FromUserId" ON "FriendRequests" ("FromUserId");

CREATE INDEX "IX_FriendRequests_ToUserId" ON "FriendRequests" ("ToUserId");

CREATE UNIQUE INDEX "IX_IdempotencyReceipts_Scope_Key" ON "IdempotencyReceipts" ("Scope", "Key");

CREATE INDEX "IX_OutboxMessages_NextAttemptAt_Id" ON "OutboxMessages" ("NextAttemptAt", "Id") WHERE "PublishedAt" IS NULL;

CREATE INDEX "IX_RefreshTokens_FamilyId" ON "RefreshTokens" ("FamilyId");

CREATE UNIQUE INDEX "IX_RefreshTokens_TokenHash" ON "RefreshTokens" ("TokenHash");

CREATE INDEX "IX_RefreshTokens_UserId" ON "RefreshTokens" ("UserId");

CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");

CREATE UNIQUE INDEX "IX_Users_Username" ON "Users" ("Username");

CREATE INDEX "IX_Wallets_UserId" ON "Wallets" ("UserId");

-- Lets a real `dotnet ef database update` run cleanly later against a
-- volume that was initialized from this file instead of via EF.
INSERT INTO
    "__EFMigrationsHistory" (
        "MigrationId",
        "ProductVersion"
    )
VALUES (
        '20260922184540_InitialCreate',
        '10.0.12'
    ),
    (
        '20260923202809_AddWalletDailyCreditTracking',
        '10.0.12'
    ),
    (
        '20261006181859_AddUserRoles',
        '10.0.12'
    ),
    (
        '20261006185410_AddRefreshTokens',
        '10.0.12'
    ),
    (
        '20261006190022_MarkRefreshTokenUsedAtAsConcurrencyToken',
        '10.0.12'
    ),
    (
        '20261007211808_AddIdempotencyReceipts',
        '10.0.12'
    ),
    (
        '20261007214819_AddOutboxMessages',
        '10.0.12'
    );