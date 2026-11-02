# LinkSuite (6saas) — VPS deploy handoff

**Status:** **LIVE since 2 Nov 2026** on the Health Tutor VPS. All 10 hosts serve over HTTPS. Paystack, Brevo and R2 are **not configured yet** (see "Still open").  
**Last updated:** 2 Nov 2026  
**Owner:** Brosync Technologies (`brosynctechnologies@gmail.com`)  
**Repo:** local `C:\Users\MichaelAyindePisiMob\source\repos\6saas` → https://github.com/brosynctechnologies/Linksuites (private; branch `main`; laptop remote `git@github-brosync:brosynctechnologies/Linksuites.git`; repo-local git identity Brosync Technologies)

This document is for any Cursor session that continues the LinkSuite deploy. Read it before changing the VPS.

---

## Operations (current state — start here)

| Item | Value |
|------|-------|
| Code on VPS | `/opt/linksuite` (clone of `Linksuites`, pulled with deploy key alias `github-linksuite`) |
| Compose | `docker compose -f docker-compose.vps.yml` (project name `linksuite`), run from `/opt/linksuite` |
| Secrets | `/opt/linksuite/.env` (root, mode 600). Template: `deploy/.env.vps.example`. Postgres password + JWT key were generated on the server; never copy them into git or chat |
| Platform admin email | `brosynctechnologies@gmail.com` (`PLATFORM_ADMIN_EMAIL` in `.env`; overrides the `guysky19@gmail.com` default in `appsettings.json`) |
| nginx | `/etc/nginx/sites-available/linksuite` (certbot-edited copy of `deploy/nginx/linksuite.conf`) + `/etc/nginx/sites-available/00-catch-all` (unknown hosts → 444; self-signed cert in `/etc/nginx/ssl/`) |
| Certificate | Let's Encrypt cert name `linksuite`, all 10 hosts, auto-renew via `certbot.timer` (expires 2027-01-31 before renewal) |
| Health Tutor nginx backup | `/root/smartplay.nginx.bak-<timestamp>` taken before LinkSuite sites were added |
| DB | PostGIS container `linksuite-db-1`, volume `linksuite_pgdata`, `127.0.0.1:55432`, db `saas`, user `postgres` |
| Uploads | Volume `linksuite_uploads` → `/app/uploads` in API (`Storage:Provider=Local` until R2 keys exist) |
| Migrations | Run automatically on API start (`Database__MigrateOnStartup=true`); seeder only upserts plans (idempotent) |
| Data protection keys | Stored in Postgres table `data_protection_keys` (survive rebuilds). Startup warning "No XML encryptor configured" is expected |
| Resource caps | API 2 CPU / 3 GB, DB 1 CPU / 2 GB, each web app 0.5 CPU / 512 MB |

### Verified at go-live (2 Nov 2026)

- 11 containers up; all 10 `https://…linksuite.brosync.ng/` return 200 (API root 302 → `/docs`); `https://api.linksuite.brosync.ng/health` = Healthy; HTTP → HTTPS 301.
- 65 EF migrations applied, 190 tables, plans seeded for all 7 products.
- CORS allows LinkSuite origins only; web bundles contain `https://api.linksuite.brosync.ng` and no `localhost:5080`; hub links point at the product subdomains.
- Health Tutor `https://healthtutor.api.brosync.ng/health` = 200 throughout (≈20 ms even during builds).
- LinkSuite idle usage ≈ 1 GB RAM total.

### Deploy an update

1. Laptop: commit and `git push` in `6saas` (remote `git@github-brosync:brosynctechnologies/Linksuites.git`).
2. VPS (builds one image at a time, then `up -d`):

```bash
ssh healthtutor-vps 'nohup bash /opt/linksuite/deploy/update-vps.sh > /root/linksuite-build.log 2>&1 &'
ssh healthtutor-vps 'tail -n 30 /root/linksuite-build.log'
```

Rebuild only some services: `bash /opt/linksuite/deploy/update-vps.sh api orders`.  
Changing any `*_URL` in `.env` requires rebuilding the web apps (values are baked into bundles); changing secrets only needs `docker compose -f docker-compose.vps.yml up -d api`.

