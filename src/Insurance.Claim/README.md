# Insurance.Claim

ASP.NET Core MVC port of the React SPA in `src/DeGoudse.Web/`. Runs on
port 5210, calls `Insurance.Api` server-side, uses jQuery + AngularJS 1.x
for the client-side interactivity per the dev's request.

Status: **in-progress POC**. The React project still ships to production.

## Client-side stack

- **jQuery 3.7** — for small DOM operations and legacy integrations.
- **AngularJS 1.x** — for two-way bound forms, dropdowns, directives.
  ⚠️ AngularJS is out of LTS since Dec 2021. Confirm this is a conscious
  choice before adding much more code — a security review will flag it.
- **Vanilla JS** — used where either of the above would be more work
  than benefit (SSE, one-off browser APIs, etc.).
- **Tailwind CSS** — same palette + component classes as the React
  project, compiled via the standalone `tailwindcss` CLI.

Both libraries load from Google's CDN in `_Layout.cshtml`. Per-view
modules load via `@section Scripts { ... }` blocks.

## What's ported

| React page          | MVC route              | Status                              |
| ------------------- | ---------------------- | ----------------------------------- |
| PolicyEntryPage     | `/`                    | ✅ Server-side form POST            |
| DashboardPage       | `/dashboard/{id}`      | ✅ Server-rendered                  |
| FileClaimPage       | `/file-claim/{id}`     | ✅ Full form with 4 directives      |
| ClaimSubmittedPage  | `/claim-submitted/{id}`| ✅ SSE + additions + FlowWalker + EvidenceSection + ReviewCard |
| ClaimEvidencePage   | `/claim/{id}/evidence` | ✅ SSE + EvidenceSection            |
| BotPage             | `/bot`                 | ✅ Standalone widget page           |

### Directives (all live under `wwwroot/app/directives/`)

Ported: `insuredDropdown`, `datePicker`, `addressPicker`, `documentUpload`,
`questionCard`, `flowWalker`, `evidenceSection`.

### Other outstanding work

- ✅ Behavioral signal collector rehosted at `wwwroot/js/behavior/*.js`
  with AngularJS `behaviorSvc` wrapping it for injection.
- Multi-brand asset loading (logos, favicons) — palette is done via
  CSS variables; per-brand logo/favicon files aren't ported yet.
- Deploy: `/Dockerfile.mvc` is ready for the cutover. The current
  top-level `Dockerfile` still builds the React app for production —
  swap it (or point a new Railway service at `Dockerfile`) when
  ready.

## Deploy topology after the cutover

Two Railway services:

1. **`degoudse-api`** — the existing `Insurance.Api` project, unchanged.
   Receives webhooks (AC, CloudMailin), handles the email pipeline, and
   answers `/api/*`. External systems (AC subscriptions, CloudMailin
   inbound) keep pointing at this service's public URL.
2. **`degoudse-web-mvc`** — the new MVC service. Renders every
   customer-facing page and reverse-proxies `/api/*` to the API via
   YARP. Browser stays on a single origin, so nothing needs CORS.

`Api__Endpoint` on the MVC service points at the API's URL (Railway
internal URL is fine — Railway's private networking works
service-to-service without going through the public internet).

## Webhook routing (no changes)

- **AC (`automated.claims`)** — still targets
  `https://<api-url>/api/claimwizard/webhook`. AC subscriptions do not
  need to change.
- **CloudMailin** — still targets the API's email ingest endpoint.
  Reply-to address in `CloudMailin__InboundAddress` stays the same.
- **Frontend behavior signal drops** — the AngularJS controllers post
  to `/api/claimwizard/behavior`; YARP forwards to the API which
  emails the JSON payload to `Behavior__EmailTo`.

None of the webhook routes are touched by this branch — they live in
the API project which is untouched.

## Cutover checklist

1. Point a new Railway service at `/Dockerfile.mvc`. Set env vars:
   - `Api__Endpoint` — the Insurance.Api service's Railway URL
   - `Design` — `degoudse` / `saltsys` / `globalexcel`
   - `Google__MapsApiKey` — Places API key for the address picker
2. Verify each page renders and the SSE / bot / file-upload flows work.
3. Cut DNS / whatever routing gates the customer-facing URL to the new
   service.
4. Delete `src/DeGoudse.Web/` and replace the top-level `Dockerfile`
   with `Dockerfile`.

## The porting pattern (for reference)

For a page with interactivity:

1. `Controllers/HomeController` gets a `[HttpGet("/...")]` action.
   Fetches whatever server data the view needs via `PolicyApiClient` and
   passes the raw `JsonNode` (or a strongly-typed view model) to the view.
2. `Views/Home/{Page}.cshtml` renders the layout. Data that a client
   controller needs is serialized into `window.__initialXxx` inside a
   `@section Scripts` block.
3. Client-side: an AngularJS controller (`wwwroot/app/controllers/xxx.js`)
   reads the initial state, holds it in `$scope`, and talks to
   `/api/claimwizard/*` via injected services (`policySvc`, etc.).
4. Reusable pieces become directives (`wwwroot/app/directives/xxx.js`).

`InsuredDropdown` is the reference implementation of this pattern. The
next four directives (date, address, docs, evidence) follow the same
shape.

## Running locally

Prerequisite: **.NET 10 SDK**. The API tree still targets `net8.0`, so
you'll want the .NET 10 SDK installed alongside it (side-by-side SDKs
are fine — each project resolves the one that matches its
TargetFramework).

```bash
# From this directory
npm install
npm run build            # Styles/site.css → wwwroot/css/site.css
dotnet run

# In a second terminal, run Insurance.Api on http://localhost:5142 so the
# MVC service can hit it (see appsettings.Development.json).
```

## Configuration

- `Api__Endpoint` — base URL of Insurance.Api (required)
- `Design` — brand theme id (`degoudse` / `saltsys` / `globalexcel`);
  default `degoudse`

## Why side-by-side with the React project?

Safer to iterate: the running production still serves the React app.
Once all pages are ported, the Dockerfile switches to build the MVC
project and the React folder is deleted.
