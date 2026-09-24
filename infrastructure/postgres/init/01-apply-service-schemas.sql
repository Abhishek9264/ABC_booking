-- Apply the hand-authored service schemas.
-- Migration files are mounted by docker-compose.yml into /migrations.
\connect tatkal_user
\i /migrations/user/001_init.sql

\connect tatkal_search
\i /migrations/search/001_init.sql

\connect tatkal_booking
\i /migrations/booking/001_init.sql

\connect tatkal_payment
\i /migrations/payment/001_init.sql