Useful: `docker compose -f docker-compose.vps.yml logs -f api`, `… ps`, `docker stats --no-stream`.

From a Windows laptop, PowerShell mangles nested quotes in `ssh host "…"`. Pipe a script instead: `(Get-Content -Raw script.sh) -replace "`r","" | ssh healthtutor-vps "bash -s"`.

### Still open

| Item | Effect until done |
|------|-------------------|
| `PAYSTACK_SECRET_KEY` | Platform payments (plan upgrades, course sales, short-lets) fail |
| `BREVO_API_KEY` (+ sender domain verified in Brevo) | Emails (verification, password reset, notifications) are only logged by the API, not sent |
| R2 keys + `STORAGE_PROVIDER=R2` | Uploads stay on the VPS disk (volume); course video processing runs FFmpeg on the shared CPU |
| Optional: basic auth / IP allow-list on `admin.linksuite.brosync.ng` | Admin login page is public (protected by app login only) |

To add a secret: edit `/opt/linksuite/.env`, then `cd /opt/linksuite && docker compose -f docker-compose.vps.yml up -d api`.

---

## Decision so far

- LinkSuite **can** share the Health Tutor VPS for launch/early traffic.
- Health Tutor is **live** and must not be broken (billing + SMS).
- **Every app gets its own subdomain** under `linksuite.brosync.ng` (decided 2 Nov 2026; replaces an earlier "products on paths" idea). No Next.js `basePath` is needed.
- LinkSuite needs production Dockerfiles/compose (mostly missing), first git commit + remote, and secrets before install.
- Prefer **CI-built images** and **R2 for media**; cap the API container CPU/RAM so FFmpeg cannot starve Health Tutor.

---

## Shared VPS (already in use by Health Tutor)

| Item | Value |
|------|--------|
| IP | `2.24.52.244` |
| Hostname | `srv2119772` |
| OS | Ubuntu 22.04.5 LTS |
| Specs | 4 vCPU, 15 GB RAM, 194 GB disk (~184 GB free as of Nov 2026) |
| SSH alias (laptop) | `ssh healthtutor-vps` → `root@2.24.52.244` with `~/.ssh/healthtutor-vps` |
| Public ports | **22, 80, 443 only** (UFW + Docker ports bound to 127.0.0.1) |
| nginx | Installed; Health Tutor site at `/etc/nginx/sites-available/smartplay` |
| Certbot | Active; renews via `certbot.timer` |

### Health Tutor on this box (do not disturb)

| Item | Value |
|------|--------|
| Code | `/opt/smartplay` → `git@github.com:brosynctechnologies/smartplay-microservices.git` |
| Public API | https://healthtutor.api.brosync.ng |
| Admin | https://healthtutor.api.brosync.ng/admin/ (basic auth: user `admin`, password in `/root/admin-panel-password.txt`) |
| Compose | `docker compose -f docker-compose.yml -f docker-compose.vps.yml` |
| Env | `/opt/smartplay/.env` (`DB_ROOT_PASSWORD`, `RABBITMQ_*`, `PISI_SECRET`) |
| Stack | MySQL 8, RabbitMQ, .NET 8 microservices (ingress/processor/content/sms/user/ussd/admin) |
| Localhost ports | 3306 (MySQL), 5000 (ingress), 5005 (ussd), 5010 (admin), 15672 (RabbitMQ UI) |

**Rule:** LinkSuite uses **separate** Docker network, **PostgreSQL** (not MySQL), and **localhost-only** app ports. Public traffic only via **new** nginx server blocks + Let’s Encrypt. Never re-run Health Tutor’s `deploy/setup-vps.sh` blindly (it resets UFW).

---

## What LinkSuite is

Multi-product SaaS (“LinkSuite”) from *Six SaaS Product Build Plans*:

| App folder | Product | Local port (dev) |
|------------|---------|------------------|
| `hub-web` | Hub (links to all products) | 3600 |
| `web` | OrderLink (WhatsApp orders) | 3700 |
| `invoice-web` | InvoiceLink | 3800 |
| `booking-web` | BookLink | 3900 |
| `course-web` | LearnLink | 4000 |
| `property-web` | PropertyLink | 4100 |
| `school-web` | SchoolLink | 4200 |
| `admin-web` | Platform admin | 4300 |
| `care-web` | CareLink | 4400 |
| `api` | Shared ASP.NET Core 10 API | 5080 |

