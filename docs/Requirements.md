# SRA Resource Management System — Software Requirements Specification

**Document version:** 1.0 (Draft)
**Date:** 29 June 2026
**Status:** Draft for review
**Owner:** Umesh Kodippili (umesh.kodippili@sra.com.au)

---

## 1. Introduction

### 1.1 Purpose

This document specifies the requirements for the **SRA Resource Management System** (SRA-RMS), a web application that enables SRA to manage clients, projects, resources (people), and the allocation of resources to projects. It is intended for the project sponsor, development team, testers, and stakeholders responsible for sign-off.

### 1.2 Scope

SRA-RMS provides a single place to record clients and their projects, maintain a directory of people and their skills/availability, allocate people to projects over time, and visualise and report on that allocation. The system supports four primary capabilities:

- CRUD management of clients, projects, resources, and resource allocations.
- A management dashboard summarising utilisation, availability, and project health.
- Visualisation of resource allocation and project timelines via Gantt charts.
- Reporting on utilisation, allocation, budget consumption, and billability.

Out of scope for the initial release: time-sheeting/actuals capture, invoicing, payroll integration, and a public-facing portal. These may be considered in later phases.

### 1.3 Background

SRA is a custom software company that builds bespoke solutions for its clients. Effective scheduling of a limited pool of people across concurrent client projects is a core operational need. SRA-RMS replaces ad-hoc spreadsheets with a governed, role-based application backed by a relational database.

### 1.4 Definitions and acronyms

| Term | Definition |
|------|------------|
| Resource | A person who can be allocated to projects. |
| Allocation | An assignment of a resource to a project for a defined period and effort. |
| Utilisation | The proportion of a resource's available hours that are allocated. |
| Billable | Whether work on a project can be charged to the client. |
| AD | Microsoft Active Directory / Entra ID. |
| RBAC | Role-Based Access Control. |
| FTE | Full-Time Equivalent. |
| SRS | Software Requirements Specification. |
| CRUD | Create, Read, Update, Delete. |

### 1.5 References

- Project notes: `notes/SRA Resource Management System.md`, `notes/Client.md`, `notes/Project.md`, `notes/Resource.md`.
- OpenAPI specification: `docs/openapi.yaml` (companion artifact to this document).
- SRA brand assets and guidelines: `brand/`.

---

## 2. Overall description

### 2.1 Product perspective

SRA-RMS is a new, standalone web application composed of three tiers:

- **Presentation tier** — a VueJS single-page application.
- **Business tier** — a C# (.NET) Web API exposing the contract defined in the companion OpenAPI specification.
- **Data tier** — a PostgreSQL relational database.

Authentication is delegated to Microsoft Active Directory. Authorization is enforced within the application using role-based access control.

### 2.2 User classes and characteristics

| Role | Description | Typical user |
|------|-------------|--------------|
| **Administrator** | Full CRUD over clients, projects, resources, and allocations; manages reference data. | Resource manager, PMO lead |
| **General** | Read-only access to clients, projects, resources, allocations, and the dashboard. | Project managers, team leads |
| **Report** | Access to reports and reporting exports. | Finance, executives |

A user may hold more than one role; effective permissions are the union of the roles' permissions. Role membership is derived from Active Directory group mapping.

### 2.3 Operating environment

Modern evergreen browsers (Chrome, Edge, Firefox, Safari — current and prior major version). The back end runs on a supported .NET LTS runtime; the database is a supported PostgreSQL major version. Deployment target is the organisation's standard hosting environment (cloud or on-premise) per the deployment guide.

### 2.4 Design and implementation constraints

- Front end must be implemented in VueJS; business layer in C#; data layer in PostgreSQL.
- Authentication must use Microsoft Active Directory; no local password store.
- The business layer must conform to the companion OpenAPI specification.
- All data-changing operations are restricted to the Administrator role.

### 2.5 Assumptions and dependencies

