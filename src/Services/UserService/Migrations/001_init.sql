-- Hand-authored equivalent of `dotnet ef migrations add InitialCreate`.
-- Run `dotnet ef migrations add InitialCreate` locally once the SDK is
-- available to generate the official, tooling-tracked migration; this file
-- documents the same schema so the DB can be stood up without the CLI.

CREATE TABLE "Users" (
    "Id" uuid PRIMARY KEY,
    "Email" varchar(256) NOT NULL,
    "PasswordHash" text NOT NULL,
    "FullName" text NOT NULL,
    "Phone" text NULL,
    "Role" varchar(16) NOT NULL DEFAULT 'User',
    "CreatedAtUtc" timestamptz NOT NULL DEFAULT now(),
    "UpdatedAtUtc" timestamptz NULL
);
CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");

CREATE TABLE "Passengers" (
    "Id" uuid PRIMARY KEY,
    "UserId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "FullName" text NOT NULL,
    "Age" int NOT NULL,
    "Gender" text NOT NULL,
    "IdProofNumber" text NULL
);
CREATE INDEX "IX_Passengers_UserId" ON "Passengers" ("UserId");
