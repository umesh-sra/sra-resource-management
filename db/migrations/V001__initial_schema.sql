-- V001__initial_schema.sql
-- SRA Resource Management System (SRA-RMS) — initial database schema.
--
-- Target: PostgreSQL 13+.
-- Source of truth: docs/Requirements.md (§3 data model, §3.5 integrity rules)
--                  and docs/openapi.yaml (component schemas + enums).
--
-- Conventions
--   * snake_case identifiers; UUID primary keys via gen_random_uuid().
--   * Enum *labels* mirror the OpenAPI contract tokens exactly (e.g. 'onHold',
--     'onLeave', 'hoursPerWeek') so the business layer serialises them with no
--     translation layer.
--   * Every table carries created_at / updated_at + created_by / updated_by to
--     satisfy audit attribution (NFR-AUD-1, FR-AUTH-4). updated_at is kept
--     current by the set_updated_at() trigger; *_by is set by the API.
--   * Foreign keys are ON DELETE RESTRICT. Cascading deletes are performed
--     explicitly by the application when ?cascade=true (FR-DEL); the database
--     guarantees integrity by default (409 Conflict maps to a RESTRICT error).

BEGIN;

-- ---------------------------------------------------------------------------
-- Extensions
-- ---------------------------------------------------------------------------
CREATE EXTENSION IF NOT EXISTS pgcrypto;   -- gen_random_uuid()
CREATE EXTENSION IF NOT EXISTS pg_trgm;    -- trigram GIN indexes for free-text search (FR-SRCH)

-- ---------------------------------------------------------------------------
-- Enums  (labels mirror docs/openapi.yaml)
-- ---------------------------------------------------------------------------
CREATE TYPE project_status  AS ENUM ('planned', 'active', 'onHold', 'completed', 'cancelled');
CREATE TYPE resource_status AS ENUM ('active', 'inactive', 'onLeave');
CREATE TYPE effort_unit     AS ENUM ('hoursPerWeek', 'percent');
CREATE TYPE day_of_week     AS ENUM ('monday','tuesday','wednesday','thursday','friday','saturday','sunday');

-- ---------------------------------------------------------------------------
-- Shared trigger: maintain updated_at on every UPDATE
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION set_updated_at() RETURNS trigger AS $$
BEGIN
  NEW.updated_at := now();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- ---------------------------------------------------------------------------
-- Reference data — admin-maintained pick-lists (FR-REF-1)
--
-- These back the /reference/{collection} endpoints. Resources store the chosen
-- *value* as text / text[] (the API exposes department & location as strings
-- and skills as a string array), so these tables are pick-lists, NOT
-- foreign-key targets. Each row maps to the OpenAPI ReferenceItem schema.
-- ---------------------------------------------------------------------------
CREATE TABLE department (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  value      text NOT NULL,
  active     boolean NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  created_by text,
  updated_by text
);
CREATE UNIQUE INDEX ux_department_value_lower ON department (lower(value));

CREATE TABLE location (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  value      text NOT NULL,
  active     boolean NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  created_by text,
  updated_by text
);
CREATE UNIQUE INDEX ux_location_value_lower ON location (lower(value));

CREATE TABLE job_title (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  value      text NOT NULL,
  active     boolean NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  created_by text,
  updated_by text
);
CREATE UNIQUE INDEX ux_job_title_value_lower ON job_title (lower(value));

CREATE TABLE skill (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  value      text NOT NULL,
  active     boolean NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  created_by text,
  updated_by text
);
CREATE UNIQUE INDEX ux_skill_value_lower ON skill (lower(value));

-- ---------------------------------------------------------------------------
-- Client  (Requirements §3.1)
-- ---------------------------------------------------------------------------
CREATE TABLE client (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name       text NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  created_by text,
  updated_by text
);
-- Unique, case-insensitive client name (FR-CLI-1).
CREATE UNIQUE INDEX ux_client_name_lower ON client (lower(name));

