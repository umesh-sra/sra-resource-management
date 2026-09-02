-- V002__reference_app_model.sql
-- SRA-RMS — extend the data model to cover the reference application (screens/).
--
-- Target: PostgreSQL 13+. Apply after V001.
-- Source of truth: docs/Requirements.md (§3.3 Resource, §3.6-§3.9) and
--                  docs/openapi.yaml (component schemas + enums).
--
-- Adds
--   * project_phase / project_milestone  — the Phases and Milestones tabs of
--     the New Project dialog (new_project_phase.png, new_project_milestone.png).
--   * time_off                            — the Time Off panel on the dashboard
--     and the leave blocks drawn on the Schedule (dashboard.png, schedule.png).
--   * activity_type reference pick-list   — the Activity Types field on a
--     project (new_project.png), following the V001 pick-list pattern.
--   * Richer resource profile             — the Overview / Extra Details /
--     Scheduling / Financial groups of the person panel (person_overview.png,
--     new_person_part1.png, new_person_part2.png).
--   * Project budget type + hour budgets  — the Budget tab (new_project_budget.png).
--   * Per-allocation hourly rate          — "per person rates are managed in the
--     Team Tab" (new_project_budget.png).
--
-- Deliberately NOT added: the reference's Permissions Role, Invitation Status
-- and Last Login. Authentication is delegated to Entra ID and roles are derived
-- from AD group membership (FR-AUTH-1/2), so an application-local permissions
-- column would contradict the authorisation design and create a second, stale
-- source of truth. See docs/Requirements.md §3.3 note.
--
-- All columns are added nullable or with defaults, so the migration is
-- backward-compatible: V001-era rows and existing API clients keep working.

BEGIN;

-- ---------------------------------------------------------------------------
-- Enums  (labels mirror docs/openapi.yaml)
-- ---------------------------------------------------------------------------
CREATE TYPE bookable_status    AS ENUM ('bookable', 'nonBookable');
CREATE TYPE time_off_type      AS ENUM ('annualLeave', 'personal', 'sick', 'publicHoliday', 'other');
CREATE TYPE milestone_status   AS ENUM ('pending', 'met', 'missed');
CREATE TYPE project_budget_type AS ENUM ('none', 'fee', 'hours');

-- ---------------------------------------------------------------------------
-- Reference data — activity types (FR-REF-1)
--
-- Same pick-list shape as department / location / job_title / skill in V001:
-- projects store the chosen *values* as text[], not foreign keys.
-- ---------------------------------------------------------------------------
CREATE TABLE activity_type (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  value      text NOT NULL,
  active     boolean NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  created_by text,
  updated_by text
);
CREATE UNIQUE INDEX ux_activity_type_value_lower ON activity_type (lower(value));

-- ---------------------------------------------------------------------------
-- Resource — richer profile  (Requirements §3.3)
-- ---------------------------------------------------------------------------
ALTER TABLE resource
  -- Overview group. job_role is distinct from primary_job_title: the reference
  -- shows both (e.g. Job Title "Capability Lead", Job Role "Tech Lead").
  ADD COLUMN job_role                text,
  -- Descriptive self-reference, so ON DELETE SET NULL rather than the RESTRICT
  -- used elsewhere: being named as someone's manager should not block deleting
  -- that person the way a real dependent record (an allocation) does.
  ADD COLUMN manager_id              uuid REFERENCES resource (id) ON DELETE SET NULL,
  -- Extra Details group. V001's `skills` column carries the primary skills;
  -- it keeps its name so existing skill filters (FR-RES-2) are unaffected.
  ADD COLUMN phone                   text,
  ADD COLUMN secondary_skills        text[] NOT NULL DEFAULT '{}',
  ADD COLUMN security_clearances     text[] NOT NULL DEFAULT '{}',
  ADD COLUMN security_npc_obtained_on date,
  ADD COLUMN certifications          text[] NOT NULL DEFAULT '{}',
  -- Scheduling group. time_zone holds an IANA name (e.g. 'Australia/Adelaide');
  -- public_holiday_calendar holds a region key used to expand public holidays.
  ADD COLUMN time_zone               text,
  ADD COLUMN bookable_status         bookable_status NOT NULL DEFAULT 'bookable',
  ADD COLUMN public_holiday_calendar text,
  -- Financial group.
  ADD COLUMN default_rate_hourly     numeric(10,2) CHECK (default_rate_hourly >= 0),
  -- Presentation: per-person colour, "makes them easy to find in the Schedule".
  ADD COLUMN colour                  text CHECK (colour IS NULL OR colour ~ '^#[0-9A-Fa-f]{6}$');

CREATE INDEX ix_resource_manager         ON resource (manager_id);
CREATE INDEX ix_resource_secondary_skills ON resource USING gin (secondary_skills);
CREATE INDEX ix_resource_bookable        ON resource (bookable_status);

