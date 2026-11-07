import { ImageResponse } from "next/og";
import { productByKey, products } from "@/lib/suite";

const app = productByKey("invoices");

export const alt = "InvoiceLink: get paid for every invoice. Invoices, quotes and payment links for Nigerian freelancers and businesses.";
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";

/** InvoiceLink's share image: the headline, then the app's name on a signboard plate in its colour. Part of LinkSuite. */
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
        <div style={{ display: "flex", marginTop: 52, fontSize: 96, fontWeight: 800, lineHeight: 1.0, letterSpacing: -2, color: "#1a1c20", maxWidth: 1000 }}>
          Get paid for every invoice.
        </div>
        <div style={{ display: "flex", marginTop: 24, fontSize: 32, color: "#3d414a", maxWidth: 980 }}>
          Invoices, quotes and payment links for Nigerian freelancers and businesses. Free to start.
        </div>
        <div style={{ display: "flex", marginTop: "auto" }}>
          <div style={{ display: "flex", padding: 8, borderRadius: 6, background: app.accent }}>
            <div
              style={{
                display: "flex",
                alignItems: "center",
                padding: "18px 40px",
                borderRadius: 3,
                border: "2px solid rgba(255,255,255,0.38)",
                color: app.onAccent,
                fontSize: 60,
                fontWeight: 800,
                letterSpacing: -1,
              }}
            >
              {app.name}
            </div>
          </div>
        </div>
      </div>
    ),
    { ...size },
  );
}
