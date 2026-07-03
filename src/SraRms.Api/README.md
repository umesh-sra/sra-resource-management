# SraRms.Api — Business layer

ASP.NET Core (.NET 9) Web API implementing [`docs/openapi.yaml`](../../docs/openapi.yaml)
over the PostgreSQL schema in [`db/`](../../db). All routes are served under `/v1`.

## Run locally

The database must be up and migrated first (see [`db/README.md`](../../db/README.md)).

```bash
cd src/SraRms.Api
dotnet run
```

- Swagger UI: `http://localhost:5163/swagger`
- The port comes from `Properties/launchSettings.json`; override with `--urls`.

### Local authentication

Production uses Microsoft Entra ID (JWT bearer). For local review, set `Auth:Mode=Dev`
(already set in `appsettings.Development.json`) — every request is signed in as a
synthetic user holding all three roles. This **only** activates in the Development
environment; any other environment requires real Entra tokens.

> `appsettings.Development.json` holds the local DB password and is git-ignored.
> Configure the real connection string and `AzureAd` section via environment
> variables / user-secrets per environment.

## Layout

```
Auth/          Role + policy constants; DevAuthHandler (local-only bypass)
Contracts/     DTOs matching the OpenAPI schemas, paging, entity->DTO mapping
Controllers/   One controller per OpenAPI tag (Clients, Projects, Resources,
               Allocations, Dashboard, Reports, ReferenceData)
Data/          EF Core entities, enums ([PgName] -> DB labels), AppDbContext
Services/      AllocationService — window validation + over-allocation maths
Program.cs     DI, EF/Npgsql + enum mapping, auth, RBAC policies, CORS, JSON
```

## Conventions worth knowing

- **Enums**: CLR PascalCase ⇄ DB labels via `[PgName]` (Npgsql) ⇄ JSON camelCase
  tokens via `JsonStringEnumConverter(CamelCase)`. The three representations line
  up (`OnHold` ⇄ `onHold`), so no manual translation is needed.
- **Errors** are RFC 9457 `application/problem+json`. Model-validation failures
  return 400 automatically; `NotFoundProblem`/`ConflictProblem`/`BadRequestProblem`
  helpers cover the rest.
- **RBAC policies**: `Admin` (Administrator — all writes), `Read` (Administrator or
  General), `Report` (Report). Applied per action via `[Authorize(Policy = ...)]`.
- **Deletes** return 409 when dependents exist unless `?cascade=true` (FR-DEL).
- **Over-allocation** is never blocking — it surfaces as the `warnings[]` array on
  a created/updated allocation (FR-ALL-6). Allocation dates outside the project
  window are a hard 400 (FR-ALL-5).
- **Audit**: `created_by`/`updated_by` are stamped from the authenticated user and
  timestamps maintained in `AppDbContext.SaveChanges`.
