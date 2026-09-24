CREATE TABLE "Payments" (
    "Id" uuid PRIMARY KEY,
    "BookingId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Amount" numeric(10,2) NOT NULL,
    "Status" varchar(16) NOT NULL DEFAULT 'Initiated',
    "ProviderTransactionId" text NULL,
    "CreatedAtUtc" timestamptz NOT NULL DEFAULT now(),
    "UpdatedAtUtc" timestamptz NULL
);
CREATE UNIQUE INDEX "IX_Payments_BookingId" ON "Payments" ("BookingId");
CREATE UNIQUE INDEX "IX_Payments_ProviderTransactionId" ON "Payments" ("ProviderTransactionId");
