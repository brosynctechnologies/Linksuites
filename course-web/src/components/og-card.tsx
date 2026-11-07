import { products } from "@/lib/suite";

export const ogSize = { width: 1200, height: 630 };

/**
 * LearnLink's share image, drawn for next/og: the headline, LearnLink on a signboard plate in Lagoon, a short learning
 * path, and the suite mark with "Part of LinkSuite". Every element with children is a flex box, as next/og requires.
 */
export function OgCard({ headline, sub }: { headline: string; sub: string }) {
  const node = (fill: string, ring?: boolean) => (
    <div style={{ display: "flex", alignItems: "center", justifyContent: "center", width: 34, height: 34, borderRadius: 17, background: fill }}>
      {ring && <div style={{ display: "flex", width: 20, height: 20, borderRadius: 10, background: "#f1f2ee" }} />}
    </div>
  );
  const line = (fill: string) => <div style={{ display: "flex", width: 56, height: 6, background: fill }} />;
  return (
    <div style={{ width: "100%", height: "100%", display: "flex", flexDirection: "column", background: "#f1f2ee", padding: "60px 64px 56px" }}>
      <div style={{ display: "flex", alignItems: "center", gap: 14 }}>
        <div style={{ display: "flex", width: 36, height: 36, borderRadius: 8, overflow: "hidden" }}>
          {products.map((p) => (
            <div key={p.key} style={{ flex: 1, background: p.accent }} />
          ))}
        </div>
        <div style={{ display: "flex", fontSize: 28, color: "#3d414a" }}>Part of LinkSuite</div>
      </div>
      <div style={{ display: "flex", marginTop: 44, fontSize: 80, fontWeight: 800, lineHeight: 1.02, letterSpacing: -2, color: "#1a1c20", maxWidth: 960 }}>
        {headline}
      </div>
      <div style={{ display: "flex", marginTop: 22, fontSize: 30, color: "#3d414a", maxWidth: 900 }}>{sub}</div>
      <div style={{ display: "flex", marginTop: "auto", alignItems: "center", justifyContent: "space-between" }}>
        <div style={{ display: "flex", background: "#0b6e6e", borderRadius: 4, padding: 6 }}>
          <div
            style={{
              display: "flex",
              border: "2px solid rgba(255, 255, 255, 0.38)",
              borderRadius: 2,
              padding: "12px 30px",
              color: "#ffffff",
              fontSize: 50,
              fontWeight: 800,
              letterSpacing: -1,
            }}
          >
            LearnLink
          </div>
        </div>
        <div style={{ display: "flex", alignItems: "center" }}>
          {node("#5e9e2f")}
          {line("#5e9e2f")}
          {node("#5e9e2f")}
          {line("#5e9e2f")}
          {node("#0b6e6e", true)}
          {line("#c3cfd1")}
          {node("#c3cfd1", true)}
        </div>
      </div>
    </div>
  );
}
