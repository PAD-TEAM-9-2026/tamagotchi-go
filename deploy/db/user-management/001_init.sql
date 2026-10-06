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

CREATE TABLE "PackageMemberships" (
    "UserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE CASCADE,
    "PackageId" uuid NOT NULL,
    PRIMARY KEY ("UserId", "PackageId")
);

CREATE TABLE "Relationships" (
    "UserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE CASCADE,
    "OtherUserId" uuid NOT NULL,
    "Type" integer NOT NULL,
    "Version" integer NOT NULL,
    PRIMARY KEY ("UserId", "OtherUserId")
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
    );