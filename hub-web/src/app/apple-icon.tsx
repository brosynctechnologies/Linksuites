import { ImageResponse } from "next/og";
import { products } from "@/lib/products";

export const size = { width: 180, height: 180 };
export const contentType = "image/png";

/** The suite mark for home screens: seven stripes in the app colours, on a full square (iOS rounds the corners). */
export default function AppleIcon() {
  return new ImageResponse(
    (
      <div style={{ width: "100%", height: "100%", display: "flex" }}>
        {products.map((p) => (
          <div key={p.key} style={{ flex: 1, background: p.accent }} />
        ))}
      </div>
    ),
    { ...size },
  );
}
