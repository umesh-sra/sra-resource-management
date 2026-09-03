-- V004__allocation_booking_status.sql
-- SRA-RMS — booking status on an allocation.
--
-- Target: PostgreSQL 13+. Apply after V003.
-- Source: Resource Guru's Bookings export, whose "Booking Status" column marks
--         every booking confirmed / tentative / waiting. Before this migration
--         the importer had nowhere to put it and landed every booking as firm,
--         which overstated committed capacity by roughly one booking row in six
--         of the SRA export. Settles open question 10 in docs/Requirements.md §8.
--
-- Semantics (Requirements §3.4, FR-ALL-9)
--   The status is *descriptive*. It deliberately does not change any capacity
--   arithmetic: over-allocation warnings (FR-ALL-6), the dashboard figures and
--   the utilisation ratio still count every allocation whatever its status, so
--   those numbers stay comparable across releases and a pencilled-in booking
--   still warns when it would push someone over their availability. What changes
--   is visibility: the allocations list can filter on status, the utilisation
--   report reports the unconfirmed share of allocated hours alongside the total,
--   and the Schedule and Gantt draw unconfirmed bookings as provisional.
--
--   'waiting' is Resource Guru's "waiting for approval". SRA-RMS has no approval
--   workflow, so it is carried as data rather than driving one.
--
-- DEFAULT 'confirmed'
--   Existing rows predate the column and were all entered as firm bookings, so
--   backfilling them to 'confirmed' preserves their meaning exactly. The default
--   also keeps every existing API client and INSERT working unchanged.

BEGIN;

CREATE TYPE booking_status AS ENUM ('confirmed', 'tentative', 'waiting');

ALTER TABLE allocation
  ADD COLUMN booking_status booking_status NOT NULL DEFAULT 'confirmed';

-- Partial: 'confirmed' is the overwhelming majority, so an index over the whole
-- column would not be selective enough to use. The queries worth serving are
-- "show me what is not yet firm".
CREATE INDEX ix_allocation_unconfirmed ON allocation (booking_status)
  WHERE booking_status <> 'confirmed';

INSERT INTO schema_migration (version, description)
VALUES ('004', 'booking status on allocation');

COMMIT;
