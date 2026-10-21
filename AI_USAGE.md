# AI usage journal

A running log of how I use AI on this project. Entries are dated and added in order, newest at the bottom.

---

## 2026-10-21

I use AI, for example, to get architecture suggestions, challenge the decisions, ask to switch to another database system, refine the technology used for tests, and so on.

### Architecture design

I used the AI as a sparring partner to design the architecture, then challenged its proposals until each decision was one I could defend. The result is `ARCHITECTURE.md`.

- **First pass:** I asked for a lightweight architecture (in-memory database, Swagger, Docker Compose, a mocked payment system, an Angular storefront) and whether a queue was needed to recover from payment failures. Without the brief, the AI proposed Spring Boot + H2, a transactional outbox and a WireMock payment container. Wrong stack, but useful to frame the failure-handling question.
- **Revised with the brief:** once I gave it the brief, the AI moved to .NET 10 + EF Core and centred the design on the two hard requirements: idempotency keys for repeated requests, and an order state machine (`Pending`, `Paid`, `PaymentFailed`, `PaymentUnknown`, `NeedsAttention`) for partial failures. Key ideas I kept: save the order *before* calling the gateway, treat a timeout as "unknown" (202) rather than "failed", never retry a charge blindly, and let a background reconciler *ask* the gateway for status instead of charging again.
- **Queue question answered:** the orders table acts as the durable work queue for the reconciler; a broker is listed as the next step for multiple instances or real money.
- **Scope advice:** the AI pointed out that the follow-up interview means every part must be explainable, so it suggested keeping scope tight and starting this journal.
- **CI and tests:** I specified GitHub Actions on every push to `main`, unit + integration tests (xUnit, WebApplicationFactory, Testcontainers, Respawn), and a k6 load test as a separate, manually triggered workflow so it never blocks the pipeline. I asked for the diagrams in Mermaid.
- **Database challenge:** the AI first proposed a hybrid (SQLite in-memory for the app, PostgreSQL only in tests). I asked why tests shouldn't use an in-memory database too. In the discussion the AI overstated SQLite's concurrency limits and corrected itself (SQLite serialises writers but still enforces the unique constraint), and caught a bug in its own design (one shared SQLite connection is not thread-safe).
- **Decision:** I asked about other in-memory options. The AI ruled out the EF Core InMemory provider (no transactions or constraints) and DuckDB (analytics-oriented), and proposed PostgreSQL with its data directory on tmpfs. I chose it for app, tests and CI: in-memory behaviour, one engine everywhere, real concurrency in tests.
- **Fact-checking:** before I committed to it, the AI checked current sources: the Npgsql EF Core provider supports .NET 10 / EF Core 10, and the PostgreSQL 18 Docker image moved its data directory, so the tmpfs must be mounted at `/var/lib/postgresql`. It left the exact image tag to pin at scaffolding instead of guessing one. Follow-on changes: EF migrations instead of `EnsureCreated()`, an `xmin` row version on orders, one place to detect unique violations.
- **What I corrected:** the AI pushed its first commit straight to GitHub from its own copy of the repo. I told it to always work in my local repo first; since then it writes changes locally and I review and push them.
- **Open points:** no circuit breaker yet for long payment outages; data is lost if the database container restarts (accepted for this exercise).

### UI kit for the frontend

I asked the AI to scrape a free storefront HTML/CSS UI kit (code-generator.net, "Storefront") and turn it into a small, reusable CSS/JS kit for the Angular frontend, kept to the basics for a demo store selling tiny houses. The result is in `ui-kit/`.

- **What the AI did:** extracted the kit's design tokens and component markup/CSS, kept only what the store needs (navbar, hero, product cards, buttons, forms, badges, tables, order timeline, modal, footer) and added what the order flow was missing (payment-outcome alerts and toasts, empty state, quantity stepper, cart summary, mobile menu).
- **Decisions I kept:** prices are formatted from integer minor units (`formatMoney`), matching the API's money model; one `orderStatus()` map drives labels and colours for every order status; a `withBusy()` helper disables the submit button to prevent double orders; modals use the native `<dialog>` element; classes are prefixed `sf-` so they don't clash with Angular or other libraries.
- **Fixes over the original kit:** text contrast (white on bronze was 2.5:1), visible focus outlines, a working mobile menu, and removal of an invalid CSS declaration and `!important` overrides.
- **How I checked it:** the AI rendered `ui-kit/demo.html` at desktop and mobile widths, exercised the interactions (cart totals, mock payment tokens, delete confirmation, form validation) and type-checked `storefront.d.ts` under strict TypeScript.
- **Open point:** the source page states no licence, so the kit is used for this demo only.

### Integration tests for the payment flow

I gave the AI five checkout scenarios to cover with integration tests: paid first time; declined and abandoned; declined then paid with another card; provider down for 30 seconds then back; provider down too long, so the system gives up and tells the customer they have not been charged. The result is in `backend/tests/Storefront.IntegrationTests/`.

- **Conflicts it flagged before writing anything:** the backend did not exist yet; the design said a declined order was final (a retry meant a new checkout); and the design never retried a charge, so "ride out a 30-second outage" and "hit the retry limit" had nothing to test against. It asked me to decide rather than guess.
- **My decisions:** tests first, before the backend exists. Card retries happen on the **same order** as a new payment attempt (`POST /api/orders/{id}/payments`). A new **"provider unavailable" state**, separate from "timeout": connection refused or 503 means nothing was charged, so the system may retry the charge with backoff, reusing the attempt's idempotency key. After the limit, the order becomes `PaymentUnavailable` and the customer is told they were not charged. Timeouts are still only status-checked, never re-charged.
- **How the tests work:** a fake clock (`FakeTimeProvider`) and a scripted payment provider replace the real ones, and the tests drive the reconciler step by step, so a two-hour outage runs in seconds with no sleeps. Each scenario also checks the money invariant: charged at most once, for exactly the total.
- **Follow-on changes:** `ARCHITECTURE.md` updated (payment attempts, new states, retry policy, new endpoint); the UI kit's status map got the two new statuses and their customer messages. The contract the backend must meet is in the test project's `README.md`.
- **Limitation:** no .NET SDK or NuGet access was available in the AI's environment, so the tests were reviewed but not compiled. Package versions were checked on nuget.org; Respawn and xUnit are pinned to the last versions whose API the AI knew, not the newest majors.
- **Open point:** still no circuit breaker; during a long outage every new order makes its own six calls to the provider.