- **DB:** PostgreSQL + PostGIS (`docker-compose.yml` uses `postgis/postgis:17-3.5`, host port `55432` for local).
- **API Dockerfile:** `api/Dockerfile` exists (includes FFmpeg).
- **Web apps:** Next.js 16; **no** `output: "standalone"` yet; **no** production Dockerfiles.
- **No** production `docker-compose` for full stack yet.
- Git: initial commit `7a9708c` pushed to `brosynctechnologies/Linksuites` (2 Nov 2026). `.superpowers/` (agent scratch) and all `.env*.local` are git-ignored.

---

## DECIDED (2 Nov 2026): one subdomain per app

DNS is at WhoGoHost (nameservers `nsa.whogohost.com` / `nsb.whogohost.com`), zone `brosync.ng`. Host field is only the left part (e.g. `api.linksuite`); do **not** append `.brosync.ng`. All records are **A → `2.24.52.244`**.

| App | Public URL | WhoGoHost host field | nginx → localhost | DNS status (2 Nov 2026, via 8.8.8.8) |
|-----|------------|----------------------|-------------------|--------------------------------------|
| Hub | `https://linksuite.brosync.ng` | `linksuite` | `127.0.0.1:3600` | Resolves |
| API | `https://api.linksuite.brosync.ng` | `api.linksuite` | `127.0.0.1:5080` | Resolves |
| OrderLink | `https://orders.linksuite.brosync.ng` | `orders.linksuite` | `127.0.0.1:3700` | Resolves |
| InvoiceLink | `https://invoices.linksuite.brosync.ng` | `invoices.linksuite` | `127.0.0.1:3800` | Resolves |
| BookLink | `https://book.linksuite.brosync.ng` | `book.linksuite` | `127.0.0.1:3900` | Resolves |
| LearnLink | `https://learn.linksuite.brosync.ng` | `learn.linksuite` | `127.0.0.1:4000` | Resolves |
| PropertyLink | `https://property.linksuite.brosync.ng` | `property.linksuite` | `127.0.0.1:4100` | Resolves |
| SchoolLink | `https://school.linksuite.brosync.ng` | via wildcard `*.linksuite` | `127.0.0.1:4200` | Resolves |
| CareLink | `https://care.linksuite.brosync.ng` | via wildcard `*.linksuite` | `127.0.0.1:4400` | Resolves |
| Platform admin | `https://admin.linksuite.brosync.ng` | `admin.linksuite` | `127.0.0.1:4300` | Resolves |

Example customer-facing links after deploy: store `https://orders.linksuite.brosync.ng/mama-put`, order `…/o/ABC123`, invoice `https://invoices.linksuite.brosync.ng/i/{token}`, booking page `https://book.linksuite.brosync.ng/{slug}`.

### WhoGoHost record limit (resolved 2 Nov 2026)

WhoGoHost allows only 8 records here. The user added the wildcard `*.linksuite` A → `2.24.52.244`; all 10 names plus any made-up `X.linksuite.brosync.ng` now resolve (verified via 8.8.8.8). Options that were considered, kept for reference:

1. **Wildcard record (preferred):** delete one of the explicit `*.linksuite` records (e.g. `property.linksuite`), then add **one** record: host `*.linksuite`, type A, value `2.24.52.244`. A wildcard answers for every `X.linksuite.brosync.ng` that has no explicit record, so school, care and the deleted one all resolve, and future products need no new records. Verify with `Resolve-DnsName school.linksuite.brosync.ng -Server 8.8.8.8`.
2. If WhoGoHost rejects `*`: ask WhoGoHost support to raise the limit or add the two records.
3. Last resort: move DNS for `brosync.ng` to Cloudflare (free, no practical record limit). Must recreate **every** existing record first, including `healthtutor.api` and the `MX 0 brosync.ng` mail record, or Health Tutor and email break.

Wildcard side effect: any made-up name (e.g. `junk.linksuite.brosync.ng`) reaches the VPS. nginx must have a catch-all `default_server` for unknown hosts that returns `444`, so they never land on Health Tutor or a LinkSuite app (see nginx sketch).

