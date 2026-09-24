-- Local development bootstrap for the four service databases.
-- The postgres image executes this file only when the data volume is first initialized.
CREATE DATABASE tatkal_user;
CREATE DATABASE tatkal_search;
CREATE DATABASE tatkal_booking;
CREATE DATABASE tatkal_payment;
