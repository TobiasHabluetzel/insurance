# Insurance stack — functionality overview

**Status: 11 October 2026.** Compiled by reading the controllers, services and
frontend modules, not from the roadmap — so "what exists" means what is wired up
and reachable today.

Three deployable units in one .NET solution:

| Unit | Role |
| --- | --- |
| `Insurance.Api` | Merged API — portal auth, portal dashboard, claim wizard, bot, email ingest, webhooks |
| `Insurance.Portal` | MVC frontend — login and policy dashboard |
| `Insurance.Claim` | MVC frontend — claim wizard and the bot iframe target |

Throughout this document:

- Plain bullets describe **what exists today**
- Bullets marked **(+)** are **options that could be added**

---

## One dependency that gates most of the options

The portal has **no access to claims at all** today, and this is architectural
rather than a gap in the UI.

Claims live in the external ClaimsOps / automated.claims system. This stack holds
only ASP.NET Identity in its database — there is no claims table and no local
copy. The webhook from automated.claims is received, the incident is classified,
and the result is pushed to an **in-memory** event channel for any browser that
happens to be watching. Nothing is persisted; close the tab and it is gone.

Every option below under *Claims on the portal*, *Continue a claim* and
*What is missing on a claim* therefore needs a **claims read path** first. Three
ways to get one:

| Approach | How | Trade-off |
| --- | --- | --- |
| Query live | Portal asks ClaimsOps for claims on demand | No new storage, always current. Slower pages, and the portal fails when ClaimsOps does |
| **Local projection** | Persist claim summaries from the webhook already being received | Fast, survives restarts, enables lists, counts and filters. A second copy that can drift |
| Hybrid | List from the projection, detail fetched live | Best experience, most moving parts |

**Recommended: the local projection.** Every event is already arriving and being
discarded; this keeps a summary of it. One table and one handler change, and it
unlocks everything else.

---

## Portal

### Login

- Email-first — one field, and the flow branches on what is found
- By password, for a known user
- Microsoft
- Google
- Set a password, where the email matches the policy system but no account exists
- Email is stored hashed, never in clear
- **(+)** Login by policy number
- **(+)** Password reset / forgotten password
- **(+)** Two-factor authentication

> Google and Microsoft are registered only when their environment variables are
> present, so they disappear silently if the configuration is missing.

### Policy overview

- Active policies only — cancelled policies are filtered out
- Policy number, product name, cover period, destination, status
- Travellers per policy, with the policyholder flagged
- **(+)** Archive toggle to show cancelled policies
- **(+)** Policy documents to download — certificate, wording

### Claims on the portal

- Claims count is displayed, but **hardcoded to zero**
- **(+)** Real claims count
- **(+)** Claims list per policy — reference, category, status, date filed
- **(+)** Claim detail with an event timeline and the documents submitted
- **(+)** Status badges — submitted, in review, evidence outstanding, settled, rejected
- **(+)** Claim history across all policies rather than per policy

### Continue a claim

- **(+)** Resume an unfinished claim from the portal
- **(+)** A "needs your attention" section for claims with open requests

> The wizard currently starts from a policy, so resuming needs a new entry point
> keyed on the claim rather than the policy. This is the largest of the options
> in this group.

### What is missing on a claim

- **(+)** Outstanding evidence listed per claim, with upload in place
- **(+)** "I cannot provide this" — reusing the path that already exists in the claim app
- **(+)** Progress indicator, e.g. three of five documents received
- **(+)** Automatic chase reminders where evidence is outstanding beyond a set period

> The data already exists. automated.claims fires an `evidence_evaluated` event,
> and the stack already emails the customer a list of what is missing. It simply
> never reaches the portal.

### Other

- Embedded chat widget — the bot, in an iframe
- **(+)** Notification preferences, e.g. email on status change

---

## Claim app

### Finding a policy

- By email address
- By policy number

### Claim wizard

- A policy dashboard shown before filing
- Guided decision trees, with six flows:
  - Accident
  - Medical
  - Travel disruption
  - Theft or loss
  - Property damage
  - Generic
- Question types: yes/no, free text, and file upload, branching on the answer
- Submit a claim, which returns a human-readable reference such as `CLM-F9KX7T`
- Update a claim after submission
- Document upload
- **(+)** Save and resume a part-finished claim
- **(+)** Choose which traveller the claim is for, from the policy's list
- **(+)** Download a claim summary as a PDF

### Evidence

- Upload requested evidence
- Report evidence as unobtainable
- Live claim status via server-sent events — only while the browser stays open

### Supporting interface

- Address picker
- Date picker
- Insured-person dropdown
- Question cards and evidence sections

---

## Bot

- Scripted welcome, then free conversation
- AI extraction pulls the claim data out of the conversation
- Submits directly to the claims system, including documents
- Runs standalone and embedded in the portal
- **(+)** Hand-off to a human agent
- **(+)** Resume a conversation later

---

## Email intake

- Inbound email received via CloudMailin
- AI extraction turns a free-text email into a claim
- Outbound replies sent via Resend
- An automatic email listing missing evidence, sent once per claim and de-duplicated
- **(+)** Mirror that missing-evidence email into the portal

---

## Behind the scenes

- Webhook from automated.claims, with AI classification of the incident category
- Behaviour signals emailed on submit, including the time elapsed since any prior submission
- Reverse proxy, so the browser only ever talks to a single origin
- Human-readable claim references generated locally; uniqueness is enforced by ClaimsOps

---

## Suggested order, if the options are taken up

1. **Claims projection table** — nothing in groups 2 to 4 works without it
2. **Claims list and count on the portal** — turns the hardcoded zero into something real
3. **Outstanding evidence in the portal** — the highest customer value, and the data already arrives
4. **Resume a claim** — needs a new wizard entry point, so the largest of the four
5. The remaining smaller items, as polish

---

## Known simplifications

These are deliberate demo shortcuts rather than defects, recorded so they are not
mistaken for finished behaviour.

- The portal's claims count is hardcoded to zero
- Claim state is not persisted in this stack at all
- The live status stream is in-memory, so it does not survive a restart or a closed tab
- Behaviour-signal emails are dropped silently if the recipient is not configured
- AngularJS 1.x is out of long-term support since December 2021 and will be flagged
  by any security review
