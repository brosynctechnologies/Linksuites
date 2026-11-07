import { ImageResponse } from "next/og";
import { products } from "@/lib/suite";

export const alt = "CareLink: clinic software for private clinics and small hospitals in Nigeria, from the front desk to the ward. Part of LinkSuite.";
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";

/** CareLink's share image: the headline, CareLink on a signboard plate in Scrub with a wristband, and the suite mark. */
export default function OpengraphImage() {
  return new ImageResponse(
    (
      <div style={{ width: "100%", height: "100%", display: "flex", flexDirection: "column", background: "#f1f2ee", padding: "64px 64px 56px" }}>
        <div style={{ display: "flex", alignItems: "center", gap: 14 }}>
          <div style={{ display: "flex", width: 36, height: 36, borderRadius: 8, overflow: "hidden" }}>
            {products.map((p) => (
              <div key={p.key} style={{ flex: 1, background: p.accent }} />
            ))}
          </div>
          <div style={{ fontSize: 30, fontWeight: 700, color: "#3d414a" }}>Part of LinkSuite</div>
        </div>
        <div style={{ display: "flex", marginTop: 48, fontSize: 84, fontWeight: 800, lineHeight: 1.0, letterSpacing: -2, color: "#1a1c20", maxWidth: 900 }}>
          Your clinic, from front desk to ward.
        </div>
        <div style={{ display: "flex", marginTop: 22, fontSize: 30, color: "#3d414a" }}>
          Patients, wards and bills for Nigerian clinics. Free to start.
        </div>
        <div style={{ display: "flex", marginTop: "auto", height: 104, padding: 5, borderRadius: 4, background: "#0f766e", color: "#ffffff" }}>
          <div
            style={{
              display: "flex",
              flex: 1,
              alignItems: "center",
              justifyContent: "space-between",
              padding: "0 34px",
              borderRadius: 2,
              border: "1px solid rgba(255,255,255,0.38)",
            }}
          >
            <div style={{ fontSize: 52, fontWeight: 800 }}>CareLink</div>
            <div style={{ display: "flex", alignItems: "center", gap: 12, height: 52, padding: "0 22px 0 10px", borderRadius: 26, background: "#e3f1ee" }}>
              <div style={{ width: 26, height: 26, borderRadius: 13, background: "#ffffff", border: "2px solid #a7d3cb" }} />
              <div style={{ fontSize: 28, fontWeight: 700, color: "#0b5f58" }}>CL-000123</div>
            </div>
          </div>
        </div>
      </div>
    ),
    { ...size },
  );
}
