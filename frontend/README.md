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
    ├── ui-kit/              # storefront CSS/JS kit (moved here from the repo root later)
    └── app/
        ├── app.config.ts    # providers: router, HttpClient
        ├── app.routes.ts    # lazy-loaded feature routes
        ├── core/            # app-wide singletons
        │   ├── api/         # typed API client
        │   │   └── generated/   # generated from the API's OpenAPI spec, never edited by hand
        │   ├── auth/        # JWT token storage, admin role guard
        │   ├── http/        # interceptors: attach JWT, map ProblemDetails to messages
        │   └── cart/        # signals-based cart service, persisted to localStorage
        ├── shared/          # reusable, stateless building blocks
        │   ├── components/  # e.g. order status badge
        │   ├── pipes/       # e.g. money pipe (minor units → £)
        │   └── models/      # Money, OrderStatus and other client-side types
        └── features/        # one folder per page, lazy-loaded
            ├── home/        # hello world placeholder
            ├── products/    # product list
            ├── cart/        # cart
            ├── checkout/    # checkout + idempotency key + polling
            ├── orders/      # order details / lookup
            └── admin/
                └── products/    # create / delete products (admin only)
```

Empty folders hold a `.gitkeep` until their first file lands.

## Docker

From the repo root, the `web` service in `docker-compose.yml` builds this folder and starts it with the API and database:

```bash
docker compose up --build web      # http://localhost:4200; also starts api and db
```

The image serves the production build on port 80, published as 4200. `/api` is proxied to `http://api:8080` on the Compose network. nginx re-resolves `api` through Docker's DNS, so rebuilding the API does not need a `web` restart. To build the image on its own: `docker build -t storefront-web .`
