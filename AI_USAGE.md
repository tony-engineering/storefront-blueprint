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

### Docker Compose and API skeleton

I asked for a Docker Compose setup to compile and run the integration tests without installing .NET on my machine.

- **First attempt, corrected:** the AI added separate `build` and `integration-tests` tool services. I didn't want a `build` service, and asked for exactly three services instead: `db` and `api`, started by `docker compose up`, and `integration-tests`, which runs only when asked (`docker compose run --rm integration-tests`, under a `tests` profile).
- **API skeleton:** I asked for the API project with its routes but no logic yet. Every route in the architecture returns **501 Not Implemented** as ProblemDetails; `/health` returns 200. The project also declares the payment contracts the tests depend on (`IPaymentGateway`, `IPaymentReconciler` and their records) and `public partial class Program`, so the test project can compile. The API image is a multi-stage Dockerfile (SDK to build, ASP.NET runtime to run, non-root user).
- **Testcontainers inside a container:** the test container gets the host's Docker socket, so Testcontainers starts its own PostgreSQL as a sibling container and connects through `host.docker.internal`. The `PostgresFixture` did not need to change for this.
- **Keeping the host clean:** build output goes to a named volume (`--artifacts-path`), so Linux `bin/obj` never clash with my IDE's; NuGet packages are cached in another volume. Only the TRX results land in the repo (`backend/TestResults/`, git-ignored).
- **Limitation:** the AI could not pull images or reach NuGet from its environment, so it only validated the Compose file; nothing was compiled or run. Resolved in the next entry.

### First build and test run

I asked the AI to build and run the stack and the integration tests for the first time, fixing only compile, restore or Testcontainers wiring problems, and no business logic.

- **Network first:** I allowed `mcr.microsoft.com`, `registry-1.docker.io` and `api.nuget.org`. The AI checked they were reachable before doing anything else; the first time they weren't yet, so it stopped and told me.
- **Where it ran:** in the AI's cloud workspace, because the shell it can use on my Mac has no Docker. It copied the repo there and wrote the fixes back to my folder; I review and commit myself.
- **App:** `docker compose build` passed first time. `/health` returned 200 and `/api/products` 501 ProblemDetails; PostgreSQL ran on tmpfs with durability off, and the API as a non-root user.
- **Fixes:** the test project did not compile, because `PostgresFixture` uses `Npgsql` directly and nothing referenced it. The AI added `Npgsql` 10.0.3, the same line as the EF Core provider the API will use. It also replaced the obsolete `new PostgreSqlBuilder()` with `new PostgreSqlBuilder("postgres:18-alpine")`. Nothing else in the code changed.
- **Workspace-only workarounds, not in the repo:** a public mirror (`mirror.gcr.io`) for `postgres:18-alpine` when Docker Hub rate-limited the cloud IP, and a Compose override kept outside the repo so the test container could use the sandbox's HTTPS proxy. Neither affects running it on my machine.
- **Results:** the tests compile with no C# warnings, and the Testcontainers wiring works. All 5 tests fail, earlier than I expected: Respawn finds no tables, because there is no migration yet. After the first migration they will fail on the 501 stubs and the missing `IPaymentReconciler`. The test README now says so.
- **A side question:** I asked why `curl http://[::1]:8080/` returned 404. It is expected: nothing is mapped at `/`, and a 404 shows the request did reach the API. The AI started restarting Docker to reproduce it; I stopped it, since reading the routes was enough.
- **Open point:** Respawn 6.2.1 brings in an old `Microsoft.Data.SqlClient` with vulnerable dependencies (`System.Drawing.Common` 5.0.0, `Azure.Identity` 1.3.0 and others). They are test-only and never ship, but I should decide whether to move to a newer Respawn.

### Angular frontend skeleton

I asked the AI to scaffold the Angular app according to `ARCHITECTURE.md`, in a separate git worktree so it wouldn't disturb the backend work in progress on `main`. Only a hello world for now, with no UI kit.

