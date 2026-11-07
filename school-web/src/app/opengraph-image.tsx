import { ImageResponse } from "next/og";
import { products } from "@/lib/suite";

export const alt = "SchoolLink: fees, results, attendance and a parent portal for nursery, primary and secondary schools in Nigeria. Part of LinkSuite.";
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";

/** SchoolLink's share image: the headline, SchoolLink on a signboard plate in Blazer, and the suite mark with "Part of LinkSuite". */
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
          <div style={{ fontSize: 30, fontWeight: 600, color: "#3d414a" }}>Part of LinkSuite</div>
        </div>
        <div style={{ display: "flex", marginTop: 52, fontSize: 84, fontWeight: 800, lineHeight: 1.0, letterSpacing: -2, color: "#1a1c20", maxWidth: 960 }}>
          Run your school&apos;s term from one place.
        </div>
        <div style={{ display: "flex", marginTop: 22, fontSize: 30, color: "#3d414a" }}>
          Fees, results, attendance and parents, for Nigerian schools. Free to start.
        </div>
        <div style={{ display: "flex", marginTop: "auto" }}>
          <div style={{ display: "flex", padding: 7, borderRadius: 4, background: "#6b1d33" }}>
            <div
              style={{
                display: "flex",
                alignItems: "center",
                height: 82,
                padding: "0 33px",
                border: "1px solid rgba(255,255,255,0.38)",
                color: "#ffffff",
                fontSize: 46,
                fontWeight: 800,
              }}
            >
              SchoolLink
            </div>
          </div>
        </div>
      </div>
    ),
    { ...size },
  );
}
