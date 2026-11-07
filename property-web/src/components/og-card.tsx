import { ImageResponse } from "next/og";
import { products } from "@/lib/suite";

export const ogSize = { width: 1200, height: 630 };

/**
 * PropertyLink's share image, after the hub's: the suite mark with "Part of LinkSuite", the page's headline, and the
 * app's name on an Adire signboard plate with its painted frame line.
 */
export function ogCard({ headline, sub }: { headline: string; sub: string }) {
  return new ImageResponse(
    (
      <div style={{ width: "100%", height: "100%", display: "flex", flexDirection: "column", background: "#f1f2ee", padding: "64px 64px 56px" }}>
        <div style={{ display: "flex", alignItems: "center", gap: 14 }}>
          <div style={{ display: "flex", width: 36, height: 36, borderRadius: 8, overflow: "hidden" }}>
            {products.map((p) => (
              <div key={p.key} style={{ flex: 1, background: p.accent }} />
            ))}
          </div>
          <div style={{ fontSize: 28, fontWeight: 600, color: "#3d414a" }}>Part of LinkSuite</div>
        </div>
        <div style={{ display: "flex", marginTop: 48, fontSize: 80, fontWeight: 800, lineHeight: 1.02, letterSpacing: -2, color: "#1a1c20", maxWidth: 960 }}>
          {headline}
        </div>
        <div style={{ display: "flex", marginTop: 22, fontSize: 30, color: "#3d414a", maxWidth: 900 }}>{sub}</div>
        <div style={{ display: "flex", marginTop: "auto" }}>
          <div style={{ display: "flex", padding: 7, borderRadius: 4, background: "#33309a" }}>
            <div
              style={{
                display: "flex",
                alignItems: "center",
                height: 84,
                padding: "0 38px",
                border: "2px solid rgba(255,255,255,0.38)",
                borderRadius: 2,
                color: "#ffffff",
                fontSize: 48,
                fontWeight: 800,
              }}
            >
              PropertyLink
            </div>
          </div>
        </div>
      </div>
    ),
    { ...ogSize },
  );
}
