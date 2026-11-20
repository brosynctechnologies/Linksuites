import type { MetadataRoute } from "next";

/**
 * Wave 4 (PWA): the web app manifest, so CareLink installs to the home screen and opens standalone. start_url is /start, which opens the part of the app this person uses (or sign-in).
 * Icons are rendered from app/icon.svg into public/ (any and maskable, 192 and 512).
 */
export default function manifest(): MetadataRoute.Manifest {
  return {
    id: "/start",
    name: "CareLink",
    short_name: "CareLink",
    description: "Your clinic, or your patient portal.",
    start_url: "/start",
    scope: "/",
    display: "standalone",
    theme_color: "#0A3A36",
    background_color: "#eef3f1",
    icons: [
      { src: "/icon-192.png", sizes: "192x192", type: "image/png", purpose: "any" },
      { src: "/icon-512.png", sizes: "512x512", type: "image/png", purpose: "any" },
      { src: "/icon-maskable-192.png", sizes: "192x192", type: "image/png", purpose: "maskable" },
      { src: "/icon-maskable-512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
    ],
  };
}
