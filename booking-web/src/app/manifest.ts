import type { MetadataRoute } from "next";

/**
 * Wave 4 (PWA): the web app manifest, so BookLink installs to the home screen and opens standalone. start_url is the signed-in home (/app); signed-out visitors are sent to sign in from there.
 * Icons are rendered from app/icon.svg into public/ (any and maskable, 192 and 512).
 */
export default function manifest(): MetadataRoute.Manifest {
  return {
    id: "/app",
    name: "BookLink",
    short_name: "BookLink",
    description: "Your bookings, calendar and clients.",
    start_url: "/app",
    scope: "/",
    display: "standalone",
    theme_color: "#2B1838",
    background_color: "#f5f3f7",
    icons: [
      { src: "/icon-192.png", sizes: "192x192", type: "image/png", purpose: "any" },
      { src: "/icon-512.png", sizes: "512x512", type: "image/png", purpose: "any" },
      { src: "/icon-maskable-192.png", sizes: "192x192", type: "image/png", purpose: "maskable" },
      { src: "/icon-maskable-512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
    ],
  };
}
