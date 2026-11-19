import { NextResponse, type NextRequest } from "next/server";
import { API_URL, SITE_URL } from "@/lib/config";

/**
 * Custom domains (Wave 3): a request whose Host is a store's live custom domain is served that store. "/" shows the store,
 * "/p/x", "/cart" and "/checkout" its pages; the app's own links ("/{slug}/...") and order pages ("/o/...") work unchanged.
 * Which store a host serves comes from GET /api/public/domains/{host} (live domains on a plan that includes them), cached here
 * for a minute (30 seconds for "none"), so a lapsed plan or removed domain goes back to the normal store link quickly.
 */

const OWN_HOSTS = new Set(["localhost", "127.0.0.1", "[::1]", hostOf(SITE_URL)].filter(Boolean));
/** App pages that never belong on a store's domain: sent to OrderLink's own address. */
const APP_PATHS = ["/dashboard", "/login", "/signup", "/forgot-password", "/reset-password", "/support"];
const HIT_MS = 60_000;
const MISS_MS = 30_000;
const MAX_ENTRIES = 2_000;

const cache = new Map<string, { slug: string | null; until: number }>();

function hostOf(url: string): string {
  try {
    return new URL(url).hostname.toLowerCase();
  } catch {
    return "";
  }
}

function requestHost(req: NextRequest): string {
  // nginx passes the visitor's Host through (proxy_params); X-Forwarded-Host is not trusted.
  const raw = req.headers.get("host") ?? "";
  return raw.split(",")[0].trim().toLowerCase().replace(/:\d+$/, "").replace(/\.$/, "");
}

async function slugFor(host: string): Promise<string | null> {
  const now = Date.now();
  const hit = cache.get(host);
  if (hit && hit.until > now) return hit.slug;
  let slug: string | null = null;
  let ttl = MISS_MS;
  try {
    const res = await fetch(`${API_URL}/api/public/domains/${encodeURIComponent(host)}`, {
      headers: { Accept: "application/json" },
      signal: AbortSignal.timeout(3_000),
    });
    if (res.ok) {
      const body = (await res.json()) as { slug?: unknown };
      if (typeof body.slug === "string" && /^[a-z0-9-]+$/.test(body.slug)) {
        slug = body.slug;
        ttl = HIT_MS;
      }
    } else if (res.status !== 404) {
      ttl = 5_000; // the API had a problem: ask again soon
    }
  } catch {
    ttl = 5_000;
  }
  if (cache.size >= MAX_ENTRIES) cache.clear();
  cache.set(host, { slug, until: now + ttl });
  return slug;
}

export async function proxy(req: NextRequest) {
  const host = requestHost(req);
  if (!host || OWN_HOSTS.has(host) || host.endsWith(".localhost")) return NextResponse.next();
  const slug = await slugFor(host);
  if (!slug) return NextResponse.next();

  const { pathname, search } = req.nextUrl;
  if (APP_PATHS.some((p) => pathname === p || pathname.startsWith(`${p}/`)))
    return NextResponse.redirect(`${SITE_URL}${pathname}${search}`);
  if (pathname === `/${slug}` || pathname.startsWith(`/${slug}/`) || pathname.startsWith("/o/")) return NextResponse.next();

  const url = req.nextUrl.clone();
  url.pathname = pathname === "/" ? `/${slug}` : `/${slug}${pathname}`;
  return NextResponse.rewrite(url);
}

export const config = {
  // Pages only: not Next's own files, icons, robots, sitemap or other static files.
  matcher: ["/((?!_next/|api/|favicon|icon|apple-icon|opengraph-image|robots\\.txt|sitemap\\.xml|.*\\.[a-z0-9]+$).*)"],
};