-- ---------------------------------------------------------------------------
-- Project — budget type, activity types, presentation  (Requirements §3.2)
-- ---------------------------------------------------------------------------
ALTER TABLE project
  -- V001's `budget` column is the fee budget; budget_type says which of the
  -- three Budget-tab modes is in force. Existing rows keep working: the
  -- backfill below sets 'fee' wherever a budget was already recorded.
  ADD COLUMN budget_type    project_budget_type NOT NULL DEFAULT 'none',
  ADD COLUMN budget_hours   numeric(10,2) CHECK (budget_hours >= 0),
  ADD COLUMN remaining_hours numeric(10,2) CHECK (remaining_hours >= 0),
  ADD COLUMN activity_types text[] NOT NULL DEFAULT '{}',
  ADD COLUMN details        text,
  ADD COLUMN colour         text CHECK (colour IS NULL OR colour ~ '^#[0-9A-Fa-f]{6}$');

UPDATE project SET budget_type = 'fee' WHERE budget IS NOT NULL;

-- A fee budget needs `budget`; an hours budget needs `budget_hours`. 'none'
-- permits neither being set (Requirements §3.5).
ALTER TABLE project
  ADD CONSTRAINT ck_project_budget_type CHECK (
        (budget_type = 'none')
     OR (budget_type = 'fee'   AND budget IS NOT NULL)
     OR (budget_type = 'hours' AND budget_hours IS NOT NULL)
  );

CREATE INDEX ix_project_activity_types ON project USING gin (activity_types);

-- ---------------------------------------------------------------------------
-- Allocation — per-person billable rate  (Requirements §3.4)
-- ---------------------------------------------------------------------------
ALTER TABLE allocation
  ADD COLUMN hourly_rate numeric(10,2) CHECK (hourly_rate >= 0);

-- ---------------------------------------------------------------------------
-- Project phase  (Requirements §3.6)
--
-- A named, dated stage within a project. Phases may overlap and need not tile
-- the project window, so no exclusion constraint is applied; they are validated
-- against the project window by the application (FR-PHASE-4).
-- ---------------------------------------------------------------------------
CREATE TABLE project_phase (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id uuid NOT NULL REFERENCES project (id) ON DELETE RESTRICT,
  name       text NOT NULL,
  start_date date NOT NULL,
  end_date   date NOT NULL,
  colour     text CHECK (colour IS NULL OR colour ~ '^#[0-9A-Fa-f]{6}$'),
  sort_order integer NOT NULL DEFAULT 0,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  created_by text,
  updated_by text,
  CONSTRAINT ck_project_phase_dates CHECK (end_date >= start_date)
);
CREATE INDEX ix_project_phase_project ON project_phase (project_id, sort_order);
CREATE INDEX ix_project_phase_dates   ON project_phase (start_date, end_date);

-- ---------------------------------------------------------------------------
-- Project milestone  (Requirements §3.7)
--
-- A dated checkpoint. Unlike a phase it is a point, not a range.
-- ---------------------------------------------------------------------------
CREATE TABLE project_milestone (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id uuid NOT NULL REFERENCES project (id) ON DELETE RESTRICT,
  name       text NOT NULL,
  due_date   date NOT NULL,
  status     milestone_status NOT NULL DEFAULT 'pending',
  note       text,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  created_by text,
  updated_by text
);
CREATE INDEX ix_project_milestone_project ON project_milestone (project_id, due_date);
CREATE INDEX ix_project_milestone_due     ON project_milestone (due_date);

-- ---------------------------------------------------------------------------
-- Time off  (Requirements §3.8)
--
-- Leave for one resource over a date range. hours_per_day NULL means a full
-- working day is unavailable; a value models partial-day leave.
--
-- Time off does NOT block allocation — consistent with over-allocation being a
-- warning rather than an error (FR-ALL-6) — but it reduces effective capacity
-- in the utilisation report (FR-REP-6) and is drawn on the Schedule.
-- ---------------------------------------------------------------------------
CREATE TABLE time_off (
  id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  resource_id   uuid NOT NULL REFERENCES resource (id) ON DELETE RESTRICT,
  start_date    date NOT NULL,
  end_date      date NOT NULL,
  type          time_off_type NOT NULL DEFAULT 'annualLeave',
  hours_per_day numeric(4,2) CHECK (hours_per_day IS NULL OR (hours_per_day > 0 AND hours_per_day <= 24)),
  note          text,
  created_at    timestamptz NOT NULL DEFAULT now(),
  updated_at    timestamptz NOT NULL DEFAULT now(),
  created_by    text,
  updated_by    text,
  CONSTRAINT ck_time_off_dates CHECK (end_date >= start_date)
);
CREATE INDEX ix_time_off_resource ON time_off (resource_id, start_date);
CREATE INDEX ix_time_off_dates    ON time_off (start_date, end_date);

-- ---------------------------------------------------------------------------
-- updated_at triggers for the new tables
-- ---------------------------------------------------------------------------
CREATE TRIGGER trg_activity_type_updated     BEFORE UPDATE ON activity_type     FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_project_phase_updated     BEFORE UPDATE ON project_phase     FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_project_milestone_updated BEFORE UPDATE ON project_milestone FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_time_off_updated          BEFORE UPDATE ON time_off          FOR EACH ROW EXECUTE FUNCTION set_updated_at();

INSERT INTO schema_migration (version, description)
VALUES ('002', 'reference-app model: phases, milestones, time off, activity types, richer resource/project/allocation attributes');

COMMIT;
