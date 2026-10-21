# `POST /api/emailclaims/ingest` — what it does, step by step

Turns an inbound email into a claim in automated.claims (AC), or attaches the
email's files to an existing claim if it is a reply.

Source: `src/Insurance.Api/Controllers/EmailClaimsController.cs`

---

## Who calls it

An inbound-email provider posts the received message here as a webhook. Both are
supported, and their field names differ:

| Provider | Sender field | Subject | Body | Files |
| --- | --- | --- | --- | --- |
| SendGrid Inbound Parse | `from` | `subject` | `text`, `html` | `attachment1`, `attachment2`, … |
| CloudMailin (Multipart Normalized) | `headers[from]`, `envelope[from]` | `headers[subject]` | `plain`, `html` | `attachments[]` |

Content type is `multipart/form-data` (or `application/x-www-form-urlencoded`).
There is **no authentication** on this endpoint — anyone who can reach the URL
can post to it.

---

## Step 1 — Work out who sent it

Take the first non-empty of `from`, `headers[from]`, `envelope[from]`.
Fields with a display name are preferred — CloudMailin's `envelope[from]` is the
bare address, `headers[from]` has the `"Name <email>"` form.

Parse it into **email**, **first name**, **last name**:

- `"Jan de Vries" <jan@example.com>` → `jan@example.com`, `Jan`, `de Vries`
- `jan@example.com` → `jan@example.com`, no name

If there is no display name, derive one from the address local part: split on
`.`, `_`, `-` and capitalise. `jan.de.vries@…` → `Jan`, `De Vries`.

## Step 2 — Get the subject and body

Subject: first non-empty of `subject`, `headers[subject]`.

Body: prefer `text` / `plain`. If only HTML is present, strip the tags with a
regex and use what is left.

## Step 3 — Decide: new claim, or a reply?

Search the **subject** for an AC claim UUID (standard 8-4-4-4-12 hex).

- **Found** → it is a customer reply → go to *Reply path* below, and stop.
- **Not found** → it is a new claim → continue with step 4.

> Note: replies are only recognised by raw UUID. Our own outbound emails put the
> short code (`CLM-F9KX7T`) in the subject, not the UUID, so **a customer
> replying to our confirmation email creates a second claim rather than
> attaching to the first.** Resolving short code → claim id would need a call to
> ClaimsOps, which has not been built. A `ShortCodePattern` regex is defined in
> the file but never used — that is the unfinished half of this.

---

# New-claim path

## Step 4 — Generate a reference

`ShortCodeService.Generate()` produces e.g. `CLM-F9KX7T`:
prefix from `App:ReferencePrefix` (default `CLM`) + 6 characters from
`ABCDEFGHJKMNPQRSTVWXYZ23456789` — no I, L, O, U, 0 or 1, so it survives being
read over the phone. Uniqueness is **not** checked here; ClaimsOps enforces it
when its webhook upserts the row.

## Step 5 — Create the claim in AC

Build a plain-text document:

```
Email: {senderEmail}

{email body}
```

The sender's address is prepended so AC always has a way to contact the
claimant, even if the body does not mention it.

`POST {ClaimsApi}/claims/upload?externalId={shortCode}` as `multipart/form-data`
with one part, `file` = `claim.txt`.

> **Gotcha:** the part's `Content-Type` must be exactly `text/plain`. Adding a
> charset suffix makes AC reject the upload with "Unsupported file type".

**AC parses the claim out of the body itself** — it reads the structured
`Claimant` / `Incident` sections and creates the claim record. We do no
extraction on our side. (There is an `EmailExtractionService` in the codebase,
but it is registered in DI and never called — do not assume it runs.)

**If this call fails the whole request fails**, returning AC's status code and
body so the provider's retry/logging sees a real error.

Read the new claim id from the response: `claim.id`, falling back to a top-level
`id` if AC changes the shape.

## Step 6 — Put back what the upload dropped

`PUT {ClaimsApi}/claims/{claimId}` with:

```json
{ "externalId": "CLM-F9KX7T", "productCode": "salsin_v3" }
```

`/claims/upload` silently discards `externalId` and `productCode` when they are
passed as query parameters, so they are written again here. Without this, AC's
outgoing webhook and the ClaimsOps inbox carry neither our reference nor the
product.

**Failure is logged as a warning and ignored** — the claim already exists and is
worth keeping.

## Step 7 — Upload the attachments

For **every** file part, regardless of field name (covers SendGrid's
`attachment1…N` and CloudMailin's `attachments[]`):

`POST {ClaimsApi}/claims/{claimId}/documents` as multipart, part named `file`,
preserving the original filename and content type.

Each failure is logged and the loop continues — one bad attachment must not cost
the others.

## Step 8 — Confirm to the sender

Send via Resend: `SendClaimReceivedAsync(senderEmail, firstName, shortCode, claimUrl)`

- Subject: `Claim received – reference CLM-F9KX7T`
- Body: the reference, plus a button to **open the claim and add missing
  evidence**
- `claimUrl` = `{App:BaseUrl or the request's own scheme+host}/claim/{claimId}/evidence`

## Step 9 — Respond

```
200 OK  { "claimId": "...", "reference": "CLM-F9KX7T" }
```

---

# Reply path

Taken when the subject contained an AC claim UUID.

## Step R1 — Verify the claim exists

`GET {ClaimsApi}/claims/{claimId}`.

If it does not exist, stop and return `200 OK { "ignored": true, "reason":
"claim-not-found" }`. This is deliberate: a stray UUID in a subject line would
otherwise create orphaned documents against a claim that is not there.

## Step R2 — Upload the attachments

Same as step 7 — `POST claims/{claimId}/documents` for each file, failures
logged and skipped.

## Step R3 — Respond

```
200 OK  { "claimId": "...", "replyAttachments": 2 }
```

No confirmation email is sent on the reply path.

---

## Error-handling philosophy

Worth understanding before changing anything here:

| Stage | On failure |
| --- | --- |
| Create claim in AC (step 5) | **Fatal** — returns AC's status, nothing else runs |
| PUT externalId + product (step 6) | Warn and continue |
| Each attachment (step 7, R2) | Log and continue to the next file |
| Confirmation email (step 8) | Handled inside `EmailSender`; does not fail the request |

The rule is: once the claim exists in AC, the request succeeds. Everything after
that point is best-effort, because losing a claim is far worse than losing an
attachment or an email.

## Things to know before working on it

- **No authentication.** Anything that can reach the URL can post a claim.
- **No idempotency.** The same email delivered twice creates two claims. Email
  providers retry on non-2xx, which is why step 5 is the only fatal step.
- **Nothing is persisted locally.** No claim row is written on our side; the
  record lives in AC and is hydrated into ClaimsOps by AC's own webhook.
- **Replies only match on UUID**, not on our short code — see step 3.
- Everything is logged under the `[email-ingest]` prefix, including the field
  names received, which is the quickest way to diagnose a provider whose
  payload shape has changed.
