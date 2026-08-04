# insurance

Consolidated demo insurance stack. Replaces the older `degoudseclaim` +
`degoudseportal` repos and their side-by-side React frontends.

Three deployable units in one .NET solution:

| Project             | Role                                                                          |
| ------------------- | ----------------------------------------------------------------------------- |
| `Insurance.Api`     | Merged API — portal auth (JWT), portal dashboard, claim wizard, bot, email-ingest, AC webhooks. One Postgres, one Program.cs. |
| `Insurance.Portal`  | MVC frontend — login (email → password / set-password / OAuth) + policy dashboard. |
| `Insurance.Claim`   | MVC frontend — wizard (`/`, `/dashboard/{id}`, `/file-claim/{id}`, `/claim-submitted/{id}`, `/claim/{id}/evidence`) and bot iframe target (`/bot`). |

Stack: .NET 10 · ASP.NET Core MVC · Razor · YARP · jQuery + AngularJS 1.x
+ Tailwind CSS 3.4 (standalone CLI, no Vite/bundler) · Postgres (portal
identity) · Anthropic SDK (bot/email extraction) · automated.claims
(external) · Resend (outbound email) · CloudMailin (inbound email).

## Repo layout

```
insurance/
├── Insurance.sln
├── Dockerfile.api          — one per deploy target
├── Dockerfile.portal
├── Dockerfile.claim
└── src/
    ├── Insurance.Api/
    ├── Insurance.Portal/
    └── Insurance.Claim/
```

Each MVC project also carries its own `package.json` + `tailwind.config.js`.
The Tailwind CLI compiles `Styles/site.css` → `wwwroot/css/site.css` at
Docker build time; the compiled CSS is gitignored.

## Client-side stack notes

The dev picked **jQuery 3.7 + AngularJS 1.x** for the client side.
AngularJS is out of long-term support since Dec 2021 — a security review
will flag it. Every AngularJS pitfall we hit during the migration is
already fixed in the code:

- Scripts load without `defer` (avoids the `nomod` race).
- Every `ng-model` uses the "dot rule" (`ng-model="form.field"`) so ng-if
  child scopes don't shadow the parent.
- Directive factories don't inject `$scope` (that only works inside
  link functions).
- Tailwind `content` globs cover `wwwroot/app/**/*.js` so utility classes
  used only inside directive templates survive the purge.
- `$sceDelegateProvider.resourceUrlWhitelist` allows Railway hosts for
  the bot iframe embed.

## Deploy (Railway)

Three Railway services, one per Dockerfile:

| Service             | Dockerfile          | Required env vars |
| ------------------- | ------------------- | ----------------- |
| `insurance-api`     | `Dockerfile.api`    | `ConnectionStrings__DefaultConnection`, `Jwt__Secret`, `PolicySystem__Endpoint`, `ClaimsApi__Endpoint`, `ClaimsApi__ApiToken`, `Resend__ApiKey`, `Resend__FromAddress`, `CloudMailin__InboundAddress`, `Behavior__EmailTo`, optional `Auth__Google__*` + `Auth__Microsoft__*` |
| `insurance-portal`  | `Dockerfile.portal` | `Api__Endpoint` (URL of `insurance-api`), `Design`, `ClaimApp__Url`, `Bot__Url`, `ClaimEmail` |
| `insurance-claim`   | `Dockerfile.claim`  | `Api__Endpoint`, `Design`, `Google__MapsApiKey` |

The two MVC services reverse-proxy `/api/*` to `Api__Endpoint` via YARP,
so the browser always talks to a single origin. Webhooks (AC,
CloudMailin) target `insurance-api` directly.

## Running locally

Prerequisites: **.NET 10 SDK**, Node 20, Postgres.

```bash
# API
cd src/Insurance.Api
dotnet run

# Portal MVC (new terminal)
cd src/Insurance.Portal
npm install && npm run build
dotnet run

# Claim MVC (new terminal)
cd src/Insurance.Claim
npm install && npm run build
dotnet run
```

Portal listens on 5210, Claim on 5220 (see `Properties/launchSettings.json`).
Set `Api__Endpoint` to the API's URL in each MVC service's
`appsettings.Development.json`.

## Migration notes

- Portal repo (`degoudseportal`) and claim repo (`degoudseclaim`) remain on
  GitHub as read-only history. Their `mvc-conversion` branches are the
  direct ancestors of this repo's code — every bug fix landed there was
  carried over.
- OAuth callback URIs at Google + Microsoft must be updated to point at
  `insurance-api`'s public URL before the portal cutover.
- Postgres identity tables (AspNetUsers, etc.) are managed by EF Core
  migrations; `Program.cs` applies pending migrations on startup.
