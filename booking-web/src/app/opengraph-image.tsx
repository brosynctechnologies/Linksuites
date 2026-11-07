import { ImageResponse } from "next/og";
import { products } from "@/lib/suite";

export const alt = "BookLink, part of LinkSuite: online booking with deposits and reminders for salons, barbers and studios in Nigeria.";
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";

/** BookLink's share image: the headline, the app's name on a rose signboard plate, and the suite mark. */
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
          <div style={{ fontSize: 28, fontWeight: 600, color: "#3d414a" }}>Part of LinkSuite</div>
        </div>
        <div style={{ display: "flex", marginTop: 48, fontSize: 80, fontWeight: 800, lineHeight: 1.0, letterSpacing: -2, color: "#1a1c20", maxWidth: 940 }}>
          Let customers book the chair without calling you.
        </div>
        <div style={{ display: "flex", marginTop: 22, fontSize: 30, color: "#3d414a" }}>
          Bookings, deposits and reminders for Nigerian salons and studios. Free to start.
        </div>
        <div style={{ display: "flex", marginTop: "auto" }}>
          <div style={{ display: "flex", padding: 8, borderRadius: 4, background: "#C2255C" }}>
            <div
              style={{
                display: "flex",
                alignItems: "center",
                padding: "14px 40px",
                borderRadius: 2,
                border: "2px solid rgba(255, 255, 255, 0.38)",
                color: "#ffffff",
                fontSize: 52,
                fontWeight: 800,
              }}
            >
              BookLink
            </div>
          </div>
        </div>
      </div>
    ),
    { ...size },
  );
}
