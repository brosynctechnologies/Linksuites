/*
 * LinkSuite service worker (Wave 4, PWA). The same file ships in every product's web app.
 *
 * - Install: caches the app shell (the offline page, icons and manifest) in a cache named after the build, so each deploy (a new
 *   ?v= on the registration URL) installs a fresh worker and deletes the old caches.
 * - Pages: always from the network. If the network fails, the offline page is shown. Pages are never cached.
 * - Next's hashed build files (/_next/static/...) are immutable, so they are cached as they are used and served from the cache.
 * - Everything else (the API on its own origin, /api, images, data requests, anything not GET) is left to the browser untouched:
 *   API responses and signed-in data are never cached.
 * - Push: shows the notification the API sent ({ title, body, link, tag }); tapping it opens the link inside this app.
 */
const VERSION = new URL(self.location.href).searchParams.get("v") || "dev";
const CACHE = `linksuite-shell-${VERSION}`;
const OFFLINE_URL = "/offline.html";
const SHELL = [OFFLINE_URL, "/icon-192.png", "/icon-512.png", "/icon-maskable-192.png", "/manifest.webmanifest"];

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches
      .open(CACHE)
      .then((cache) => cache.addAll(SHELL.map((url) => new Request(url, { cache: "reload" }))))
      .then(() => self.skipWaiting()),
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((key) => key.startsWith("linksuite-shell-") && key !== CACHE).map((key) => caches.delete(key))))
      .then(() => self.clients.claim()),
  );
});

self.addEventListener("fetch", (event) => {
  const request = event.request;
  if (request.method !== "GET") return;
  const url = new URL(request.url);
  if (url.origin !== self.location.origin || url.pathname.startsWith("/api/")) return;

  if (request.mode === "navigate") {
    event.respondWith(
      fetch(request).catch(() => caches.match(OFFLINE_URL, { ignoreSearch: true }).then((page) => page || Response.error())),
    );
    return;
  }

  if (url.pathname.startsWith("/_next/static/") && !request.headers.has("range")) {
    event.respondWith(
      caches.open(CACHE).then((cache) =>
        cache.match(request).then(
          (hit) =>
            hit ||
            fetch(request).then((response) => {
              if (response.ok && response.type === "basic") cache.put(request, response.clone());
              return response;
            }),
        ),
      ),
    );
  }
});

/** Only a path inside this app is opened; anything else opens the app's home. */
function safePath(link) {
  return typeof link === "string" && link.startsWith("/") && !link.startsWith("//") && !link.startsWith("/\\") ? link : "/";
}

self.addEventListener("push", (event) => {
  let data = {};
  try {
    data = event.data ? event.data.json() : {};
  } catch {
    data = { body: event.data ? event.data.text() : "" };
  }
  const title = typeof data.title === "string" && data.title ? data.title : "New notification";
  event.waitUntil(
    self.registration.showNotification(title, {
      body: typeof data.body === "string" ? data.body : "",
      icon: "/icon-192.png",
      badge: "/icon-maskable-192.png",
      tag: typeof data.tag === "string" ? data.tag : undefined,
      data: { link: safePath(data.link) },
    }),
  );
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const target = new URL(safePath(event.notification.data && event.notification.data.link), self.location.origin).href;
  event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((windows) => {
      for (const client of windows) {
        if (new URL(client.url).origin === self.location.origin && "focus" in client) {
          return client.focus().then((focused) => (focused && "navigate" in focused ? focused.navigate(target) : undefined));
        }
      }
      return self.clients.openWindow(target);
    }),
  );
});