-- ---------------------------------------------------------------------------
-- Resource  (Requirements §3.3)
-- ---------------------------------------------------------------------------
CREATE TABLE resource (
  id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name                        text NOT NULL,
  email                       text NOT NULL,
  primary_job_title           text NOT NULL,
  secondary_job_title         text,
  status                      resource_status NOT NULL DEFAULT 'active',
  department                  text,
  location                    text,
  notes                       text,
  skills                      text[] NOT NULL DEFAULT '{}',
  image_url                   text,
  availability_hours_per_week numeric(5,2) NOT NULL DEFAULT 0
                                CHECK (availability_hours_per_week >= 0
                                       AND availability_hours_per_week <= 168),
  working_days                day_of_week[] NOT NULL DEFAULT '{}',
  created_at                  timestamptz NOT NULL DEFAULT now(),
  updated_at                  timestamptz NOT NULL DEFAULT now(),
  created_by                  text,
  updated_by                  text
);
-- Unique, case-insensitive email (Requirements §3.5).
CREATE UNIQUE INDEX ux_resource_email_lower ON resource (lower(email));
CREATE INDEX ix_resource_skills     ON resource USING gin (skills);            -- skill AND-filter (FR-RES-2)
CREATE INDEX ix_resource_department ON resource (department);
CREATE INDEX ix_resource_status     ON resource (status);
CREATE INDEX ix_resource_name_trgm  ON resource USING gin (name gin_trgm_ops); -- free-text search

-- ---------------------------------------------------------------------------
-- Project  (Requirements §3.2)
-- ---------------------------------------------------------------------------
CREATE TABLE project (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  client_id  uuid NOT NULL REFERENCES client (id) ON DELETE RESTRICT,
  name       text NOT NULL,
  code       text NOT NULL,
  start_date date NOT NULL,
  end_date   date NOT NULL,
  budget     numeric(14,2) CHECK (budget >= 0),
  remaining  numeric(14,2) CHECK (remaining >= 0),
  billable   boolean NOT NULL DEFAULT true,
  status     project_status NOT NULL DEFAULT 'planned',
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  created_by text,
  updated_by text,
  -- End date on or after start date (FR-PRJ-6, Requirements §3.5).
  CONSTRAINT ck_project_dates CHECK (end_date >= start_date)
);
CREATE UNIQUE INDEX ux_project_code     ON project (code);         -- unique business code (FR-PRJ-1)
CREATE INDEX        ix_project_client   ON project (client_id);
CREATE INDEX        ix_project_status   ON project (status);
CREATE INDEX        ix_project_dates    ON project (start_date, end_date);
CREATE INDEX        ix_project_name_trgm ON project USING gin (name gin_trgm_ops);

-- ---------------------------------------------------------------------------
-- Allocation  (Requirements §3.4) — join of one Resource to one Project
-- ---------------------------------------------------------------------------
CREATE TABLE allocation (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id      uuid NOT NULL REFERENCES project (id)  ON DELETE RESTRICT,
  resource_id     uuid NOT NULL REFERENCES resource (id) ON DELETE RESTRICT,
  start_date      date NOT NULL,
  end_date        date NOT NULL,
  effort          numeric(7,2) NOT NULL CHECK (effort >= 0),
  effort_unit     effort_unit NOT NULL,
  role_on_project text,
  billable        boolean NOT NULL DEFAULT true,
  created_at      timestamptz NOT NULL DEFAULT now(),
  updated_at      timestamptz NOT NULL DEFAULT now(),
  created_by      text,
  updated_by      text,
  -- Allocation end on or after start (FR-ALL-5, Requirements §3.5).
  -- NB: over-allocation (effort exceeding availability) is intentionally NOT a
  -- constraint — it is a non-blocking warning surfaced by the API (FR-ALL-6).
  CONSTRAINT ck_allocation_dates CHECK (end_date >= start_date)
);
CREATE INDEX ix_allocation_project  ON allocation (project_id);
CREATE INDEX ix_allocation_resource ON allocation (resource_id);
CREATE INDEX ix_allocation_dates    ON allocation (start_date, end_date);

-- ---------------------------------------------------------------------------
-- updated_at triggers
-- ---------------------------------------------------------------------------
CREATE TRIGGER trg_department_updated  BEFORE UPDATE ON department  FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_location_updated    BEFORE UPDATE ON location    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_job_title_updated   BEFORE UPDATE ON job_title   FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_skill_updated       BEFORE UPDATE ON skill       FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_client_updated      BEFORE UPDATE ON client      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_resource_updated    BEFORE UPDATE ON resource    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_project_updated     BEFORE UPDATE ON project     FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_allocation_updated  BEFORE UPDATE ON allocation  FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- ---------------------------------------------------------------------------
-- Migration tracking (used by the plain-psql workflow; a dedicated migration
-- tool such as Flyway/DbUp would maintain its own history table instead).
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS schema_migration (
  version     text PRIMARY KEY,
  description text NOT NULL,
  applied_at  timestamptz NOT NULL DEFAULT now()
);
INSERT INTO schema_migration (version, description) VALUES ('001', 'initial schema');

COMMIT;
