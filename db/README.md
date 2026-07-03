# Database — SRA-RMS

PostgreSQL data tier for the SRA Resource Management System. Schema changes are
delivered as **versioned, forward-only migration scripts** (NFR-MAINT-2).

## Layout

```
db/
  migrations/   Versioned schema scripts, applied in filename order.
                Naming: V<nnn>__<description>.sql  (Flyway-compatible)
  seed/         Non-schema data scripts.
                dev_seed.sql is LOCAL-DEV ONLY (it truncates business tables).
```

## Design notes

- **PostgreSQL 13+** (relies on `gen_random_uuid()` and core enum/array features).
  Extensions used: `pgcrypto`, `pg_trgm`.
- **UUID** primary keys throughout, matching the OpenAPI `format: uuid` fields.
- **Enum labels mirror the OpenAPI contract tokens** (`onHold`, `onLeave`,
  `hoursPerWeek`, …) so the business layer serialises them without translation.
- **Audit attribution** (`created_by` / `updated_by`, plus timestamps) is on every
  table (NFR-AUD-1). The API sets `*_by`; `updated_at` is maintained by a trigger.
  A full before/after audit *history* table is deliberately **not** included yet —
  see open question #6 in `docs/Requirements.md`.
- **Referential integrity**: foreign keys are `ON DELETE RESTRICT`. The
  `?cascade=true` behaviour (FR-DEL) is implemented in the application by deleting
  dependents first, not by DB-level cascade.
- **Reference data** (`department`, `location`, `job_title`, `skill`) are
  admin-maintained pick-lists. Resources store the chosen *values* as `text` /
  `text[]`, mirroring the API (department/location are strings, skills a string
  array) — so these tables are not FK targets.
- **No soft-delete** yet (open question #3). Deletes are physical.

## Applying migrations

There is no local `psql` client checked in; any of these works against a
PostgreSQL reachable on `localhost:5432`.

### Option A — psql (if installed)

```bash
psql "postgresql://postgres@localhost:5432/sra_rms" -f migrations/V001__initial_schema.sql
psql "postgresql://postgres@localhost:5432/sra_rms" -f seed/dev_seed.sql   # local dev only
```

### Option B — psql via Docker (no local install)

From the `db/` directory (uses `host.docker.internal` to reach the host DB on
Windows/macOS Docker Desktop):

```bash
docker run --rm -i -e PGPASSWORD="<password>" \
  -v "$PWD":/db postgres:16 \
  psql -h host.docker.internal -U postgres -d sra_rms -f /db/migrations/V001__initial_schema.sql
```

Create the database first if it does not exist:

```bash
docker run --rm -e PGPASSWORD="<password>" postgres:16 \
  psql -h host.docker.internal -U postgres -d postgres -c "CREATE DATABASE sra_rms;"
```

> Local dev credentials live in `notes/`. Do **not** commit secrets into app
> config or these scripts — the API reads them from environment/secret store.

## Migration conventions

- One concern per migration; never edit a migration that has been applied to a
  shared environment — add a new `V<nnn>__…` script instead.
- Wrap each migration in a single `BEGIN … COMMIT` transaction.
- Record each applied version in `schema_migration` (the plain-psql workflow
  does this inline; a migration tool would use its own history table).