Certificates: still use HTTP-01 with each name listed explicitly in certbot (no wildcard certificate needed).

### Why one subdomain per app is the better layout

- Each app has its own browser origin, so a script bug in one app cannot read another app's login, including admin's. This removes the shared-origin risk discussed earlier.
- No `basePath` and no redirect rewrites in app code; apps run exactly as in local dev.
- The API builds every link from config (`AppUrls` in `api/src/Saas.Application/Common/Abstractions.cs`), so only config changes.
- Moving a product to a separate domain later (e.g. `orderlink.ng`) is DNS + config + rebuild of that app + an nginx redirect from the old host. Users log in again on that product; browser-saved carts/drafts stay on the old host.
- As of 2 Nov 2026 the web apps have no `dangerouslySetInnerHTML`, `innerHTML =`, `eval` or `new Function`, and the API sets no cookies (bearer tokens only).

### Code changes required before deploy (not done yet)

1. Each Next app `next.config.ts`: add `output: "standalone"` (no `basePath`).
2. Dockerfile per web app (or one shared multi-stage Dockerfile with a build arg for the app folder).
3. `docker-compose.vps.yml`: PostGIS, API, nine web apps; ports `127.0.0.1:…` only; API `deploy.resources.limits` (e.g. 2 CPUs, 3G RAM).

### Production config values

API (`/opt/linksuite/.env` → `App__*`, `Cors__*`):

```
App__WebBaseUrl=https://orders.linksuite.brosync.ng
App__ApiBaseUrl=https://api.linksuite.brosync.ng
App__ProductWebUrls__invoices=https://invoices.linksuite.brosync.ng
App__ProductWebUrls__bookings=https://book.linksuite.brosync.ng
App__ProductWebUrls__courses=https://learn.linksuite.brosync.ng
App__ProductWebUrls__property=https://property.linksuite.brosync.ng
App__ProductWebUrls__school=https://school.linksuite.brosync.ng
App__ProductWebUrls__admin=https://admin.linksuite.brosync.ng
App__ProductWebUrls__care=https://care.linksuite.brosync.ng
Cors__Origins__0=https://orders.linksuite.brosync.ng
Cors__Origins__1=https://invoices.linksuite.brosync.ng
Cors__Origins__2=https://book.linksuite.brosync.ng
Cors__Origins__3=https://learn.linksuite.brosync.ng
Cors__Origins__4=https://property.linksuite.brosync.ng
Cors__Origins__5=https://school.linksuite.brosync.ng
Cors__Origins__6=https://admin.linksuite.brosync.ng
Cors__Origins__7=https://care.linksuite.brosync.ng
Cors__Origins__8=https://linksuite.brosync.ng
```

Web apps (build-time; `NEXT_PUBLIC_*` are baked into the bundle):

```
NEXT_PUBLIC_API_URL=https://api.linksuite.brosync.ng
NEXT_PUBLIC_SITE_URL=https://<app>.linksuite.brosync.ng   # per app
# hub only:
NEXT_PUBLIC_ORDERLINK_URL=https://orders.linksuite.brosync.ng
NEXT_PUBLIC_INVOICELINK_URL=https://invoices.linksuite.brosync.ng
NEXT_PUBLIC_BOOKLINK_URL=https://book.linksuite.brosync.ng
NEXT_PUBLIC_LEARNLINK_URL=https://learn.linksuite.brosync.ng
NEXT_PUBLIC_PROPERTYLINK_URL=https://property.linksuite.brosync.ng
NEXT_PUBLIC_SCHOOLLINK_URL=https://school.linksuite.brosync.ng
NEXT_PUBLIC_CARELINK_URL=https://care.linksuite.brosync.ng
NEXT_PUBLIC_ADMIN_URL=https://admin.linksuite.brosync.ng
```

### nginx sketch (`/etc/nginx/sites-available/linksuite`)

