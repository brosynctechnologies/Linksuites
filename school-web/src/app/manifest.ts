import type { MetadataRoute } from "next";

/**
 * Wave 4 (PWA): the web app manifest, so SchoolLink installs to the home screen and opens standalone. start_url is /start, which opens the part of the app this person uses (or sign-in).
 * Icons are rendered from app/icon.svg into public/ (any and maskable, 192 and 512).
 */
export default function manifest(): MetadataRoute.Manifest {
  return {
    id: "/start",
    name: "SchoolLink",
    short_name: "SchoolLink",
    description: "Results, fees, attendance and messages for your school.",
    start_url: "/start",
    scope: "/",
    display: "standalone",
    theme_color: "#40111F",
    background_color: "#edf1f6",
    icons: [
      { src: "/icon-192.png", sizes: "192x192", type: "image/png", purpose: "any" },
      { src: "/icon-512.png", sizes: "512x512", type: "image/png", purpose: "any" },
      { src: "/icon-maskable-192.png", sizes: "192x192", type: "image/png", purpose: "maskable" },
      { src: "/icon-maskable-512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
    ],
  };
}