- An Active Directory tenant and the appropriate app registration / security groups are available.
- AD groups exist (or will be created) to map onto the three application roles.
- Reference data (departments, locations, job titles) is maintained by Administrators within the app.
- A single currency and time zone are used for budgets and scheduling in the first release (configurable defaults).

---

## 3. Data requirements

The core domain comprises four entities. A **Client** has zero or more **Projects**; a **Project** has zero or more **Allocations**; a **Resource** has zero or more **Allocations**. An Allocation is the association between one Resource and one Project.

### 3.1 Client

| Attribute | Type | Notes |
|-----------|------|-------|
| Client name | text | Required, unique. |
| Projects | relation | Zero or more projects. |
| Teams | derived | The set of resources allocated across the client's projects. |

### 3.2 Project

| Attribute | Type | Notes |
|-----------|------|-------|
| Project name | text | Required. |
| Client | relation | Required; the owning client. |
| Project code | text | Required, unique business identifier. |
| Start date | date | Required. |
| End date | date | Required; must be on or after start date. |
| Budget | money | Total budget. |
| Remaining | money | Remaining budget (derived or maintained). |
| Billable | boolean | Whether the project is billable. |
| Team (resources) | relation | Resources allocated via allocations. |
| Gantt | derived | Timeline view derived from project dates and allocations. |

### 3.3 Resource (person)

| Attribute | Type | Notes |
|-----------|------|-------|
| Name | text | Required. |
| Email | text | Required, unique, valid email. |
| Primary job title | text | Required. |
| Secondary job title | text | Optional. |
| Status | enum | e.g. Active, Inactive, On leave. |
| Department | text/ref | Reference data. |
| Location | text/ref | Reference data. |
| Notes | text | Free text. |
| Skills | tags | Searchable tags. |
| Image | image | Profile photo. |
| Availability | number | Hours available per week. |
| Working days | set | Days the resource works (e.g. Mon–Wed). |

### 3.4 Allocation

| Attribute | Type | Notes |
|-----------|------|-------|
| Resource | relation | Required. |
| Project | relation | Required. |
| Start date | date | Required; within or overlapping the project window. |
| End date | date | Required; on or after allocation start date. |
| Allocation (effort) | number/percent | Hours per week or percentage of availability. |
| Role on project | text | Optional. |
| Billable | boolean | Defaults from the project; overridable. |

### 3.5 Data integrity rules

- A project's end date must not precede its start date.
- An allocation's date range must fall within (or be validated against) the project's date range.
- Deleting a client with existing projects, or a project/resource with existing allocations, is prevented or cascaded explicitly (see FR-DEL).
- Email addresses and project codes are unique.
- Total concurrent allocation for a resource should not silently exceed its weekly availability; over-allocation is flagged.

---

## 4. Functional requirements

Each requirement has an identifier, a priority (Must / Should / Could), and the roles permitted to perform it.

### 4.1 Authentication and authorization

| ID | Requirement | Priority | Roles |
|----|-------------|----------|-------|
| FR-AUTH-1 | The system shall authenticate users via Microsoft Active Directory (single sign-on). | Must | All |
| FR-AUTH-2 | The system shall map AD group membership to the application roles Administrator, General, and Report. | Must | — |
| FR-AUTH-3 | The system shall deny access to any function not permitted by the user's effective roles and return an appropriate authorization error. | Must | All |
| FR-AUTH-4 | The system shall record the authenticated user against all create/update/delete operations for audit. | Should | — |

### 4.2 Client management

| ID | Requirement | Priority | Roles |
|----|-------------|----------|-------|
| FR-CLI-1 | Create a client with a unique name. | Must | Administrator |
| FR-CLI-2 | View a list of clients with search, filter, and pagination. | Must | Admin, General |
| FR-CLI-3 | View a single client including its projects and aggregated team. | Must | Admin, General |
| FR-CLI-4 | Update a client's details. | Must | Administrator |
| FR-CLI-5 | Delete a client only when it has no projects, or with explicit confirmation/cascade. | Must | Administrator |

### 4.3 Project management

