import { ImageResponse } from "next/og";

export const size = { width: 180, height: 180 };
export const contentType = "image/png";

/** PropertyLink's mark for home screens: a house on an Adire signboard, on a full square (iOS rounds the corners). */
export default function AppleIcon() {
  return new ImageResponse(
    (
      <div style={{ width: "100%", height: "100%", display: "flex", alignItems: "center", justifyContent: "center", background: "#33309A" }}>
        <svg viewBox="0 0 32 32" width="132" height="132">
          <path d="M8 15.5 16 8.5l8 7" fill="none" stroke="#FFFFFF" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round" />
          <path d="M10.5 14v9.5h11V14" fill="none" stroke="#FFFFFF" strokeWidth="2.6" strokeLinejoin="round" />
          <rect x="14.2" y="17.5" width="3.6" height="6" fill="#FFFFFF" />
        </svg>
      </div>
    ),
    { ...size },
  );
}
