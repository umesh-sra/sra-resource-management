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
  lib/        formatting helpers (dates, money, status badges)
  stores/     Pinia (toast notifications)
  components/ Layout chrome + reusable bits (ModalDialog, PagerBar, ToastHost)
  views/      One per route: Dashboard, Clients(+detail), Projects(+detail),
              Resources(+detail), Allocations, Reports
  router/     routes + titles
  styles/     main.css — design tokens + base component classes
```

## Conventions

- **Theming** is centralised in `src/styles/main.css` via `--brand-*` / token
  variables, set to the official SRA palette (`brand/BrandGuidelines.pdf` §1.05):
  Deep Blue `#002048` (chrome), Red `#F4004E` (`--accent`), Silver `#D4E7E3`.
- **Errors**: the axios interceptor unwraps RFC 9457 problem details into an
  `ApiError`; views surface `e.message` through the toast store.
- **Over-allocation**: creating an allocation that exceeds capacity still succeeds
  (HTTP 201) but returns a `warnings[]` array, shown to the user as a toast.

## Not yet wired

- **Entra ID sign-in** — `api/http.ts` is the interceptor point to attach a bearer
  token (e.g. via MSAL) for non-dev environments.
- **Role-aware UI** — the header shows a static all-roles dev user; gate write
  actions by the signed-in user's roles once auth is real.
- **Gantt visualisation** (FR-GANTT-*) — `/dashboard/gantt` is implemented in the
  API but not yet surfaced in the UI.