| ID | Requirement | Priority | Roles |
|----|-------------|----------|-------|
| FR-PRJ-1 | Create a project under a client with name, unique code, dates, budget, and billable flag. | Must | Administrator |
| FR-PRJ-2 | View projects with search, filter (by client, status, billable, date range), and pagination. | Must | Admin, General |
| FR-PRJ-3 | View a single project including allocated resources and timeline. | Must | Admin, General |
| FR-PRJ-4 | Update a project's details. | Must | Administrator |
| FR-PRJ-5 | Delete a project only when it has no allocations, or with explicit confirmation/cascade. | Must | Administrator |
| FR-PRJ-6 | Validate that end date is on or after start date. | Must | Administrator |
| FR-PRJ-7 | Track remaining budget against total budget. | Should | Administrator |

### 4.4 Resource management

| ID | Requirement | Priority | Roles |
|----|-------------|----------|-------|
| FR-RES-1 | Create a resource with the attributes in section 3.3. | Must | Administrator |
| FR-RES-2 | View resources with search by name, skill tags, department, location, and status; with pagination. | Must | Admin, General |
| FR-RES-3 | View a single resource including current and historical allocations. | Must | Admin, General |
| FR-RES-4 | Update a resource, including skills, availability, and working days. | Must | Administrator |
| FR-RES-5 | Upload and display a resource profile image. | Should | Administrator |
| FR-RES-6 | Set a resource's status (Active, Inactive, On leave). | Must | Administrator |
| FR-RES-7 | Delete a resource only when it has no allocations, or with explicit confirmation. | Must | Administrator |

### 4.5 Allocation management

| ID | Requirement | Priority | Roles |
|----|-------------|----------|-------|
| FR-ALL-1 | Allocate a resource to a project for a date range with an effort value. | Must | Administrator |
| FR-ALL-2 | View allocations filtered by project, resource, or date range. | Must | Admin, General |
| FR-ALL-3 | Update an allocation's dates, effort, or role. | Must | Administrator |
| FR-ALL-4 | Remove an allocation. | Must | Administrator |
| FR-ALL-5 | Validate allocation dates against the project window. | Must | Administrator |
| FR-ALL-6 | Detect and warn when an allocation causes a resource to exceed its weekly availability. | Should | Administrator |
| FR-ALL-7 | Respect a resource's working days when computing effort and over-allocation. | Could | — |

### 4.6 Dashboard

| ID | Requirement | Priority | Roles |
|----|-------------|----------|-------|
| FR-DASH-1 | Present a management dashboard summarising headline metrics (active projects, resource count, average utilisation, over/under-allocated resources, budget at risk). | Must | Admin, General |
| FR-DASH-2 | Show upcoming project starts/ends and resources rolling off in a configurable horizon. | Should | Admin, General |
| FR-DASH-3 | Allow drill-down from a dashboard metric to the underlying list. | Should | Admin, General |

### 4.7 Gantt visualisation

| ID | Requirement | Priority | Roles |
|----|-------------|----------|-------|
| FR-GANTT-1 | Display a Gantt chart of project timelines across a selectable date range. | Must | Admin, General |
| FR-GANTT-2 | Display a resource-allocation Gantt showing each resource's allocations over time. | Must | Admin, General |
| FR-GANTT-3 | Colour or flag over-allocated periods. | Should | Admin, General |
| FR-GANTT-4 | Filter the Gantt by client, project, department, or resource. | Should | Admin, General |

### 4.8 Reporting

| ID | Requirement | Priority | Roles |
|----|-------------|----------|-------|
| FR-REP-1 | Generate a resource utilisation report over a date range. | Must | Report |
| FR-REP-2 | Generate an allocation report by project and by resource. | Must | Report |
| FR-REP-3 | Generate a budget consumption report (budget vs remaining) by project and client. | Should | Report |
| FR-REP-4 | Generate a billable vs non-billable summary. | Should | Report |
| FR-REP-5 | Export reports to CSV (and optionally PDF/Excel). | Should | Report |