```nginx
map $host $linksuite_upstream {
    linksuite.brosync.ng            127.0.0.1:3600;
    orders.linksuite.brosync.ng     127.0.0.1:3700;
    invoices.linksuite.brosync.ng   127.0.0.1:3800;
    book.linksuite.brosync.ng       127.0.0.1:3900;
    learn.linksuite.brosync.ng      127.0.0.1:4000;
    property.linksuite.brosync.ng   127.0.0.1:4100;
    school.linksuite.brosync.ng     127.0.0.1:4200;
    care.linksuite.brosync.ng       127.0.0.1:4400;
}

server {
    server_name linksuite.brosync.ng orders.linksuite.brosync.ng invoices.linksuite.brosync.ng
                book.linksuite.brosync.ng learn.linksuite.brosync.ng property.linksuite.brosync.ng
                school.linksuite.brosync.ng care.linksuite.brosync.ng;
    client_max_body_size 50m;
    location / { proxy_pass http://$linksuite_upstream; include /etc/nginx/proxy_params; }
}
server {
    server_name admin.linksuite.brosync.ng;
    add_header X-Frame-Options DENY always;
    add_header X-Robots-Tag "noindex, nofollow" always;
    location / { proxy_pass http://127.0.0.1:4300; include /etc/nginx/proxy_params; }
}
server {
    server_name api.linksuite.brosync.ng;
    client_max_body_size 500m;   # course video uploads; lower if R2 direct upload is used
    location / { proxy_pass http://127.0.0.1:5080; include /etc/nginx/proxy_params; }
}
```

Catch-all for unknown hosts (needed once the wildcard DNS record exists). Check first that Health Tutor's `smartplay` site is not already `default_server`; only one server per port may be:

```nginx
server { listen 80 default_server; server_name _; return 444; }
# after certbot, also: listen 443 ssl default_server; with any existing cert + return 444;
```

Then: `certbot --nginx --redirect -d linksuite.brosync.ng -d api.linksuite.brosync.ng -d orders.linksuite.brosync.ng -d invoices.linksuite.brosync.ng -d book.linksuite.brosync.ng -d learn.linksuite.brosync.ng -d property.linksuite.brosync.ng -d school.linksuite.brosync.ng -d care.linksuite.brosync.ng -d admin.linksuite.brosync.ng` (all names must resolve first).

Optional hardening: restrict `admin.linksuite.brosync.ng` with nginx basic auth or an IP allow-list (same idea as Health Tutor `/admin/`).

---

## What we need from the user (checklist)

Copy answers into this section when received:

### 1. DNS / hostnames (required)

- [x] URL scheme: one subdomain per app (table above)
- [x] All 10 names resolve to `2.24.52.244` (wildcard `*.linksuite` covers school + care; verified 2 Nov 2026)

### 2. Git remote (required)

- [x] First commit of 6saas completed (`7a9708c`)
- [x] Private GitHub repo: https://github.com/brosynctechnologies/Linksuites
- [x] VPS deploy key generated: `/root/.ssh/linksuite_deploy` (comment `linksuite-vps`); SSH alias `github-linksuite` in `/root/.ssh/config` (Health Tutor keeps `Host github.com` → `smartplay_deploy`)
- [ ] **User:** add the public key below as a **read-only** deploy key on the Linksuites repo (Settings → Deploy keys → Add deploy key, title `linksuite-vps`, leave "Allow write access" unchecked)

```
ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIPHw/qPdmxoJnPFpkS8yehCW7SZTChJyXEQAdE2CfNUZ linksuite-vps
```

VPS clone command once the key is added:

```bash
git clone git@github-linksuite:brosynctechnologies/Linksuites.git /opt/linksuite
```

Test access first: `ssh -T git@github-linksuite` should greet with the repo name.

### 3. Secrets / integrations (required for real traffic)

| Setting | Status | Notes |
|---------|--------|-------|
| `Jwt:SigningKey` | [ ] | 32+ random chars; generate on server if needed |
| Postgres password | [ ] | Separate from Health Tutor MySQL root |
| `Paystack:SecretKey` | [ ] | Platform payments (courses, short-lets, plan upgrades) |
| `Email:BrevoApiKey` + From | [ ] | Or accept console-only email until set |
| `Storage:Provider=R2` + R2 keys | [ ] | **Strongly recommended** before course/property media |
| `Platform:AdminEmails` | [ ] | First super-admin email(s) |
| Product Paystack keys | later | Per-tenant invoice Paystack can wait |

