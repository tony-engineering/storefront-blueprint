# Storefront web: builds the Angular app and serves it with nginx.
# Build context is this folder:  docker build -t storefront-web ./frontend
# nginx proxies /api to http://api:8080, so run it on the same network as the API.

# ---- build ----
FROM node:22-alpine AS build
WORKDIR /app

# Dependencies first so this layer is cached until package*.json change.
COPY package.json package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci --no-audit --no-fund

COPY . .
ARG BUILD_CONFIGURATION=production
RUN npx ng build --configuration ${BUILD_CONFIGURATION}

# ---- runtime ----
FROM nginx:1.29-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/storefront/browser /usr/share/nginx/html

EXPOSE 80
HEALTHCHECK --interval=10s --timeout=3s --retries=3 \
  CMD wget -q --spider http://127.0.0.1/ || exit 1
