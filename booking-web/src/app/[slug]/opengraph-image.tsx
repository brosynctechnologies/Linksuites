import { ImageResponse } from "next/og";
import { initials, normalizeHex, readableOn } from "@/lib/color";
import { formatMoney } from "@/lib/money";
import { getPublicBookingPage } from "@/lib/server-api";

export const alt = "Book an appointment online with BookLink";
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";

/**
 * Each business's own share image: its name on its brand colour, where it is, its rating and its first services. Uploaded
 * logos and covers are not drawn (they may be WebP, which the image renderer can't read, or slow to fetch); the initials
 * plate stands in for the logo.
 */
export default async function BusinessShareImage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  const res = await getPublicBookingPage(slug);
  const p = res.ok ? res.data : null;
  const brand = normalizeHex(p?.brandColor);
  const ink = readableOn(brand);
  const name = p?.name ?? "Book an appointment";
  const services = (p?.services ?? []).slice(0, 3);

  return new ImageResponse(
    (
      <div style={{ width: "100%", height: "100%", display: "flex", flexDirection: "column", background: "#f1f2ee", padding: "56px 64px" }}>
        <div style={{ display: "flex", alignItems: "center", gap: 28 }}>
          <div
            style={{
              display: "flex", alignItems: "center", justifyContent: "center", width: 120, height: 120, borderRadius: 24,
              background: brand, color: ink, fontSize: 52, fontWeight: 800,
            }}
          >
            {initials(name)}
          </div>
          <div style={{ display: "flex", flexDirection: "column", maxWidth: 900 }}>
            <div style={{ display: "flex", fontSize: 64, fontWeight: 800, letterSpacing: -1.5, lineHeight: 1.05, color: "#1a1c20" }}>
              {name.length > 40 ? `${name.slice(0, 39)}…` : name}
            </div>
            {p?.address && (
              <div style={{ display: "flex", marginTop: 10, fontSize: 28, color: "#3d414a" }}>
                {p.address.length > 60 ? `${p.address.slice(0, 59)}…` : p.address}
              </div>
            )}
          </div>
        </div>

        <div style={{ display: "flex", flexDirection: "column", marginTop: 44, gap: 14 }}>
          {services.map((s) => (
            <div key={s.id} style={{ display: "flex", justifyContent: "space-between", fontSize: 32, color: "#1a1c20", maxWidth: 900 }}>
              <span>{s.name.length > 36 ? `${s.name.slice(0, 35)}…` : s.name}</span>
              {/* "NGN" rather than "₦": the image renderer's built-in font has no naira sign. */}
              <span style={{ color: "#3d414a" }}>{formatMoney(s.priceMinor, p?.currency).replace("₦", "NGN ")}</span>
            </div>
          ))}
        </div>

        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginTop: "auto" }}>
          <div style={{ display: "flex", padding: "16px 36px", borderRadius: 12, background: brand, color: ink, fontSize: 36, fontWeight: 700 }}>
            Book online
          </div>
          <div style={{ display: "flex", fontSize: 28, color: "#3d414a" }}>
            {p?.rating && p.rating.count > 0 ? `Rated ${p.rating.average.toFixed(1)} (${p.rating.count})  ·  BookLink` : "BookLink"}
          </div>
        </div>
      </div>
    ),
    { ...size },
  );
}
