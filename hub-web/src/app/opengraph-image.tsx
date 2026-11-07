import { ImageResponse } from "next/og";
import { products } from "@/lib/products";

export const alt = "LinkSuite: seven business apps for Nigerian small businesses, from orders on WhatsApp to clinic management.";
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";

/** The hub's share image: the headline over the row of seven signboards, each in its app's colour. */
export default function OpengraphImage() {
  return new ImageResponse(
    (
      <div style={{ width: "100%", height: "100%", display: "flex", flexDirection: "column", background: "#f1f2ee", padding: "64px 64px 56px" }}>
        <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
          <div style={{ display: "flex", width: 44, height: 44, borderRadius: 10, overflow: "hidden" }}>
            {products.map((p) => (
              <div key={p.key} style={{ flex: 1, background: p.accent }} />
            ))}
          </div>
          <div style={{ fontSize: 40, fontWeight: 800, color: "#1a1c20" }}>LinkSuite</div>
        </div>
        <div style={{ display: "flex", marginTop: 52, fontSize: 84, fontWeight: 800, lineHeight: 1.0, letterSpacing: -2, color: "#1a1c20", maxWidth: 900 }}>
          Run your business on one account.
        </div>
        <div style={{ display: "flex", marginTop: 22, fontSize: 30, color: "#3d414a" }}>Seven apps for Nigerian small businesses. Free to start.</div>
        <div style={{ display: "flex", marginTop: "auto", gap: 10 }}>
          {products.map((p) => (
            <div
              key={p.key}
              style={{
                display: "flex",
                flex: 1,
                height: 74,
                alignItems: "center",
                justifyContent: "center",
                borderRadius: 4,
                background: p.accent,
                color: p.onAccent,
                fontSize: 19,
                fontWeight: 700,
              }}
            >
              {p.name}
            </div>
          ))}
        </div>
      </div>
    ),
    { ...size },
  );
}
