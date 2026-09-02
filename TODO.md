# SRA-RMS — TODO

Living backlog for the SRA Resource Management System. Status as of 2026-09-02.
See `docs/Requirements.md` for the full spec and `docs/openapi.yaml` for the API contract.

## Done

- [x] **Database** — `db/migrations/V001__initial_schema.sql` applied to local `sra_rms`; dev seed in `db/seed/dev_seed.sql`.
- [x] **Business API** — `src/SraRms.Api` implements every endpoint in `docs/openapi.yaml` (CRUD, dashboard, Gantt, reports, reference data), RBAC policies, RFC 9457 errors, over-allocation warnings.
- [x] **Tests (initial)** — `tests/SraRms.Api.Tests`: AllocationService unit tests + integration tests for clients, allocations, resources, reports (Testcontainers Postgres). 18 passing.
- [x] **Front end (initial)** — `web/` Vue 3 + Vite + TS SPA: layout/nav, dashboard, clients, projects, resources (all list + create + detail), allocations, utilisation report. Wired to the live API.
- [x] **Security headers (NFR-SEC-1, NFR-SEC-4)** — 2026-07-05: API middleware sets `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, strict CSP (Swagger exempt), and default `Cache-Control: no-store` on every response, + HSTS/HTTPS-redirect outside Development (integration-tested in `SecurityHeadersTests`); Vite dev/preview servers send the non-CSP subset; production hosting CSP documented in `web/README.md`. Note: the deployment guide (below) must wire forwarded headers if TLS terminates at a proxy.
- [x] **Reference-app UI rebuild** — 2026-09-02: the SPA now follows the information architecture and screen layouts in `screens/` (Dashboard agenda + rail, Schedule people timeline, Gantt charts, People & Resources card grid with a person drawer, tabbed Projects & Clients, Reports rail + chart), rendered in the SRA palette rather than the reference's purple (NFR-USE-1). Adds `TimelineGrid`, `SidePanel`, `PersonPanel`, `CollapsibleSection`, `AppAvatar`/`AvatarStack`, tabbed `ProjectFormModal` and sectioned `PersonFormModal`, plus page-scroll locking for overlays. API side: `Project.team` roster on list/detail and `effortUnit` on Gantt bars (both added to `docs/openapi.yaml`), covered by new `ProjectsTests`. See `web/README.md` for the screen map and the list of reference features deliberately not built.
- [x] **Gantt UI (FR-GANTT-*)** — 2026-09-02: `/gantt` renders `/dashboard/gantt` for both the projects and resources views; `/schedule` is the day-level people view.
- [x] **Accessibility pass (NFR-USE-2, WCAG 2.1 AA)** — resolves review finding W-M1 (2026-07-05): skip link, keyboard-reachable row links, modal focus trap/Escape/labelling, toast live region + keyboard dismiss, `label for=` on all fields, `th scope`, visible focus indicators, contrast-compliant tokens (muted text, amber, success toast, input borders), single `h1` per page, reduced-motion support.

- [x] **Reference-app data model (V002)** — 2026-09-02: `db/migrations/V002__reference_app_model.sql` adds `project_phase`, `project_milestone`, `time_off` and the `activity_type` pick-list; extends `resource` with the full person profile (job role, manager, phone, secondary skills, security clearances + NPC date, certifications, time zone, bookable status, public holiday calendar, default rate, colour), `project` with budget type / hour budgets / activity types / details / colour, and `allocation` with an hourly rate. SRS raised to v1.1 (§3.6–§3.9, FR-PHASE-*, FR-MILE-*, FR-TIMEOFF-*, FR-RES-8/9, FR-PRJ-8/9, FR-ALL-8, FR-REP-6); `docs/openapi.yaml` gains the matching schemas and the `/projects/{id}/phases`, `/projects/{id}/milestones` and `/timeoff` paths. 17 new integration tests (`ReferenceModelTests`), 53 passing. The test fixture now applies **all** `V*.sql` in order, so future migrations need no fixture edit.

## Open items from the V002 model

- [ ] **Time off does not yet reduce over-allocation warnings.** `AllocationService` still compares effort against gross weekly availability, so a person fully on leave is not flagged differently. Reporting *does* account for leave (`effectiveCapacityHours`). Decide whether the warning should use effective capacity — this is open question #7 in `docs/Requirements.md` §8.
- [ ] **Public holiday calendar is stored but not expanded** — `publicHolidayCalendar` holds a region key; nothing generates `publicHoliday` time-off rows from it (§8 #8).
- [ ] **Manager cycles** — only direct self-reference is rejected; A → B → A is possible. A full ancestry check needs a recursive query on every write (§8 #9).
- [ ] **Time-off CRUD has no dedicated UI.** The API is complete (`/v1/timeoff`) and leave renders read-only on the Schedule and person drawer, but there is no add/edit dialog yet — records must be created through the API.
- [ ] **Allocation hourly rate is not in the allocation dialogs.** The column, DTO and contract exist; the project Team tab and `AllocationEditModal` do not expose it yet.
- [ ] **Phases do not drive allocation** — they are presentational only (§8 #10).

## Next up — additional test slices

Extend `tests/SraRms.Api.Tests` to cover the gaps below. Each is independent.

### RBAC / 403 behaviour
The Dev auth handler currently grants all three roles, so role restrictions are never exercised. Make per-test role control possible, then assert it.
- [ ] Allow tests to run as a specific role set (e.g. a test auth scheme reading roles from a request header, or per-request role configuration on the factory) instead of the all-roles Dev user.
- [ ] **General** role: can `GET` clients/projects/resources/allocations/dashboard (200) but `POST`/`PUT`/`DELETE` are **403**.
- [ ] **Report** role: can hit `/v1/reports/*` (200) but is **403** on `GET /v1/clients` and on writes.
- [ ] **Administrator** role: writes succeed (201/200/204).
- [ ] A request with **no/empty roles** is **403** on protected endpoints (and document that unauthenticated → 401 once real Entra auth is wired).

### Dashboard
- [ ] `GET /v1/dashboard/summary`: with seeded allocations active "today", assert `activeProjects`, `totalResources`, `averageUtilisation`, and `overAllocatedResources`/`underAllocatedResources` counts. Use a window that makes allocations current so utilisation is non-zero.
- [ ] `budgetAtRisk`: a project consumed ≥90% (budget vs remaining) is included; one below threshold is excluded.
- [ ] `upcomingProjectStarts` / `upcomingRollOffs`: respect the `from`/`to` horizon.
- [ ] `GET /v1/dashboard/gantt?view=projects`: rows per project, bars clipped to the requested window.
- [ ] `GET /v1/dashboard/gantt?view=resources`: bars carry `overAllocated=true` when overlapping weekly hours exceed availability.
- [ ] `view` other than `projects`/`resources` → **400**; `to` before `from` → **400**.

### Reference data
- [ ] `GET /v1/reference/{collection}` for `departments`, `locations`, `jobTitles`, `skills` returns seeded values; `resourceStatuses` returns the enum tokens (`active`, `inactive`, `onLeave`).
- [ ] `POST /v1/reference/{departments}` creates a value (201); duplicate (case-insensitive) → **409**.
- [ ] `POST /v1/reference/resourceStatuses` → **400** (fixed enumeration, not extendable).
- [ ] Unknown collection (e.g. `/v1/reference/widgets`) → **404**.

## Backlog (not yet started)

- [ ] **Front-end polish** — role-aware UI (hide writes for non-admins), reference-data pick-lists in forms (`/reference/*` is implemented server-side but the new person/project dialogs still use free-text for job title, department and location).
- [ ] **Person photos** — the person dialog uploads via `PUT /resources/{id}/image`, but there is no way to remove a photo, and the people grid falls back to initials tiles. The reference grid is photo-led, so bulk import / upload UX is worth revisiting.
- [ ] **Reference features not built** (no data model behind them) — Timesheets, Time Off, project Phases and Milestones, hour-based budgets, per-person charge-out rates. Each needs a schema + `docs/openapi.yaml` change before any UI.
- [ ] **Real Entra ID auth** — both tiers: replace `AzureAd` placeholders + AD group→role mapping (SRS #5) in the API, and attach MSAL bearer tokens in `web/src/api/http.ts`.
- [ ] **CI** — pipeline to run `dotnet build` + `dotnet test` (Docker available on the runner for integration tests).
- [ ] **Deployment guide & artifacts** (SRS §7).
- [ ] Serve uploaded resource images (static files / blob storage) and validate image size/type.

## Open questions to resolve (SRS §8)

These affect schema/behaviour; decisions should land before they get expensive.
- [ ] Effort unit: hours/week, percent, or both? (currently both, per `EffortUnit`)
- [ ] `remaining` budget — maintained manually or derived from actuals? (currently a stored column)
- [ ] Retain historical allocations / soft-delete? (currently physical delete, no history)
- [ ] Multi-currency / multi-time-zone in v1? (currently single)
- [ ] Exact AD groups → role mapping.
- [ ] Full before/after audit history, or last-modified attribution? (currently the latter)
