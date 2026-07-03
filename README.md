# SRA Resource Management System (SRA-RMS)

Web app for SRA to manage clients, projects, resources (people), and the allocation of resources to projects — with a dashboard, Gantt data, and reporting.

## Structure

| Path | What |
|---|---|
| `src/SraRms.Api` | .NET 9 Web API (business tier) implementing `docs/openapi.yaml` |
| `web/` | Vue 3 + Vite + TypeScript SPA |
| `db/` | PostgreSQL migrations (`V001__initial_schema.sql`) and dev seed |
| `tests/SraRms.Api.Tests` | Unit + integration tests (Testcontainers Postgres) |
| `docs/` | SRS (`Requirements.md`), API contract (`openapi.yaml`), review notes |
| `notes/` | Original domain notes (Obsidian vault); `docs/` supersedes them |
| `brand/` | SRA logos and brand guidelines |

## Run locally

```bash
# API (Swagger at http://localhost:5163/swagger)
dotnet run --project src/SraRms.Api

# Front end (http://localhost:5173, expects API on 5163)
cd web && npm install && npm run dev

# Tests (integration tests need Docker)
dotnet test
```

Requires PostgreSQL on `localhost:5432` with the `sra_rms` database — see `db/README.md` for applying migrations and seed data. Local dev auth is a synthetic all-roles user (`Auth:Mode=Dev`, Development environment only).

See `CLAUDE.md` for developer/AI guidance, `TODO.md` for the backlog.
