# SRA-RMS Web (presentation tier)

Vue 3 + Vite + TypeScript single-page app for the SRA Resource Management System.
Talks to the business API (`src/SraRms.Api`) over its `/v1` REST surface.

## Run locally

The API must be running first (see `src/SraRms.Api/README.md`); in Development it
authorises every request, so the SPA needs no login during local dev.

```bash
cd web
npm install
npm run dev        # http://localhost:5173
```

`npm run build` type-checks (vue-tsc) and produces a production bundle in `dist/`.

Configure the API base URL via `VITE_API_BASE` (see `.env.development`,
default `http://localhost:5163/v1`). The API's CORS allow-list must include the
SPA origin (`http://localhost:5173` by default).

## Structure

```
src/
  api/        http.ts (axios + problem+json -> ApiError) and endpoint modules
  types/      TS mirrors of the API DTOs
  lib/        format.ts (dates, money, status badges, timeline date maths),
              scrollLock.ts (page-scroll lock for open overlays)
  stores/     Pinia (toast notifications)
  components/ AppAvatar, AvatarStack, TimelineGrid, SidePanel, ModalDialog,
              CollapsibleSection, PagerBar, ToastHost, and the record dialogs
              (PersonFormModal, ProjectFormModal, AllocationEditModal, PersonPanel)
  views/      Dashboard, Schedule, Gantt, People, Work (Projects & Clients),
              ProjectDetail, ClientDetail, Reports
  router/     routes + titles
  styles/     main.css — design tokens + base component classes
```

## Screen layout

The information architecture and screen layouts follow the reference application
captured in `screens/`:

| Route | Screen | Reference |
| --- | --- | --- |
| `/dashboard` | Month agenda + portfolio rail | `dashboard.png` |
| `/schedule` | People × days timeline | `schedule.png` |
| `/gantt` | Project (or people) Gantt | — |
| `/people`, `/people/:id` | Photo-card grid + person drawer | `people and resources.png`, `person_overview.png`, `person_projects.png` |
| `/projects`, `/clients` | Tabbed table with team rosters | `projects and clients.png` |
| `/reports` | Report rail + chart + figures | `reports.png` |

Create dialogs mirror `new_person_*.png` and `new_project*.png`.

**Deliberate departures.** Colour follows the SRA brand rather than the
reference's purple (NFR-USE-1).

The V002 model closed most of the earlier gaps: project **Phases** and
**Milestones** are now tabs on the project dialog and cards on the project page,
**Time off** is drawn on the Schedule as hatched blocks and listed in the person
drawer, **hour-based budgets** are a Budget-tab option, and **per-person rates**
live on the person record (default) and each allocation (override).

Still omitted rather than mocked, because nothing backs them: **Timesheets** and
timesheet actuals, invitation emails, and per-person **permission roles** — roles
come from AD group membership, so an app-local role would contradict the auth
design (SRS §3.3). The Reports rail lists the unavailable standard reports
explicitly so the gap is visible rather than silent.

## Conventions

- **Theming** is centralised in `src/styles/main.css` via `--brand-*` / token
  variables, set to the official SRA palette (`brand/BrandGuidelines.pdf` §1.05):
  Deep Blue `#002048` (chrome), Red `#F4004E` (`--accent`), Silver `#D4E7E3`.
- **Errors**: the axios interceptor unwraps RFC 9457 problem details into an
  `ApiError`; views surface `e.message` through the toast store.
- **Over-allocation**: creating an allocation that exceeds capacity still succeeds
  (HTTP 201) but returns a `warnings[]` array, shown to the user as a toast and
  rendered as a red bar on the Schedule.
- **Overlays**: `ModalDialog` and `SidePanel` both trap focus, close on Escape,
  restore focus to their opener, and lock page scroll via `lib/scrollLock.ts`
  (locks are reference-counted so a dialog opened from the drawer nests safely).

## Production hosting headers

`npm run build` produces a static bundle; security headers must come from
whatever serves `dist/` (reverse proxy, CDN, static host). The dev/preview
servers already send the non-CSP subset (see `vite.config.ts`). Required
(NFR-SEC-1, NFR-SEC-4):

```
Strict-Transport-Security: max-age=31536000; includeSubDomains
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: no-referrer
Content-Security-Policy: default-src 'self'; script-src 'self';
  style-src 'self' 'unsafe-inline'; img-src 'self' data: <API_ORIGIN>;
  connect-src 'self' <API_ORIGIN>; frame-ancestors 'none';
  base-uri 'self'; form-action 'self'; object-src 'none'
```

Replace `<API_ORIGIN>` with the deployed API origin (`VITE_API_BASE` minus the
`/v1` path). `style-src 'unsafe-inline'` is needed because components use
inline `style=` bindings. The API sets its own headers server-side
(`src/SraRms.Api/Program.cs`).

## Not yet wired

- **Entra ID sign-in** — `api/http.ts` is the interceptor point to attach a bearer
  token (e.g. via MSAL) for non-dev environments.
- **Role-aware UI** — the header shows a static all-roles dev user; gate write
  actions by the signed-in user's roles once auth is real.
- **Gantt visualisation** (FR-GANTT-*) — `/dashboard/gantt` is implemented in the
  API but not yet surfaced in the UI.