### 4. Product scope for first deploy (optional but useful)

- [ ] Deploy **all** products, or only a subset (list which)?  
  Default if blank: **all** once Docker assets exist.

### 5. Approvals

- [ ] OK to install LinkSuite on **same** VPS as Health Tutor with CPU/RAM caps  
- [ ] OK that FFmpeg/course uploads may briefly raise load (mitigated by caps)

---

## Work already done (agent context)

| Area | Done? | Notes |
|------|-------|-------|
| Assess fit on Health Tutor VPS | Yes | Plenty of RAM/disk; CPU contention from FFmpeg is main risk |
| Document safeguards | Yes | Caps, R2, CI builds, localhost ports, separate PG |
| URL layout | Yes | One subdomain per app |
| DNS records | Yes | All 10 resolve (8 explicit + wildcard) |
| First git commit / GitHub remote | Yes | `brosynctechnologies/Linksuites`, `main` |
| VPS deploy key | Partly | Generated on VPS; user must add it on GitHub |
| Production compose for LinkSuite | **No** | Need `docker-compose.vps.yml` |
| Next.js Dockerfiles + `standalone` | **No** | |
| nginx + SSL for LinkSuite | **No** | After all DNS resolves |
| Seed / migrate on VPS | **No** | After API up |

Health Tutor deploy history lives in  
`C:\Users\MichaelAyindePisiMob\source\repos\smartplay-microservices\docs\deploy-vps.md`  
and chat transcript under smartplay agent-transcripts.

---

## Next steps for the next agent (in order)

1. Confirm the user added the deploy key (`ssh healthtutor-vps "ssh -T git@github-linksuite"`), then clone to `/opt/linksuite` (command above).
2. Add production assets (see "Code changes required before deploy"), commit, push, `git pull` on the VPS.
3. Laptop pushes use remote `origin` = `git@github-brosync:brosynctechnologies/Linksuites.git`.
4. Create `/opt/linksuite/.env` (mode 600) with JWT, Postgres, App URLs, CORS origins, optional Paystack/Brevo/R2.
5. nginx: site from "nginx sketch" + catch-all if wildcard DNS is used; certbot with all ten names.
6. Smoke test: hub + API `/docs` + one product signup; confirm Health Tutor `https://healthtutor.api.brosync.ng/health` still 200.
7. Record credential locations here (never commit secrets).

### Do not

- Publish Docker ports on `0.0.0.0` (bypasses UFW).
- Share MySQL with Health Tutor or reuse Health Tutor `.env`.
- Build all nine Next apps on the VPS in one go without watching RAM.
- Overwrite `/etc/nginx/sites-enabled/smartplay`.
- Move DNS away from WhoGoHost without recreating `healthtutor.api` and MX first.

---

## Localhost port map (VPS)

| Service | Bind |
|---------|------|
| Postgres (PostGIS) | `127.0.0.1:55432` |
| API | `127.0.0.1:5080` |
| Hub | `127.0.0.1:3600` |
| OrderLink | `127.0.0.1:3700` |
| InvoiceLink | `127.0.0.1:3800` |
| BookLink | `127.0.0.1:3900` |
| LearnLink | `127.0.0.1:4000` |
| PropertyLink | `127.0.0.1:4100` |
| SchoolLink | `127.0.0.1:4200` |
| Admin | `127.0.0.1:4300` |
| CareLink | `127.0.0.1:4400` |

These do not conflict with Health Tutor’s 3306 / 5000 / 5005 / 5010 / 15672.

---

## Contact / accounts

| Account | Use |
|---------|-----|
| GitHub `brosynctechnologies` | Private repos; SSH via laptop host `github-brosync` (`~/.ssh/brosync`) |
| DNS | WhoGoHost for `brosync.ng` |
| Let’s Encrypt email used for Health Tutor | `brosynctechnologies@gmail.com` |

---

## One-line summary for the user

Provide: (1) the VPS deploy key added (read-only) on `brosynctechnologies/Linksuites`, (2) JWT/Postgres/Paystack/Brevo/R2/admin emails when ready for production traffic — then an agent can finish Docker + nginx + SSL without asking the same questions again.
