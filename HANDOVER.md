# Handover — 2026-07-05

Context for the next agent/session working on SRA-RMS. Read `CLAUDE.md` first;
`TODO.md` is the living backlog. This note covers what the 2026-07-05 session
did, the state it left the machine in, and the sharp edges it found.

## What landed this session (both pushed to `main`)

1. **`eb44787` — WCAG 2.1 AA accessibility pass over the SPA** (NFR-USE-2,
   resolves finding W-M1 in `docs/Review-2026-07-03.md`):
   - Skip link, `nav aria-label`, focus moves to `<main>` on route change,
     visible `:focus-visible` indicators (white variant on the dark sidebar).
   - Every "clickable row" now also has a real `RouterLink` in its primary cell
     (class `row-link`); the row `@click` remains for mouse convenience. Keep
     this pattern for any new tables (Gantt UI is still unbuilt).
   - `ModalDialog.vue` now owns the dialog pattern: focus trap, Escape, focus
     restore, `aria-labelledby` via `useId()`. New modals get all this for free.
   - Toasts: `aria-live="polite"` host, `role="alert"` for errors, keyboard
     dismiss button.
   - All form fields have `label for`/`id` (unique per-view prefixes: `np-`,
     `nr-`, `ep-`, `er-`, `al-`, `ea-`). Follow the convention for new forms.
   - Contrast tokens changed in `web/src/styles/main.css` — `--text-muted` is
     now gray-600, `--amber-700` is `#8a5700`, success toast uses green-700,
     `--input-border` is `#7f8a97` (3:1 boundary). Don't reintroduce the old
     lighter values; they failed WCAG 1.4.3/1.4.11.
   - Exactly one `<h1>` per page: the topbar title is a styled `<span>`, each
     view supplies the `h1`.

2. **`fde48f7` — Security headers on both tiers** (NFR-SEC-1, NFR-SEC-4):
   - `src/SraRms.Api/Program.cs`: first middleware in the pipeline sets, via
     `Response.OnStarting` (so exception-handler re-executions are covered):
     nosniff, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, CSP
     `default-src 'none'; frame-ancestors 'none'` (paths under `/swagger`
     exempt), and `Cache-Control: no-store` unless the endpoint set its own.
     HSTS + HTTPS redirection apply only outside Development.
   - `tests/SraRms.Api.Tests/SecurityHeadersTests.cs` asserts the headers on
     200/404/400 responses — extend it if you add or change headers.
   - `web/vite.config.ts` dev/preview servers send the non-CSP subset. Do NOT
     add a CSP there — it breaks Vite HMR. The production CSP for the static
     bundle is documented in `web/README.md` ("Production hosting headers")
     and must be set by whatever serves `dist/`.

## Machine / environment state

- **Dev servers**: during the session the user's original `run.ps1` processes
  were replaced — Vite (5173) now runs under Claude Code's preview tool, and
  the API (5163) runs as a background `dotnet run` task. If they've gone away,
  `./run.ps1` starts both; API alone: `dotnet run --project src/SraRms.Api`.
- **Git over HTTPS**: outbound TLS is intercepted by a corporate proxy.
  `http.sslBackend=schannel` is set in the user's **global** git config
  (2026-07-05); without it, push fails with "unable to get local issuer
  certificate (20)". See memory note `git-push-tls.md`.
- `.claude/launch.json` (git-ignored) defines the `web` preview server.
- `.claude/settings.local.json` has uncommitted local modifications — leave
  them out of commits.
- Docker is available; the integration tests (Testcontainers Postgres) run
  fine: `dotnet test` → 33/33 passing as of this session.

## Verification snapshot (all green as of 2026-07-05)

- `cd web && npm run build` — vue-tsc + bundle OK.
- `dotnet build SraRms.sln` / `dotnet test` — clean, 33 tests pass. NB: the
  build fails with a file-lock error if the API is running; stop it first.
- Live checks: headers confirmed on `http://localhost:5163/v1/clients` and
  `http://localhost:5173/`; Swagger still loads (CSP-exempt); modal focus
  trap/Escape/restore verified in-browser; skip link + landmarks present in
  the accessibility tree.

## Suggested next work (see TODO.md for the full list)

- RBAC / 403 integration test slices (top of "Next up" — needs per-test role
  control instead of the all-roles Dev auth user).
- Gantt UI (FR-GANTT-*) — API endpoint exists, no SPA surface. Build it with
  the a11y patterns above; a time-grid visualisation will need real keyboard
  and screen-reader design, not just the row-link pattern.
- Deployment guide — must include the production hosting headers from
  `web/README.md` and forwarded-headers config if TLS terminates at a proxy
  (otherwise the API's HTTPS redirect loops).

## Open questions still unresolved

`docs/Requirements.md` §8 (effort units, derived vs stored `remaining`,
soft-delete/history, multi-currency, AD group mapping, audit depth). Surface
these rather than picking silently if work depends on them.
