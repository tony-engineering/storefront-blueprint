# Storefront.IntegrationTests

End-to-end tests of the API over real HTTP, against the same PostgreSQL-on-tmpfs setup the app runs on. They are written **test-first**: they describe the behaviour the backend must have, and they compile against the API skeleton in `backend/src/Storefront.Api`, which declares the contract types below. Until the backend is implemented they fail, in this order:

1. **In the fixture, before any test runs:** Respawn reports `No tables found`, because there is no EF Core migration yet and it needs at least one table to build its delete plan.
2. **Against the stubs:** every route returns 501, and `IPaymentReconciler` is not registered.

## Running

Only Docker is needed; the .NET SDK runs in a container. From the repo root:

```bash
docker compose run --rm integration-tests      # compile and run the tests
docker compose run --rm integration-tests --filter "FullyQualifiedName~Happy_path"   # extra args go to dotnet test
```

The test container gets the host's Docker socket, so Testcontainers starts `postgres:18-alpine` as a sibling container and reaches it through `host.docker.internal`. Results are written to `backend/TestResults/integration-tests.trx`; build output and the NuGet cache stay in named volumes (`docker compose down -v` clears them). With Colima or another non-default socket, set `DOCKER_SOCKET` to its path first.

With the .NET 10 SDK installed locally, `dotnet test backend/tests/Storefront.IntegrationTests` works too (Docker must still be running).

Time is simulated, so the outage scenarios finish in seconds.

## Payment scenarios — `Payments/CheckoutPaymentTests.cs`

| Test | Scenario | Expected outcome |
|---|---|---|
| `Happy_path_order_is_paid_on_the_first_attempt` | Order placed, card charged | **201**, `Paid`, one charge for the order total |
| `Declined_card_without_retry_leaves_the_order_failed_and_uncharged` | Card declined, customer leaves | **201**, `PaymentFailed` + decline code; 30 min later still failed, never retried, nothing charged |
| `Declined_card_then_retry_with_another_card_pays_the_same_order_once` | Declined, then `POST /orders/{id}/payments` with another card | **201**, same order `Paid`; attempts `[Declined, Succeeded]`; one charge, on the second card |
| `Provider_down_for_30_seconds_…_gets_a_confirmation` | Provider unreachable for 30 s | **202** `PaymentRetrying`; background retries with the same idempotency key; polling shows `Paid`; one charge |
| `Provider_down_for_too_long_…_not_charged` | Provider unreachable for 2 h | **202** `PaymentRetrying`; after exactly `MaxChargeAttempts` calls → `PaymentUnavailable`, `charged: false`, `payment_provider_unavailable`; answer within ~1 min; no further calls |

Every scenario also checks the invariant that matters for real money: **the customer is charged at most once, for exactly the order total, and is told "paid" or "not charged" only when that is true at the provider.**

## How the tests control time and the provider

- **`FakeTimeProvider`** replaces `TimeProvider` in DI. Tests advance it one second at a time; nothing sleeps.
- **`ScriptedPaymentGateway`** replaces `IPaymentGateway`. Tests script declines (`Decline(token, code)`) and outages (`StartOutage(duration)`, measured on the fake clock). It de-duplicates charges by idempotency key, like a real provider, and records every call and charge.
- **The reconciler loop is disabled** (`Payments:Reconciler:Enabled=false`). Each simulated second the test calls `IPaymentReconciler.RunOnceAsync` itself, and every 5 s it polls `GET /api/orders/{id}` as the checkout page does. Retries therefore happen at known moments and the tests are deterministic.
- **Isolation:** one container per run; before each test Respawn empties every table except `__EFMigrationsHistory`, and a fresh API instance (own clock, own gateway) re-seeds the catalog on start-up.

## Contract the backend must satisfy

### Hosting

- `public partial class Program;` in `Program.cs` so `WebApplicationFactory<Program>` can reach it.
- Configuration is read lazily (options pattern, or `AddDbContext((sp, o) => …)`), so settings supplied by the factory are seen.
- `Database:MigrateOnStartup` and `Database:SeedOnStartup`: when true, migrations and the idempotent catalog seed finish **before** the host reports started.
- Everything that reads the time uses the injected `TimeProvider`.

### Types in `Storefront.Api.Payments`

```csharp
public interface IPaymentGateway
{
    Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken);
    Task<ChargeStatusResult> GetStatusAsync(string idempotencyKey, CancellationToken cancellationToken);
}

public sealed record ChargeRequest(string IdempotencyKey, Guid OrderId, long AmountMinor, string Currency, string PaymentToken);

public enum ChargeOutcome { Succeeded, Declined, Unavailable, Unknown }
public sealed record ChargeResult(ChargeOutcome Outcome, string? ChargeId = null, string? DeclineCode = null);

public enum ChargeStatus { Charged, NotCharged, Unknown }
public sealed record ChargeStatusResult(ChargeStatus Status, string? ChargeId = null);

public interface IPaymentReconciler
{
    /// Processes every payment attempt whose next retry or status check is due, then returns.
    Task RunOnceAsync(CancellationToken cancellationToken);
}
```

- `ChargeOutcome.Unavailable` means the provider **definitely did not process** the charge (connection refused, HTTP 503). Only this outcome is retried.
- `ChargeOutcome.Unknown` means it **may** have been processed (timeout). It is never re-charged, only status-checked.
- `ChargeRequest.IdempotencyKey` is the **payment attempt's** ID: the same on every retry of one attempt, different for a new attempt (another card).

### Configuration

| Key | Used in tests | Meaning |
|---|---|---|
| `ConnectionStrings:Storefront` | container | PostgreSQL connection |
| `Payments:Reconciler:Enabled` | `false` | Run the background loop |
| `Payments:Retry:MaxChargeAttempts` | `6` | Calls per attempt, first inline call included |
| `Payments:Retry:InitialDelay` | `00:00:02` | Delay before the first retry |
| `Payments:Retry:BackoffMultiplier` | `2` | Exponential backoff factor |
| `Payments:Retry:MaxDelay` | `00:01:00` | Cap on a single delay |

### HTTP

- `POST /api/orders` (header `Idempotency-Key`): body `{ lines: [{ productId, quantity }], paymentToken }`.
- `POST /api/orders/{id}/payments` (header `Idempotency-Key`): body `{ paymentToken }`. Allowed when the order is `PaymentFailed` or `PaymentUnavailable`; otherwise 409.
- Both return the order: **201** + `Location` when the payment outcome is final (`Paid`, `PaymentFailed`), **202** + `Location` while it is `PaymentRetrying` or `PaymentUnknown`.
- Order JSON (camelCase, enums as strings):

```json
{
  "id": "…", "status": "Paid", "charged": true, "failureReason": null,
  "total": { "amountMinor": 4999900, "currency": "GBP" },
  "lines": [{ "productId": "…", "productName": "…", "unitPrice": { "amountMinor": 4999900, "currency": "GBP" }, "quantity": 1 }],
  "payments": [{ "number": 1, "status": "Succeeded", "failureReason": null }]
}
```

- Order statuses used: `Paid`, `PaymentFailed`, `PaymentRetrying`, `PaymentUnavailable`. Attempt statuses: `Succeeded`, `Declined`, `Retrying`, `ProviderUnavailable`. Failure reasons: the provider's decline code, or `payment_provider_unavailable`.
