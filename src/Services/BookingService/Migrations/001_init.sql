CREATE TABLE "Bookings" (
    "Id" uuid PRIMARY KEY,
    "UserId" uuid NOT NULL,
    "ScheduleId" uuid NOT NULL,
    "CoachId" uuid NOT NULL,
    "SeatId" uuid NOT NULL,
    "Status" varchar(24) NOT NULL DEFAULT 'Initiated',
    "Amount" numeric(10,2) NOT NULL,
    "SeatReservationId" uuid NULL,
    "PaymentId" uuid NULL,
    "CreatedAtUtc" timestamptz NOT NULL DEFAULT now(),
    "UpdatedAtUtc" timestamptz NULL
);
CREATE INDEX "IX_Bookings_UserId" ON "Bookings" ("UserId");

CREATE TABLE "BookingPassengers" (
    "Id" uuid PRIMARY KEY,
    "BookingId" uuid NOT NULL REFERENCES "Bookings"("Id") ON DELETE CASCADE,
    "FullName" text NOT NULL,
    "Age" int NOT NULL,
    "Gender" text NOT NULL
);

CREATE TABLE "SeatReservations" (
    "Id" uuid PRIMARY KEY,
    "ScheduleId" uuid NOT NULL,
    "CoachId" uuid NOT NULL,
    "SeatId" uuid NOT NULL,
    "BookingId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Status" varchar(16) NOT NULL DEFAULT 'Locked',
    "CreatedAtUtc" timestamptz NOT NULL DEFAULT now(),
    "ExpiresAtUtc" timestamptz NOT NULL
);
-- THE uniqueness guarantee: only one Locked/Confirmed reservation per seat per schedule.
CREATE UNIQUE INDEX "IX_SeatReservations_Active_Unique"
    ON "SeatReservations" ("ScheduleId", "CoachId", "SeatId")
    WHERE "Status" IN ('Locked', 'Confirmed');
CREATE INDEX "IX_SeatReservations_ExpiresAtUtc" ON "SeatReservations" ("ExpiresAtUtc");

CREATE TABLE "OutboxMessages" (
    "Id" uuid PRIMARY KEY,
    "Type" text NOT NULL,
    "Payload" jsonb NOT NULL,
    "CorrelationId" uuid NOT NULL,
    "CreatedAtUtc" timestamptz NOT NULL DEFAULT now(),
    "ProcessedAtUtc" timestamptz NULL,
    "RetryCount" int NOT NULL DEFAULT 0,
    "ClaimedAtUtc" timestamptz NULL,
    "ClaimedBy" uuid NULL,
    "Status" varchar(16) NOT NULL DEFAULT 'Pending'
);
CREATE INDEX "IX_OutboxMessages_ProcessedAtUtc" ON "OutboxMessages" ("ProcessedAtUtc");

CREATE TABLE "InboxMessages" (
    "MessageId" uuid NOT NULL,
    "ConsumerName" text NOT NULL,
    "ProcessedAtUtc" timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY ("MessageId", "ConsumerName")
);
CREATE INDEX "IX_InboxMessages_ProcessedAtUtc" ON "InboxMessages" ("ProcessedAtUtc");

CREATE TABLE "IdempotencyRecords" (
    "IdempotencyKey" text PRIMARY KEY,
    "UserId" uuid NOT NULL,
    "RequestHash" text NOT NULL,
    "BookingId" uuid NULL,
    "Status" varchar(16) NOT NULL DEFAULT 'Pending',
    "CreatedAtUtc" timestamptz NOT NULL DEFAULT now(),
    "ExpiresAtUtc" timestamptz NOT NULL
);
CREATE INDEX "IX_IdempotencyRecords_ExpiresAtUtc" ON "IdempotencyRecords" ("ExpiresAtUtc");

CREATE TABLE "BookingEvents" (
    "SequenceNumber" bigserial PRIMARY KEY,
    "BookingId" uuid NOT NULL,
    "EventType" text NOT NULL,
    "PayloadJson" jsonb NOT NULL,
    "OccurredAtUtc" timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX "IX_BookingEvents_BookingId_Seq" ON "BookingEvents" ("BookingId", "SequenceNumber");
