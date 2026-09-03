# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project state

All three tiers are **implemented and working**: the .NET 9 API (`src/SraRms.Api`) covers every endpoint in `docs/openapi.yaml`, the Vue SPA (`web/`) covers the core screens, the initial schema and dev seed are in `db/`, and unit + integration tests pass in `tests/SraRms.Api.Tests`. When asked to build something, extend the existing code — do not re-scaffold. `TODO.md` is the living backlog (remaining features, test gaps, open questions); `docs/Review-2026-07-03.md` holds the latest code-review findings.

A **Resource Guru importer** loads the incumbent system's report export — see *Data migration* below.

The system is the **SRA Resource Management System (SRA-RMS)** — a web app for SRA (a custom software company) to manage clients, projects, resources (people), and the allocation of resources to projects, with a dashboard, Gantt visualisation, and reporting.

## Source-of-truth documents

Read these before designing or implementing anything. They are authoritative and take precedence over assumptions:

- **`docs/openapi.yaml`** — the API contract for the business layer. This is the single source of truth for the REST surface: endpoints, request/response schemas, status codes, query parameters, and role annotations (in each operation's `description`). The C# Web API **must** conform to it. Treat schema changes as contract changes.
- **`docs/Requirements.md`** — the full SRS: functional requirements (FR-* IDs), non-functional requirements (NFR-* IDs), data model, data-integrity rules, use cases, and open questions. Cite requirement IDs when implementing or discussing a feature.
- **`notes/`** — Obsidian vault with the original domain notes (`Client.md`, `Project.md`, `Resource.md`, `SRA Resource Management System.md`). Background/origin material; `docs/` supersedes it where they differ.
- **`brand/`** — SRA logos and brand guideline PDFs. The UI must follow these (NFR-USE-1).

## Architecture (planned three-tier)

- **Presentation** — Vue 3 + Vite + TypeScript SPA in `web/`. Calls the API's `/v1` surface; axios client + Pinia. See `web/README.md`.
- **Business** — C# (.NET 9) Web API implementing `docs/openapi.yaml`. Lives in `src/SraRms.Api` (solution `SraRms.sln`). See `src/SraRms.Api/README.md`.
- **Data** — PostgreSQL. Schema changes are delivered as **versioned migration scripts** (NFR-MAINT-2), not ad-hoc DDL. See `db/README.md`.

Local development database (from `notes/`): PostgreSQL on `localhost:5432`, user `postgres`. Treat the password in the notes as a local-dev-only secret — never commit credentials into application config or source. `appsettings.Development.json` (which holds it) is git-ignored.

## Commands

```bash
# API — build / run / test
dotnet build SraRms.sln
dotnet run --project src/SraRms.Api        # Swagger at http://localhost:5163/swagger
dotnet test                                # unit + integration; integration needs Docker (Testcontainers)

# Front end (web/)
cd web && npm install && npm run dev    # http://localhost:5173 (needs API on 5163)
cd web && npm run build                 # vue-tsc type-check + production bundle

# Database — apply schema + dev seed (no local psql; uses Docker against host DB).
# MSYS_NO_PATHCONV stops Git Bash mangling the container path.
MSYS_NO_PATHCONV=1 docker run --rm -i -e PGPASSWORD="<pw>" \
  -v "C:/Workspace/Claude.Projects/sra-resource-management/db":/db postgres:16 \
  psql -h host.docker.internal -U postgres -d sra_rms -v ON_ERROR_STOP=1 -f /db/migrations/V001__initial_schema.sql
```

Business dates: `App:TimeZone` (an IANA name, default `Australia/Adelaide`) sets the zone the server uses for every derived **business date** — the dashboard horizon, "current" allocations, utilisation windows. Never derive these from `DateTime.UtcNow`: in Australia that rolls the day over mid-morning, so same-day items drop out of the horizon while the SPA still shows them. Use the injected `BusinessClock` (`Services/BusinessClock.cs`). Audit `created_at`/`updated_at` are instants and correctly stay UTC. An unknown zone id fails at startup rather than quietly serving UTC dates.

Local API auth: `appsettings.Development.json` sets `Auth:Mode=Dev`, which signs every request in as a synthetic all-roles user so endpoints can be exercised without an Entra tenant. This bypass is hard-gated to the Development environment.

## Data migration

SRA's resourcing lives in **Resource Guru** today. `POST /v1/import/resource-guru`
(Administrator-only, multipart) loads its report export; the SPA's **Data Import**
screen drives it. `data-migration/README.md` is the field-level mapping and the
authority on how each source column is interpreted — read it before changing
`src/SraRms.Api/Services/Import/`.

Points that are easy to get wrong:

- **`dryRun` defaults to `true`.** The run happens inside one transaction that is
  rolled back for a dry run, so a preview exercises every constraint and reports
  exactly the counts a commit would. Keep that property: the SPA's two-step
  Analyse-then-Import flow depends on the two reports being identical.
- **The importer writes entities directly, not through the controllers**, so it
  is responsible for the invariants the endpoints enforce in code rather than in
  the schema — notably the no-overlapping-leave rule and keeping allocations
  inside their project window.
- **Resource Guru zeroes `Available Hours` on days taken by leave**, so a
  person's contract can only be read from the *non-zero* rows. Averaging over all
  rows records anyone on extended leave as having no capacity at all.
- **A booking is one row per day** in the export and a date range in SRA-RMS.
  Runs are folded using the person's own working days, so weekends join but a
  real mid-week break splits.
- **Re-runs must stay idempotent.** Natural keys do the matching, and generated
  project codes are derived from the project and client names (never from a
  counter over the input) so a second import lands on the same rows.
- Anything the model cannot hold is reported in the response's `warnings` and
  `unmappedFields` rather than dropped quietly. As of V004 nothing in the SRA
  export is dropped: `unmappedFields` comes back empty.
- **Booking status is part of a booking's identity.** It is in the run key, so a
  tentative day and a confirmed day on the same project never fold together, and
  in the dedupe key, so a re-import matches them separately. A re-import against
  data loaded *before* V004 would therefore duplicate the unconfirmed bookings
  — those rows were backfilled to `confirmed`. Reload from scratch instead.

The export files are **git-ignored**: they carry real staff names, emails, phone
numbers and security clearances (NFR-SEC-5).

## Domain model

Four core entities with this relational shape:

- **Client** `1—*` **Project** `1—*` **Allocation** `*—1` **Resource**

An **Allocation** is the join between one Resource and one Project for a date range with an effort value (`hoursPerWeek` or `percent`, see `EffortUnit`).

Four further entities came with the V002 model (SRS §3.6–§3.9):

- **Project** `1—*` **ProjectPhase** (named, dated stage) and `1—*` **ProjectMilestone** (dated checkpoint)
- **Resource** `1—*` **TimeOff** (leave over a date range)
- **ActivityType** — a fifth admin-maintained pick-list alongside department / location / job title / skill

Key invariants (enforce server-side, per §3.5 of the SRS):

- Project `endDate` ≥ `startDate`; allocation dates validated against the project window.
- `email` (Resource) and `code` (Project) are unique.
- Deletes are referential-integrity-aware: by default return **409** when dependents exist; the `cascade=true` query parameter opts into cascading delete.
- Over-allocation (a resource's concurrent effort exceeding its weekly availability) is **not blocked** — it is surfaced as a non-blocking `warnings` array on the created/updated allocation, and flagged in Gantt/dashboard views.
- Phase date ranges and milestone due dates are validated against the project window; a project's `budgetType` must agree with its budget fields (`fee` needs `budget`, `hours` needs `budgetHours`).
- Time off does **not** block allocation either, but overlapping leave for the same resource **is** rejected (409) — that is a data error, not a legitimate warning state. Leave reduces `effectiveCapacityHours` in the utilisation report; `utilisation` itself stays measured against **gross** availability so the ratio is comparable across releases.
- A resource may not be its own manager. `manager_id` is `ON DELETE SET NULL`: being named as a manager is descriptive and must not block a delete. The same reasoning covers `allocation.booker_id` and `time_off.booker_id` (V003) — these three are the only exceptions to the RESTRICT convention.
- A **booker** (V003) is business data: the person a booking or leave record was arranged by, chosen in the Schedule's day-cell dialog. It is deliberately separate from `created_by`, which is audit attribution stamped from the authenticated principal and never user-editable.
- **Booking status** (V004: `confirmed` / `tentative` / `waiting`, default `confirmed`) is **descriptive and must stay that way**. No capacity arithmetic keys off it: over-allocation warnings, the dashboard figures and the utilisation ratio count every allocation whatever its status, so those numbers stay comparable across releases and a pencilled-in booking still warns when it would exceed availability. What it drives is visibility — the `?bookingStatus=` filter on the allocations list, `unconfirmedHours` on the utilisation report, and dashed bars on the Schedule and Gantt. Whether reports should *exclude* unconfirmed bookings is a deferred question (§8 no. 10); do not answer it by quietly filtering somewhere.

## Authn / authz

- **Authentication**: Microsoft Active Directory / Entra ID via OAuth2 + OpenID Connect (authorization-code flow). No local password store.
- **Authorization**: role-based, with three roles mapped from AD groups. A user's effective permissions are the **union** of their roles.
  - **Administrator** — all create/update/delete (every data-changing operation is Administrator-only).
  - **General** — read-only over clients, projects, resources, allocations, dashboard, Gantt.
  - **Report** — the `/reports/*` endpoints.
- Enforce authorization **server-side on every request** (NFR-SEC-2), not only in the Vue UI. The required roles for each endpoint are documented in its OpenAPI `description`.

## API conventions (from the OpenAPI contract)

- IDs are UUIDs. Errors use RFC 9457 problem-details (`application/problem+json`, the `Problem` schema).
- List endpoints share consistent paging/sorting/search params: `q`, `page` (1-based), `pageSize` (max 200, default 25), `sort` (e.g. `name`, `-startDate` for descending). Paged responses wrap `{ items, meta }`.
- Reports support `format=json|csv`.

## Open questions

`docs/Requirements.md` §8 lists unresolved decisions (effort units, how "remaining" budget is derived, soft-delete/history retention, multi-currency, exact AD group mapping, audit depth, and — from v1.1 — whether utilisation should be measured against effective capacity, whether the public holiday calendar auto-generates leave, deeper manager-cycle validation, and whether phases should drive allocation). If implementation work depends on one of these, surface it rather than silently picking an answer.

The reference application's *Permissions Role*, *Invitation Status* and *Last Login* are deliberately **not** modelled: roles come from AD group membership, so an app-local permissions column would contradict the auth design (SRS §3.3).
