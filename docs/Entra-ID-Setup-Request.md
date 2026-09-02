# IT Request — Microsoft Entra ID setup for SRA-RMS

**Requested by:** Umesh Kodippili (Umesh.Kodippili@sra.com.au)
**Date:** 2026-07-05
**Application:** SRA Resource Management System (SRA-RMS)

## What we're asking for

SRA-RMS is an internal web application (a Vue single-page app backed by a .NET
REST API) that needs single sign-on with the company's Active Directory via
Microsoft Entra ID, using OAuth 2.0 / OpenID Connect (authorization-code flow
with PKCE). No passwords are stored by the application; it relies entirely on
Entra ID for authentication.

We need **two app registrations**, **three app roles**, and **AD security
groups assigned to those roles**. Everything below can be done in the Entra
admin center (https://entra.microsoft.com) by a user with the *Application
Administrator* (or Global Administrator) role. Estimated effort: 20–30 minutes.

> **Note:** No client secrets or certificates are required for either
> registration. The SPA uses PKCE and the API only validates tokens.

---

## 1. App registration: SRA-RMS API

Entra admin center → **Identity → Applications → App registrations → New registration**

| Setting | Value |
|---|---|
| Name | `SRA-RMS API` |
| Supported account types | Accounts in this organizational directory only (single tenant) |
| Redirect URI | *(leave empty)* |

After creating it:

### 1a. Expose an API

**Expose an API** blade:

1. Set the **Application ID URI** to `api://sra-rms`
   (if the tenant enforces the default format, `api://<client-id>` is fine —
   just tell us the final value).
2. **Add a scope**:

| Setting | Value |
|---|---|
| Scope name | `access_as_user` |
| Who can consent | Admins only |
| Admin consent display name | Access SRA-RMS as the signed-in user |
| Admin consent description | Allows the SRA-RMS web app to call the SRA-RMS API on behalf of the signed-in user. |
| State | Enabled |

### 1b. Define app roles

**App roles** blade → **Create app role** (three times):

| Display name | Value (exact, case-sensitive) | Allowed member types | Description |
|---|---|---|---|
| Administrator | `Administrator` | Users/Groups | Full create/update/delete access to all SRA-RMS data. |
| General | `General` | Users/Groups | Read-only access to clients, projects, resources, allocations, dashboard. |
| Report | `Report` | Users/Groups | Access to SRA-RMS reporting endpoints. |

> The `Value` column must match exactly — the API authorizes against these
> strings in the token's `roles` claim.

### 1c. Assign AD groups to the app roles

**Identity → Applications → Enterprise applications → SRA-RMS API →
Users and groups → Add user/group**

Assign the following security groups (⚠ **to be confirmed by the requester** —
placeholders below; if suitable groups don't exist, please create them or
advise the naming convention):

| AD security group | App role |
|---|---|
| `<TBC — e.g. SG-SRA-RMS-Admins>` | Administrator |
| `<TBC — e.g. SG-SRA-RMS-Users>` | General |
| `<TBC — e.g. SG-SRA-RMS-Reporting>` | Report |

Notes:
- A user may be in multiple groups; the application treats permissions as the
  union of their roles.
- Groups synced from on-premises AD via Entra Connect work here, as long as
  they are security groups visible in Entra ID.
- Optional but recommended: on the enterprise application's **Properties**
  blade set **Assignment required? = Yes**, so only members of the assigned
  groups can sign in to SRA-RMS at all.

---

## 2. App registration: SRA-RMS Web

Entra admin center → **App registrations → New registration**

| Setting | Value |
|---|---|
| Name | `SRA-RMS Web` |
| Supported account types | Accounts in this organizational directory only (single tenant) |
| Platform | **Single-page application (SPA)** |
| Redirect URIs | `http://localhost:5173` (development) and `<TBC — production URL>` |

> The production redirect URI will be supplied once hosting is finalised; the
> localhost URI is needed now for development. Additional redirect URIs can be
> added later without other changes.

### 2a. API permissions

**API permissions** blade:

1. **Add a permission → My APIs → SRA-RMS API** → Delegated permissions →
   tick `access_as_user` → Add.
2. Click **Grant admin consent for \<tenant\>** so users are not prompted
   individually.

(The default `Microsoft Graph → User.Read` delegated permission can stay.)

---

## 3. What we need back from you

Please reply with:

1. **Tenant ID** (Directory ID GUID)
2. **SRA-RMS API** — Application (client) ID and the final **Application ID URI**
3. **SRA-RMS Web** — Application (client) ID
4. The names of the AD groups assigned to each of the three app roles
5. Confirmation that admin consent was granted on the SRA-RMS Web registration

None of these values are secrets; they can be sent by email or Teams.

## 4. What we do with it (no action needed from IT)

- The API validates Entra-issued JWT bearer tokens (issuer = our tenant,
  audience = the API's Application ID URI) and enforces the three roles
  server-side on every request.
- The SPA signs users in with MSAL (authorization-code + PKCE), acquires an
  access token for `api://sra-rms/access_as_user`, and attaches it to API
  calls. Sign-in is seamless for users already signed in to their Microsoft
  365 account.

## Questions

Contact Umesh Kodippili (Umesh.Kodippili@sra.com.au).