### 4.9 Reference data and cross-cutting

| ID | Requirement | Priority | Roles |
|----|-------------|----------|-------|
| FR-REF-1 | Maintain reference data: departments, locations, job titles, skill tags, resource statuses. | Should | Administrator |
| FR-DEL | For every delete, enforce referential integrity: block when dependents exist, or require explicit confirmation with defined cascade behaviour. | Must | Administrator |
| FR-SRCH | Provide consistent search, filter, sort, and pagination semantics across all list endpoints. | Must | Admin, General |

---

## 5. Non-functional requirements

### 5.1 Performance

- NFR-PERF-1: List endpoints shall return the first page within 2 seconds for datasets up to 10,000 records under normal load.
- NFR-PERF-2: Dashboard and Gantt views shall render within 3 seconds for a 12-month horizon.

### 5.2 Scalability and capacity

- NFR-SCALE-1: The system shall support at least 100 concurrent users and 10,000 resources / 5,000 projects without redesign.

### 5.3 Security

- NFR-SEC-1: All traffic shall use TLS.
- NFR-SEC-2: Access control shall be enforced server-side on every request, not solely in the UI.
- NFR-SEC-3: All write operations shall be attributable to an authenticated user (audit trail).
- NFR-SEC-4: Input shall be validated and output encoded to prevent injection and XSS; the API shall protect against common OWASP Top 10 risks.
- NFR-SEC-5: Personal data (resource details, images) shall be access-controlled and handled per applicable privacy obligations.

### 5.4 Availability and reliability

- NFR-AVAIL-1: Target availability of 99.5% during business hours.
- NFR-REL-1: Automated database backups with a documented recovery procedure.

### 5.5 Usability and accessibility

- NFR-USE-1: The UI shall follow SRA brand guidelines (see `brand/`).
- NFR-USE-2: The UI should target WCAG 2.1 AA accessibility.

### 5.6 Maintainability and portability

- NFR-MAINT-1: The business layer shall conform to the companion OpenAPI specification, which is the source of truth for the API contract.
- NFR-MAINT-2: Database schema changes shall be managed via versioned migration scripts.

### 5.7 Auditability and observability

- NFR-OBS-1: The system shall emit structured logs and key operational metrics.
- NFR-AUD-1: Create/update/delete events shall be recorded with user, timestamp, and entity affected.

---

## 6. Use cases (representative)

**UC-1 Allocate a resource to a project.** An Administrator opens a project, selects an available resource (optionally filtered by skill), sets a date range and effort, and saves. The system validates dates against the project window and warns if the resource would be over-allocated, then records the allocation.

**UC-2 Find people with a skill who are free.** A General user searches resources by skill tag and reviews each candidate's current allocations and remaining availability on the resource-allocation Gantt.

**UC-3 Review portfolio health.** A General user opens the dashboard, sees average utilisation and budget-at-risk, and drills into the list of over-allocated resources.

**UC-4 Produce a monthly utilisation report.** A Report user selects a date range, generates the utilisation report, and exports it to CSV for finance.

**UC-5 Close out a project.** An Administrator updates a project's end date and status; resources rolling off appear on the dashboard's upcoming-roll-offs panel.

---

## 7. Project artifacts

The following artifacts accompany delivery: this requirements document, the OpenAPI specification for the business layer (`docs/openapi.yaml`), database scripts (schema and migrations), a deployment guide, and deployment artifacts.

---

## 8. Open questions

1. Is allocation effort expressed in hours per week, percentage of availability, or both?
2. Is "Remaining" budget maintained manually, or derived from actuals captured elsewhere (and if so, from where)?
3. Should historical allocations be retained indefinitely for reporting, and is soft-delete required?
4. Is multi-currency / multi-time-zone needed in the first release?
5. What are the exact AD groups and their mapping to the three roles?
6. Is a full audit history (who changed what, before/after) required, or is last-modified attribution sufficient?

---

*End of document.*
