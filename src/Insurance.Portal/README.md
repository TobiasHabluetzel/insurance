# Insurance.Portal

ASP.NET Core MVC (.NET 10) port of the React portal in
`(legacy repo)`. Runs alongside `Insurance.Api`.

Client-side stack matches the degoudseclaim MVC port:
- **jQuery 3.7 + AngularJS 1.x** (AngularJS is out of LTS since Dec 2021;
  the dev chose the stack, a security review will flag it)
- **Tailwind CSS** via the standalone CLI (no Vite)
- **Vanilla JS** where either would be more effort than benefit

## What's ported

| React page      | MVC route     | Status                                     |
| --------------- | ------------- | ------------------------------------------ |
| LoginPage       | `/`           | ✅ 3-step wizard (email / password / set)  |
| DashboardPage   | `/dashboard`  | ✅ Policy list + chat widget + card links  |

Directives: `policyCard`, `chatWidget`. Services: `authSvc` (JWT
lifecycle + /api/auth wrapper), `portalSvc` (/api/portal wrapper).

## Auth model

Kept **client-side** (localStorage) to mirror the React version — the
portal API is Bearer-token, not cookie-auth, so this is a direct port.
`DashboardController` bounces to `/` if no token is stored; a 401 from
`/api/portal/dashboard` also clears the token and bounces.

Alternative: server-side session cookies via ASP.NET auth middleware.
That's more idiomatic for MVC but requires API changes (a token→cookie
translation layer), so it's out of scope until the dev asks for it.

## Running locally

Prerequisite: **.NET 10 SDK**.

```bash
cd src/Insurance.Portal
npm install
npm run build            # Styles/site.css → wwwroot/css/site.css
dotnet run

# In another terminal, run Insurance.Api so /api/auth/* and
# /api/portal/* resolve. During dev, either front the MVC service with
# the same host, or configure a dev proxy to forward /api/* to the API.
```

## Configuration

- `Design` — brand id (`degoudse` / `saltsys` / `globalexcel`)
- `ClaimApp__Url` — deep-link target for "File a claim"
- `Bot__Url` — chat widget iframe src
- `ClaimEmail` — CloudMailin inbound address for "Claim by mail"

## Deploy topology after the cutover

Two Railway services:

1. **`portal-api`** — the existing `Insurance.Api` project,
   unchanged. Owns the Postgres user database, JWT issuance, and OAuth
   callbacks. Google/Microsoft OAuth redirect URIs continue to target
   this service's URL.
2. **`portal-web-mvc`** — the new MVC service. Renders login and
   dashboard, reverse-proxies `/api/*` to the portal API via YARP so
   the browser sees a single origin.

`Api__Endpoint` on the MVC service points at the portal API's URL
(Railway private URL works — no need to go through the public
internet).

## OAuth callback routing (no changes)

Google + Microsoft OAuth callbacks target
`https://<portal-api-url>/api/auth/oauth/callback/{provider}` today —
that stays. The MVC service's `/api/auth/oauth/challenge/{provider}`
link forwards through YARP to the API which issues the 302 to the
provider, so the redirect chain still ends on the API. No IdP redirect
URI needs updating.

## Cutover checklist

1. Point a new Railway service at `/Dockerfile.mvc`. Set the env vars
   above plus routing so `/api/*` reaches `Insurance.Api`.
2. Verify the login flow (email → password / set-password), OAuth
   callbacks, dashboard load, chat widget, and both card CTAs.
3. Cut DNS to the new service.
4. Delete `(legacy repo)` and replace the top-level
   `Dockerfile` with `Dockerfile`.
