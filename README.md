# Storefront Blueprint

A minimal storefront made of a .NET 10 API and an Angular client, where users browse products, place orders and look them up, and admins create and delete products. Payment is mocked, but the system is designed as if real money were involved, so repeated requests and partial failures produce predictable, meaningful responses. It runs on an in-memory PostgreSQL with hardcoded seed data, which keeps it lightweight without giving up real transactions and constraints.

## Live demo

- Frontend: `<placeholder: link to frontend>`
- Swagger (API docs): `<placeholder: link to swagger>`

## Run locally

Requires Docker with Compose. Nothing else (no .NET or Node) needs to be installed.

```bash
# Start the whole stack: PostgreSQL (db), API (api) and Angular app behind nginx (web)
docker compose up --build web
```

| What | Where |
|---|---|
| Storefront | http://localhost:4200 |
| API | http://localhost:8080 |
| Swagger UI | http://localhost:8080/swagger |
| Health check | http://localhost:8080/health |

Run the tests (on demand, nothing .NET is installed on the host):

```bash
# Unit tests
docker compose run --rm unit-tests

# Integration tests (use Testcontainers, so they need the Docker socket)
docker compose run --rm integration-tests
```

Colima / Rancher users: set `DOCKER_SOCKET` to your socket path, e.g. `DOCKER_SOCKET=$HOME/.colima/default/docker.sock`. Test results (TRX) are written to `backend/TestResults/`.

## Architecture decisions

The backend is a single .NET 10 Minimal API organised by feature (products, orders, auth), using EF Core on PostgreSQL 18 with its data directory on tmpfs. That gives in-memory behaviour (fast, reset on restart) on the same engine used in tests and CI. The Angular client (standalone components, signals) is served by nginx, which proxies `/api` to the API so the browser sees one origin.

Resilience is built around the two hard requirements in the brief:

- **Repeated requests:** order creation is protected by idempotency keys, so a retried `POST` returns the original result instead of creating a second order or charge.
- **Partial failures:** orders follow an explicit state machine (`Pending`, `Paid`, `PaymentFailed`, `PaymentUnknown`, `PaymentUnavailable`, `NeedsAttention`). The order is saved *before* the payment gateway is called, a timeout is treated as "unknown" (202) rather than "failed", and a charge is never retried blindly. A background reconciler asks the gateway for the status of unclear payments instead of charging again.
- **Consistency:** money is stored as integer minor units, orders snapshot product names and prices, and optimistic concurrency (`xmin`) guards concurrent updates.

Full details, diagrams, trade-offs and assumptions: [ARCHITECTURE.md](./ARCHITECTURE.md)

## How it was built

AI was used throughout as a sparring partner and implementation helper, with me making and defending the decisions. I had it propose designs and then challenged them (for example, pushing the database choice from SQLite to PostgreSQL on tmpfs), had it fact-check versions and current behaviour before committing to them, and corrected it when it went off track. Work happens locally and in separate git worktrees, I review every change and push myself, and the AI states plainly what it could and couldn't verify (for example, code that was reviewed but not compiled).

A dated journal of what the AI did, what I decided and what I corrected: [AI_USAGE.md](./AI_USAGE.md)
