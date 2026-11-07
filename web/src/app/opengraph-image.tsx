import { ImageResponse } from "next/og";
import { products } from "@/lib/suite";

export const alt = "OrderLink: take orders on WhatsApp from your own store link. Part of LinkSuite.";
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";

/** OrderLink's share image: the headline beside the app's signboard plate in marigold, and the suite mark with "Part of LinkSuite". */
export default function OpengraphImage() {
  return new ImageResponse(
    (
      <div style={{ width: "100%", height: "100%", display: "flex", flexDirection: "column", background: "#f1f2ee", padding: "64px 64px 56px" }}>
        <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
          <div style={{ display: "flex", width: 40, height: 40, borderRadius: 9, overflow: "hidden" }}>
            {products.map((p) => (
              <div key={p.key} style={{ flex: 1, background: p.accent }} />
            ))}
          </div>
          <div style={{ fontSize: 30, fontWeight: 700, color: "#3d414a" }}>Part of LinkSuite</div>
        </div>
        <div style={{ display: "flex", marginTop: 52, fontSize: 80, fontWeight: 800, lineHeight: 1.0, letterSpacing: -2, color: "#1a1c20", maxWidth: 980 }}>
          Take orders on WhatsApp from your own store link.
        </div>
        <div style={{ display: "flex", marginTop: 22, fontSize: 30, color: "#3d414a" }}>
          For food vendors, boutiques and small shops in Nigeria. Free to start.
        </div>
        <div style={{ display: "flex", marginTop: "auto" }}>
          <div style={{ display: "flex", padding: 6, borderRadius: 4, background: "#f2a93b" }}>
            <div
              style={{
                display: "flex",
                alignItems: "center",
                height: 84,
                padding: "0 34px",
                borderRadius: 2,
                border: "2px solid rgba(27, 31, 59, 0.38)",
                color: "#1b1f3b",
                fontSize: 52,
                fontWeight: 800,
              }}
            >
              OrderLink
            </div>
          </div>
        </div>
      </div>
    ),
    { ...size },
  );
}
