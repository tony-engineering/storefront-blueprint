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

### From specs to implementation: tests and CI first

With the specs refined, I started implementing, in this order on purpose: tests and CI first, then the code that makes them pass. Until the backend exists, the tests and the pipeline are expected to fail. The goal of the next phase is to turn them green one by one. I ran several AI sessions in parallel, each in its own git worktree and branch, and then used one more session only to integrate them into `main`.

- **What was built in parallel:** the Angular skeleton, the CI design in `ARCHITECTURE.md` §13, the two GitHub Actions workflows, the OpenAPI document with Swagger UI, the unit tests (order state machine, request hashing, money), and the EF Core persistence with the demo data seeder.
- **How I integrated them:** one worktree at a time, each with a merge commit (`--no-ff`), so the history shows what came from where. Before each merge the AI checked what the branch contained, what had changed on `main` since, and which files overlapped. The branches were merged in this order: frontend skeleton, CI design, GitHub Actions, Swagger/OpenAPI, unit tests, seeder.
- **Uncommitted work:** most of the worktrees had no commits at all, only staged or modified files. The AI committed them on their branch first and merged after, instead of copying files across, so nothing lands on `main` without a commit message.
- **An empty worktree:** when I first asked to integrate the seeder, the AI found that its worktree had no commits and no changes, and still carried the old `main` from when it was created. The other session had not written anything yet. It stopped and told me, instead of merging nothing and saying it was done. I asked again a few minutes later, when the work had appeared.
- **Merge conflicts:** the seeder and the Swagger work both changed `Program.cs`, `Storefront.Api.csproj` and `ProductEndpoints.cs`. Each conflict was two independent additions, so I kept both sides.
- **Duplicate `Money`:** the unit tests (written first) expected `Common.Money`, a stub that throws `NotImplementedException`, while the seeder branch had added its own working `Features.Money`. Keeping both would have broken the unit-test build. The AI asked me how to resolve it. I chose **one Money**: it implemented `Common.Money` to the rules the tests describe (integer minor units, a three-letter upper-case currency, no negatives, checked arithmetic that throws on overflow, an error when currencies are mixed) and switched the seeder over to it. The unit tests for `Money` should now pass.
- **A stale lock:** the seeder worktree could not be committed because the other session left a `HEAD.lock` behind. I told the AI to merge a snapshot of the staged files instead of touching the lock, then to remove the lock and all worktrees once I had no work in progress. Before deleting anything it checked that every branch was already merged and that the worktree matched the snapshot exactly.
- **What I corrected:** the AI started to run the seeder checks in its cloud workspace by starting a Docker daemon there. I stopped it. I run those checks on my own machine, and I don't want the cloud workspace's results standing in for them.
- **Kept out of the repo:** my Angular CLI analytics ID, again, as in the frontend skeleton entry.
- **What is expected to fail:** the order unit tests, because `Order` and `OrderLine` are still stubs; the integration tests, which fail until the order routes, the payment gateway and `IPaymentReconciler` exist (the pipeline lets that job fail without turning the run red, for now); and the load test, which has no k6 script yet.
- **Limitation:** no part of this was compiled or run. The AI's shell on my Mac has no .NET or Docker, and the cloud workspace was off limits for the seeder. The riskiest change is `Money`: it became a struct stored by EF Core as a complex property. If the API fails to start or the migration complains about `Price`, I'll look there first.
- **Next:** run `docker compose run --rm unit-tests` and the five seeder checks (start the stack, check the tables and rows, restart the API, call `/api/products`, reset the database). Then push to see the pipeline run for the first time, and implement the backend against the failing tests.

### Frontend implementation

I asked for a plan to implement the Angular frontend, with an explicit "keep it minimal and simple", then reviewed and adjusted it before any code was written. The work was done in its own git worktree (`.worktrees/frontend`, branch `feature/frontend`) so my main checkout stayed untouched.

- **Plan first:** the AI read the architecture, the API skeleton, the integration-test contract (`ApiContracts.cs`) and the UI kit, then proposed pages, a hand-written API client and a short test list. It listed two open decisions instead of guessing them.
- **My decisions:** I asked why it had not looked at the running API's OpenAPI document. It then read `http://localhost:8080/openapi/v1.json` through the desktop app's browser pane (its shell cannot reach my localhost). Result: the document has request schemas only, with every response typed as a bare `200 OK`, so a generated client would be all `unknown`. Rather than add response types to the API just to feed a generator, I dropped the generated client for good: with three resources and a handful of DTOs, a hand-typed layer is simpler and easier to explain (`ARCHITECTURE.md` §11). The folder `core/api/generated` from the skeleton is gone. I also decided the admin page stays unguarded until the backend has auth.
- **What was built:** product list, cart (ids and quantities only, in `localStorage`; prices always come from the catalog), checkout, order page and order lookup, and the admin page. The UI kit moved to `frontend/src/ui-kit/` and its font import became `<link>` tags.
- **Failure handling in the UI:** one idempotency key per checkout attempt, reused on retries and replaced once the outcome is known; the submit button is disabled while a request runs; 201 and 202 both open the order page; the order page polls while the payment is unresolved, survives temporary connection errors and stops at a final status; "pay with another card" is offered only when nothing was charged.
- **How it was checked:** lint, 24 unit tests (cart, key reuse on retry, double click, polling stop conditions, pay-again keys) and a production build (about 80 kB gzipped initially) all pass, in the AI's cloud workspace with Node 22.23.
- **Environment trouble:** my Mac's shell lost access to the repo folder partway through `npm ci`, so the AI built and tested a copy in its cloud workspace and copied the files back. It also had to ask for permission to delete git's own `.lock` files, which a `git worktree add` had left behind.
- **Limitation:** the order, payment and admin endpoints still return 501, so those pages were tested against mocked HTTP only. Only the product list works against the real API until the backend is implemented.


