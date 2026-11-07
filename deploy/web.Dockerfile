# Shared image for every Next.js app. The build context is the app folder:
#   docker build -f deploy/web.Dockerfile ./invoice-web
# NEXT_PUBLIC_* values are baked into the client bundle at build time, so changing a URL means rebuilding.
FROM node:24-bookworm-slim AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

FROM node:24-bookworm-slim AS build
WORKDIR /app
ARG NEXT_PUBLIC_API_URL
ARG NEXT_PUBLIC_SITE_URL
ARG NEXT_PUBLIC_ADMIN_URL
ARG NEXT_PUBLIC_ORDERLINK_URL
ARG NEXT_PUBLIC_INVOICELINK_URL
ARG NEXT_PUBLIC_BOOKLINK_URL
ARG NEXT_PUBLIC_LEARNLINK_URL
ARG NEXT_PUBLIC_PROPERTYLINK_URL
ARG NEXT_PUBLIC_SCHOOLLINK_URL
ARG NEXT_PUBLIC_CARELINK_URL
ARG NEXT_PUBLIC_HUB_URL
ARG NEXT_PUBLIC_CONTACT_EMAIL
ENV NEXT_TELEMETRY_DISABLED=1
COPY --from=deps /app/node_modules ./node_modules
COPY . .
RUN mkdir -p public && npm run build

FROM node:24-bookworm-slim
WORKDIR /app
ENV NODE_ENV=production NEXT_TELEMETRY_DISABLED=1 HOSTNAME=0.0.0.0 PORT=3000
COPY --from=build --chown=node:node /app/public ./public
COPY --from=build --chown=node:node /app/.next/standalone ./
COPY --from=build --chown=node:node /app/.next/static ./.next/static
USER node
EXPOSE 3000
CMD ["node", "server.js"]
