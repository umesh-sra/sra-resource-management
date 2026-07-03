# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project state

All three tiers are **implemented and working**: the .NET 9 API (`src/SraRms.Api`) covers every endpoint in `docs/openapi.yaml`, the Vue SPA (`web/`) covers the core screens, the initial schema and dev seed are in `db/`, and unit + integration tests pass in `tests/SraRms.Api.Tests`. When asked to build something, extend the existing code — do not re-scaffold. `TODO.md` is the living backlog (remaining features, test gaps, open questions); `docs/Review-2026-07-03.md` holds the latest code-review findings.

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

Local API auth: `appsettings.Development.json` sets `Auth:Mode=Dev`, which signs every request in as a synthetic all-roles user so endpoints can be exercised without an Entra tenant. This bypass is hard-gated to the Development environment.

## Domain model

Four core entities with this relational shape:

- **Client** `1—*` **Project** `1—*` **Allocation** `*—1` **Resource**

An **Allocation** is the join between one Resource and one Project for a date range with an effort value (`hoursPerWeek` or `percent`, see `EffortUnit`). Key invariants (enforce server-side, per §3.5 of the SRS):

- Project `endDate` ≥ `startDate`; allocation dates validated against the project window.
- `email` (Resource) and `code` (Project) are unique.
- Deletes are referential-integrity-aware: by default return **409** when dependents exist; the `cascade=true` query parameter opts into cascading delete.
- Over-allocation (a resource's concurrent effort exceeding its weekly availability) is **not blocked** — it is surfaced as a non-blocking `warnings` array on the created/updated allocation, and flagged in Gantt/dashboard views.

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

`docs/Requirements.md` §8 lists unresolved decisions (effort units, how "remaining" budget is derived, soft-delete/history retention, multi-currency, exact AD group mapping, audit depth). If implementation work depends on one of these, surface it rather than silently picking an answer.