- **Worktree:** the AI created `.worktrees/frontend-skeleton` on a new `feature/frontend-skeleton` branch, ignored locally through `.git/info/exclude` rather than by editing `.gitignore`. My uncommitted changes on `main` were left alone.
- **Skeleton:** Angular 22 (the current CLI release, checked on npm) in `frontend/`, using standalone components, signals and zoneless change detection, with Vitest and angular-eslint. A lazy-loaded hello world page, `HttpClient` set up, and a dev proxy sending `/api` to `localhost:8080`. A first Dockerfile and `nginx.conf` were added, with nginx proxying `/api` to the `api` container.
- **Folder structure, second step:** I then asked for the folder layout from §3 and §11 and a proper Dockerfile, with changes limited to `frontend/` so the merge stays easy and no Compose yet. The new folders are `core/` (`api/generated`, `auth`, `http`, `cart`), `shared/` (`components`, `pipes`, `models`) and one folder per page under `features/`. There is also an empty `src/ui-kit/` waiting for the kit to move in. The Dockerfile caches `npm ci`, takes the build configuration as an argument and has a health check. The frontend README documents the layout.
- **How it was checked:** lint, unit tests and the production build passed. The dev server served the app, and the proxy forwarded `/api` (502, since the API wasn't running). Docker wasn't available to the AI, so the image itself wasn't built. Instead it re-ran `npm ci` and the production build in a clean copy, which are the steps of the Docker build stage.
- **Snags from working on my Mac through a Linux shell:** git stored the worktree's paths as the Linux shell's paths, so the AI rewrote them to my Mac paths. It deleted the `node_modules` it had installed, because Linux binaries don't run on macOS. For the same reason it couldn't use the `node_modules` I installed later, which is why it verified in a clean copy. Git also needed my permission to delete its own lock files.
- **Kept out of a commit:** the Angular CLI wrote my analytics ID into `angular.json` when I answered its prompt. The AI left it out of the commit so it doesn't apply to everyone who clones the repo.

### Frontend in Docker Compose

Once the Angular skeleton was merged, I asked the AI to add it to Docker Compose so I can start the storefront with it.

- **New `web` service:** builds `frontend/` with its existing Dockerfile (Node build, nginx runtime) and serves it on http://localhost:4200, as `ARCHITECTURE.md` already planned. It depends on `api`, so `docker compose up --build web` starts all three. The on-demand `integration-tests` service is unchanged.
- **How it was checked:** the AI built and started the stack in its cloud workspace. `/` and client-side routes such as `/products` returned the app's `index.html`, `/api/products` reached the API through nginx (501 from the stub), and the container's health check passed.
- **A bug it found by testing:** nginx resolved `api` only once, at start-up. After the API container was recreated with a new IP, every `/api` call returned 502 until `web` was restarted too, which would happen each time I rebuild the backend. The AI changed `nginx.conf` to re-resolve `api` through Docker's DNS (`resolver 127.0.0.11` and the upstream in a variable), then repeated the test: the API came back on a new IP and calls went through without touching `web`.
- **Docs:** `ARCHITECTURE.md` §14 now shows how to start only the storefront and notes the re-resolution. It also corrects the line that said PostgreSQL wasn't exposed (it is, on `127.0.0.1:5432` only). The frontend README's Docker section now points to the Compose service.
- **Workspace-only workaround, not in the repo:** the AI's sandbox inspects HTTPS with its own certificate, so `npm ci` failed in the build container there. For its own builds it used a local `node:22-alpine` that trusts that certificate, and pulled `nginx:1.29-alpine` from the same public mirror when Docker Hub rate-limited it again. My Dockerfile is unchanged.

### GitHub Actions pipeline

I asked the AI to implement the CI design from `ARCHITECTURE.md` §13, in a new worktree (`.worktrees/a7fb3393`, branch `feature/github-actions`).

- **`ci.yml`:** runs on every push to `main` and on pull requests, cancelling an older run on the same ref. Five jobs as in the design: `backend-unit` → `backend-integration`, plus `backend-docker`, `frontend-docker` and `frontend`, which all start together. `backend-integration` has `continue-on-error: true` with a comment saying to remove it once the backend passes the tests.
- **Pinned actions:** each action is pinned to the commit SHA of its latest release, which the AI looked up from the action's git tags rather than from memory, with the version in a comment.
- **Caches and results:** NuGet packages are cached with `actions/cache` keyed on the `.csproj` files, because `setup-dotnet`'s own cache needs `packages.lock.json` files the repo doesn't have. npm uses `setup-node`'s cache, and Docker layers the GitHub Actions cache, one scope per image. Unit and integration TRX files are uploaded as artifacts even when tests fail.
- **`load-test.yml`:** manual only, with `vus` and `duration` inputs. The inputs reach the shell through environment variables, not `${{ }}` in scripts, and are validated first. It starts the stack, waits for `/health`, runs k6, uploads the summary, prints the container logs on failure and always stops the stack.
- **Not yet runnable:** `backend-unit` needs `backend/tests/Storefront.UnitTests`, still being written on `feature/unit-tests`, so it fails until that branch is merged (and `backend-integration`, which waits for it, is skipped). `load-test.yml` stops with a clear error until `loadtests/load-test.js` exists.
- **How it was checked:** both files parse and the job graph matches the design. The AI could not run the workflows or actionlint, so the first push is the real test.
