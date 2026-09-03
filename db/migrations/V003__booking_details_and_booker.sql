-- V003__booking_details_and_booker.sql
-- SRA-RMS — the Details and Booker fields of the reference booking dialog.
--
-- Target: PostgreSQL 13+. Apply after V002.
-- Source: screens/shedule_booking.png and screens/shedule_timeoff.png — both
--         tabs of the day-cell dialog end with a free-text "Details" box and a
--         "Booker" person picker, below the fold of the earlier screenshots.
--
-- Adds
--   * allocation.details    — free text on a booking. time_off already has the
--     equivalent column (`note`), so only the allocation side is new.
--   * allocation.booker_id / time_off.booker_id — who arranged the entry.
--
-- Booker vs created_by
--   `created_by` is audit attribution: the authenticated principal that wrote
--   the row, stamped by the application and never user-editable. The Booker is
--   business data — the person on whose behalf the entry was made — is chosen
--   in the dialog, and may differ from whoever typed it in. They are kept
--   separate deliberately.
--
-- ON DELETE SET NULL
--   Being named as a booker is descriptive, exactly like resource.manager_id:
--   it must not block deleting a person. This is the second documented
--   exception to the RESTRICT convention used elsewhere in the schema.

BEGIN;

ALTER TABLE allocation
  ADD COLUMN details   text,
  ADD COLUMN booker_id uuid REFERENCES resource (id) ON DELETE SET NULL;

ALTER TABLE time_off
  ADD COLUMN booker_id uuid REFERENCES resource (id) ON DELETE SET NULL;

-- The FKs are nullable and non-unique; index them so deleting a resource does
-- not sequential-scan both tables to null the references out.
CREATE INDEX ix_allocation_booker ON allocation (booker_id);
CREATE INDEX ix_time_off_booker   ON time_off (booker_id);

INSERT INTO schema_migration (version, description)
VALUES ('003', 'booking details and booker on allocation and time off');

COMMIT;
