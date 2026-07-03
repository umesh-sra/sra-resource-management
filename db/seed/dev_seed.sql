-- dev_seed.sql
-- LOCAL DEVELOPMENT ONLY — do not run against staging or production.
--
-- Populates representative reference data and a small, internally-consistent
-- set of clients, projects, resources, and allocations so the API and UI have
-- something to render. Safe to re-run: it truncates the business tables first.
--
-- Run AFTER V001__initial_schema.sql.

BEGIN;

-- Reset business data (reference + core) but leave schema_migration intact.
TRUNCATE allocation, project, resource, client,
         department, location, job_title, skill RESTART IDENTITY CASCADE;

-- ---------------------------------------------------------------------------
-- Reference data
-- ---------------------------------------------------------------------------
INSERT INTO department (value) VALUES
  ('Engineering'), ('Design'), ('Delivery'), ('Quality Assurance'), ('Operations');

INSERT INTO location (value) VALUES
  ('Sydney'), ('Melbourne'), ('Brisbane'), ('Remote');

INSERT INTO job_title (value) VALUES
  ('Software Engineer'), ('Senior Software Engineer'), ('Tech Lead'),
  ('Project Manager'), ('UX Designer'), ('QA Engineer'), ('Business Analyst');

INSERT INTO skill (value) VALUES
  ('C#'), ('.NET'), ('Vue.js'), ('TypeScript'), ('PostgreSQL'),
  ('Azure'), ('UX Design'), ('Test Automation'), ('Project Management');

-- ---------------------------------------------------------------------------
-- Clients
-- ---------------------------------------------------------------------------
INSERT INTO client (id, name) VALUES
  ('11111111-1111-1111-1111-111111111111', 'Acme Corporation'),
  ('22222222-2222-2222-2222-222222222222', 'Globex Pty Ltd');

-- ---------------------------------------------------------------------------
-- Projects
-- ---------------------------------------------------------------------------
INSERT INTO project (id, client_id, name, code, start_date, end_date, budget, remaining, billable, status) VALUES
  ('aaaaaaa1-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111',
   'Customer Portal Rebuild', 'ACME-001', '2026-07-01', '2026-12-19', 480000, 480000, true, 'active'),
  ('aaaaaaa2-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111',
   'Data Warehouse Migration', 'ACME-002', '2026-09-01', '2027-03-31', 320000, 320000, true, 'planned'),
  ('bbbbbbb1-0000-0000-0000-000000000001', '22222222-2222-2222-2222-222222222222',
   'Mobile App MVP', 'GLBX-001', '2026-06-15', '2026-10-31', 210000, 210000, true, 'active');

-- ---------------------------------------------------------------------------
-- Resources
-- ---------------------------------------------------------------------------
INSERT INTO resource (id, name, email, primary_job_title, secondary_job_title, status,
                      department, location, skills, availability_hours_per_week, working_days) VALUES
  ('c0000001-0000-0000-0000-000000000001', 'Ava Nguyen', 'ava.nguyen@sra.com.au',
   'Senior Software Engineer', 'Tech Lead', 'active', 'Engineering', 'Sydney',
   ARRAY['C#','.NET','PostgreSQL','Azure'], 38, ARRAY['monday','tuesday','wednesday','thursday','friday']::day_of_week[]),
  ('c0000002-0000-0000-0000-000000000002', 'Liam Patel', 'liam.patel@sra.com.au',
   'Software Engineer', NULL, 'active', 'Engineering', 'Melbourne',
   ARRAY['Vue.js','TypeScript','C#'], 38, ARRAY['monday','tuesday','wednesday','thursday','friday']::day_of_week[]),
  ('c0000003-0000-0000-0000-000000000003', 'Sofia Rossi', 'sofia.rossi@sra.com.au',
   'UX Designer', NULL, 'active', 'Design', 'Remote',
   ARRAY['UX Design'], 30, ARRAY['monday','tuesday','wednesday']::day_of_week[]),
  ('c0000004-0000-0000-0000-000000000004', 'Noah Brown', 'noah.brown@sra.com.au',
   'QA Engineer', NULL, 'onLeave', 'Quality Assurance', 'Brisbane',
   ARRAY['Test Automation'], 38, ARRAY['monday','tuesday','wednesday','thursday','friday']::day_of_week[]);

-- ---------------------------------------------------------------------------
-- Allocations
-- ---------------------------------------------------------------------------
INSERT INTO allocation (project_id, resource_id, start_date, end_date, effort, effort_unit, role_on_project, billable) VALUES
  ('aaaaaaa1-0000-0000-0000-000000000001', 'c0000001-0000-0000-0000-000000000001',
   '2026-07-01', '2026-12-19', 30, 'hoursPerWeek', 'Tech Lead', true),
  ('aaaaaaa1-0000-0000-0000-000000000001', 'c0000002-0000-0000-0000-000000000002',
   '2026-07-01', '2026-10-31', 38, 'hoursPerWeek', 'Full-stack Engineer', true),
  ('aaaaaaa1-0000-0000-0000-000000000001', 'c0000003-0000-0000-0000-000000000003',
   '2026-07-01', '2026-08-31', 20, 'hoursPerWeek', 'UX Designer', true),
  -- Ava is also on the Globex project — overlapping window pushes her over 38h
  -- (30 + 15), which the API should surface as an over-allocation warning.
  ('bbbbbbb1-0000-0000-0000-000000000001', 'c0000001-0000-0000-0000-000000000001',
   '2026-07-01', '2026-10-31', 15, 'hoursPerWeek', 'Technical Advisor', true);

COMMIT;
