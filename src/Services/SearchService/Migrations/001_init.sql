CREATE TABLE "Stations" (
    "Id" uuid PRIMARY KEY, "Code" varchar(16) NOT NULL, "Name" text NOT NULL, "City" text NOT NULL
);
CREATE UNIQUE INDEX "IX_Stations_Code" ON "Stations" ("Code");

CREATE TABLE "Trains" (
    "Id" uuid PRIMARY KEY, "Number" varchar(16) NOT NULL, "Name" text NOT NULL
);
CREATE UNIQUE INDEX "IX_Trains_Number" ON "Trains" ("Number");

CREATE TABLE "TrainRoutes" (
    "Id" uuid PRIMARY KEY,
    "TrainId" uuid NOT NULL REFERENCES "Trains"("Id") ON DELETE CASCADE,
    "StationId" uuid NOT NULL REFERENCES "Stations"("Id"),
    "SequenceNumber" int NOT NULL,
    "ArrivalOffset" interval NOT NULL,
    "DepartureOffset" interval NOT NULL
);
CREATE UNIQUE INDEX "IX_TrainRoutes_Train_Seq" ON "TrainRoutes" ("TrainId", "SequenceNumber");

CREATE TABLE "Coaches" (
    "Id" uuid PRIMARY KEY,
    "TrainId" uuid NOT NULL REFERENCES "Trains"("Id") ON DELETE CASCADE,
    "Code" varchar(8) NOT NULL,
    "Class" varchar(16) NOT NULL,
    "SeatCount" int NOT NULL
);
CREATE UNIQUE INDEX "IX_Coaches_Train_Code" ON "Coaches" ("TrainId", "Code");

CREATE TABLE "Seats" (
    "Id" uuid PRIMARY KEY,
    "CoachId" uuid NOT NULL REFERENCES "Coaches"("Id") ON DELETE CASCADE,
    "SeatNumber" int NOT NULL,
    "SeatType" varchar(16) NOT NULL DEFAULT 'Regular'
);
CREATE UNIQUE INDEX "IX_Seats_Coach_Number" ON "Seats" ("CoachId", "SeatNumber");

CREATE TABLE "Schedules" (
    "Id" uuid PRIMARY KEY,
    "TrainId" uuid NOT NULL REFERENCES "Trains"("Id"),
    "DepartureDate" date NOT NULL,
    "TatkalWindowOpen" boolean NOT NULL DEFAULT false,
    "TatkalOpensAtUtc" timestamptz NULL
);
CREATE INDEX "IX_Schedules_Train_Date" ON "Schedules" ("TrainId", "DepartureDate");
