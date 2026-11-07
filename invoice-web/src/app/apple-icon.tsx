import { ImageResponse } from "next/og";

export const size = { width: 180, height: 180 };
export const contentType = "image/png";

/** InvoiceLink's mark for home screens: a white invoice sheet on the ink blue, on a full square (iOS rounds the corners). */
export default function AppleIcon() {
  const line = (width: number) => <div style={{ width, height: 8, borderRadius: 4, background: "#2b47c9" }} />;
  return new ImageResponse(
    (
      <div style={{ width: "100%", height: "100%", display: "flex", alignItems: "center", justifyContent: "center", background: "#2b47c9" }}>
        <div
          style={{
            width: 96,
            height: 122,
            display: "flex",
            flexDirection: "column",
            justifyContent: "flex-end",
            gap: 12,
            padding: "0 20px 22px",
            background: "#ffffff",
            borderRadius: 6,
            borderTop: "26px solid #c9d3f7",
          }}
        >
          {line(56)}
          {line(56)}
          {line(32)}
        </div>
      </div>
    ),
    { ...size },
  );
}
