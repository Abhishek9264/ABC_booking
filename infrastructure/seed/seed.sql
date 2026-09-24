-- Minimal seed data for local demo/testing — run against the search-service
-- database after migrations: psql -f infrastructure/seed/seed.sql tatkal_search

INSERT INTO "Stations" ("Id","Code","Name","City") VALUES
  ('a0000000-0000-0000-0000-000000000001','NDLS','New Delhi','Delhi'),
  ('a0000000-0000-0000-0000-000000000002','BCT','Mumbai Central','Mumbai');

INSERT INTO "Trains" ("Id","Number","Name") VALUES
  ('b0000000-0000-0000-0000-000000000001','12951','Mumbai Rajdhani');

INSERT INTO "TrainRoutes" ("Id","TrainId","StationId","SequenceNumber","ArrivalOffset","DepartureOffset") VALUES
  ('c0000000-0000-0000-0000-000000000001','b0000000-0000-0000-0000-000000000001','a0000000-0000-0000-0000-000000000001',1,'00:00:00','00:00:00'),
  ('c0000000-0000-0000-0000-000000000002','b0000000-0000-0000-0000-000000000001','a0000000-0000-0000-0000-000000000002',2,'16:00:00','16:05:00');

INSERT INTO "Coaches" ("Id","TrainId","Code","Class","SeatCount") VALUES
  ('d0000000-0000-0000-0000-000000000001','b0000000-0000-0000-0000-000000000001','B1','Ac3Tier',64);

INSERT INTO "Seats" ("Id","CoachId","SeatNumber","SeatType")
SELECT gen_random_uuid(), 'd0000000-0000-0000-0000-000000000001', n, 'Regular'
FROM generate_series(1, 64) AS n;

INSERT INTO "Schedules" ("Id","TrainId","DepartureDate","TatkalWindowOpen") VALUES
  ('e0000000-0000-0000-0000-000000000001','b0000000-0000-0000-0000-000000000001','2026-10-01', true);
