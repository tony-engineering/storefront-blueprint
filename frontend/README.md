# Storefront — frontend

Angular (standalone components, signals, zoneless) client for the Storefront API. See `../ARCHITECTURE.md` §11.

## Run

```bash
npm install
npm start            # http://localhost:4200, /api proxied to http://localhost:8080
```

| Command | What |
|---|---|
| `npm start` | Dev server with the `/api` proxy (`proxy.conf.json`) |
| `npm run lint` | ESLint (angular-eslint) |
| `npm test` | Unit tests (Vitest), watch mode |
| `npm run test:ci` | Unit tests, single run |
| `npm run build` | Production build into `dist/storefront/browser` |

In Docker, the `Dockerfile` builds the app and serves it with nginx; `nginx.conf` forwards `/api` to the `api` container so the browser sees a single origin.

## Layout

```
frontend/
├── Dockerfile               # node build → nginx runtime
├── nginx.conf               # serves the SPA, proxies /api to the api container
├── proxy.conf.json          # dev server: /api → http://localhost:8080
└── src/
    ├── ui-kit/              # storefront CSS/JS kit (storefront.css is listed first in angular.json)
    └── app/
        ├── app.*            # shell: header with cart count, footer, router outlet
        ├── app.routes.ts    # lazy-loaded feature routes
        ├── core/
        │   ├── api/         # ApiService: the six API calls, typed
        │   ├── http/        # problem.ts (ProblemDetails → message), idempotency.ts (key per attempt, safe retry)
        │   └── cart/        # signals-based cart (ids + quantities), persisted to localStorage
        ├── shared/
        │   ├── models/      # wire types (hand-written from the API contract), money parsing
        │   ├── pipes/       # money pipe (minor units → £)
        │   └── components/  # order status badge
        └── features/        # one folder per page
            ├── products/    # /              product grid, add to cart
            ├── cart/        # /cart          lines, quantities, totals, hosts checkout
            ├── checkout/    #                summary + "Place order" (idempotent)
            ├── orders/      # /orders, /orders/:id   lookup; status, polling, pay again
            └── admin/products/   # /admin/products   create and delete (not guarded yet)
```

## How the pages handle failure

- **Checkout** makes one `Idempotency-Key` per attempt and reuses it when the same request is retried (network error, 409, 502–504), so a retry can never create a second order or charge. The button is disabled while a request is in flight. Both 201 and 202 open the order page; a 422 (for example a product was deleted meanwhile) shows the server's message and refreshes the cart.
- **Order page** polls every 2 s while the payment is unresolved (`Pending`, `PaymentRetrying`, `PaymentUnknown`) and stops at a final status or when the page is left. It keeps polling through temporary connection errors. For `PaymentFailed` and `PaymentUnavailable` (nothing was charged) it offers "Pay with another card", which uses a new key.
- **Prices** are never sent: the cart stores ids and quantities, and the server computes the total.

## Known gaps

- The admin page has no sign-in, and neither does the API (ARCHITECTURE.md §9).
- API types are written by hand; there is no client code generator (ARCHITECTURE.md §11).

## Docker

From the repo root, the `web` service in `docker-compose.yml` builds this folder and starts it with the API and database:

```bash
docker compose up --build web      # http://localhost:4200; also starts api and db
```

The image serves the production build on port 80, published as 4200. `/api` is proxied to `http://api:8080` on the Compose network. nginx re-resolves `api` through Docker's DNS, so rebuilding the API does not need a `web` restart. To build the image on its own: `docker build -t storefront-web .`