---

## 2026-10-23

### Automatic deployment to Scaleway Kapsule

I asked the AI for a plan to deploy automatically when all the CI tests pass, to my Kapsule cluster, and whether I needed to put an access key in GitHub. I was hesitating between plain Kubernetes manifests and Helm. The storefront is live at https://storefront.tony-engineering.com.

- **Plan first, from the real cluster:** I gave the AI access to `kubectl` and `scw`. It looked at what the cluster already runs (ingress-nginx, cert-manager, external-dns, the CloudNativePG operator, kube-green) before proposing anything, so the chart reuses them: one Ingress with a Let's Encrypt certificate, and a CNPG database.
- **My decisions:** Helm, because the cluster's other apps use it and `--atomic` rolls back a bad release. Migrations as their own step. The host `storefront.tony-engineering.com`.
- **Why migrations are not run at app start:** I asked. The answer I kept: with several pods, each would race to migrate; a slow or failed migration would look like a crash loop; and the app should not need schema-change rights. So the API has a `migrate` command, run by a Helm hook Job before the new pods start. This led to a rule I saved for the project: each migration must work with the previous app version (add first, remove in a later release), because old pods run against the new schema during a rollout and a Helm rollback does not undo the schema. It is in `ARCHITECTURE.md` §8.
- **Secrets:** I created the API key and the GitHub `production` environment myself, because the AI must not handle keys. The AI found that the Scaleway kubeconfig holds a legacy admin token that cannot be revoked, so CI builds its own kubeconfig from the cluster CA and uses the API secret key as the bearer token. It tested this with a temporary kubeconfig and deleted it. The environment accepts deployments from `main` only; the AI also scanned the repo for leaked keys before it goes public (clean).
- **Pieces:** `deploy/database.yaml` (CNPG cluster on a retained volume, applied outside Helm so no Helm action can delete the data), `deploy/chart/` (migrate hook, API, web, Ingress), and a `deploy` job in `ci.yml` that builds and pushes both images, checks the commit is still the tip of `main`, and runs `helm upgrade --install --atomic`. Helm is pinned and installed by a SHA-pinned action. `ARCHITECTURE.md` §13 and §16 describe it.
- **What I corrected:** the AI tried to create the registry namespace itself; the tool refused (it makes a public resource), so it stopped and asked me, and I created it.
- **First push, red for an old reason:** the integration tests did not compile (`HttpResponseHeaders`, a `using` removed by an earlier "Reconcile" commit), so `main` was already red and `deploy` was skipped, as designed. The AI restored the line and ran the integration tests locally in Docker (46 of 46 passed) before pushing again. It first misread an older results file with 87 failures; it caught the mistake by checking the newest file.
- **The deploy then failed three times, none from our code:**
  - *Slow image pulls.* The PostgreSQL image (253 MB) took 5 min 20 s on one node. The deploy job's 10-minute wait for the database ran out. Nothing was installed, so I re-ran the job.
  - *Memory starved nodes.* I asked the AI to check the node metrics. The three DEV1-M nodes (about 2.3 GB usable each) ran at 86 to 110% memory, while CPU was at 14 to 28%. The migration then timed out and one pod crashed with exit 139.
  - *The autoscaler did not add a node.* I asked why, with a pool of 2 to 4. The AI showed it scales on pods that cannot be scheduled, using their requests; no pod was `Pending`, and requests were only 77 to 89% because other apps ask for little and use a lot. So it was working as designed. I then asked the AI to resize the pool to 4 with `scw`, and it did; the autoscaler removed the extra node again soon after.
- **Last obstacle:** one node reported "container runtime is down" with a full disk (image garbage collection failing), and the database and a migrate pod were on it. The database pod was killed by its liveness probe, although it used almost no CPU or memory. The AI laid out the choices (wait for auto-healing, replace the node, or cancel) and asked me; it changed nothing more on the cluster. After a while the cluster settled and the site came up.
- **A question it answered plainly:** I asked whether Kubernetes can upgrade Helm. No: Helm is a client that only runs inside the CI job.
- **Limitations and open points:**
  - The API uses the database owner role; a DML-only role is next. There are no database backups yet.
  - The demo catalog is seeded in production (`seedDemoCatalog: true`); turn it off for a real shop.
  - The cluster is too small for another app: add nodes or raise memory requests so the autoscaler can see the real load. One node (`e6e101`) still has a nearly full disk and should be replaced.
  - Not yet done: require SHA-pinned actions in the repo settings, and turn on secret scanning and push protection when the repo goes public. Branch rules for the `production` environment may not be enforced on a private repo on the Free plan.
  - The AI did not check the live site after the last run; I opened it myself and it worked.
