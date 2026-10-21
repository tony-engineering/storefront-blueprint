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
src/app/
├── core/        # app-wide singletons: API client, HTTP interceptor, auth
├── shared/      # reusable pipes and components (e.g. money pipe, status badge)
└── features/    # one folder per page, lazy-loaded from app.routes.ts
    ├── home/
    ├── products/
    ├── cart/
    ├── checkout/
    ├── orders/
    └── admin/
```
