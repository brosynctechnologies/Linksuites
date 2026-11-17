/** Sends a crash to the API's error log (admin console, Errors). Best effort: never throws, never blocks the page. */
const API = (process.env.NEXT_PUBLIC_API_URL || "http://localhost:5080").replace(/\/+$/, "");

export function reportError(error: Error & { digest?: string }) {
  try {
    void fetch(`${API}/api/client-errors`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      keepalive: true,
      body: JSON.stringify({
        app: "school",
        message: `${error.name || "Error"}: ${error.message || "(no message)"}`.slice(0, 1000),
        path: typeof location === "undefined" ? null : location.pathname,
        stack: error.stack?.slice(0, 8000) ?? null,
        digest: error.digest ?? null,
      }),
    }).catch(() => {});
  } catch {
    // reporting must never make things worse
  }
}
