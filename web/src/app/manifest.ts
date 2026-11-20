import type { MetadataRoute } from "next";

/**
 * Wave 4 (PWA): the web app manifest, so OrderLink installs to the home screen and opens standalone. start_url is the signed-in home (/dashboard); signed-out visitors are sent to sign in from there.
 * Icons are rendered from app/icon.svg into public/ (any and maskable, 192 and 512).
 */
export default function manifest(): MetadataRoute.Manifest {
  return {
    id: "/dashboard",
    name: "OrderLink",
    short_name: "OrderLink",
    description: "Your store's orders, products and customers.",
    start_url: "/dashboard",
    scope: "/",
    display: "standalone",
    theme_color: "#1b1f3b",
    background_color: "#f6f7fa",
    icons: [
      { src: "/icon-192.png", sizes: "192x192", type: "image/png", purpose: "any" },
      { src: "/icon-512.png", sizes: "512x512", type: "image/png", purpose: "any" },
      { src: "/icon-maskable-192.png", sizes: "192x192", type: "image/png", purpose: "maskable" },
      { src: "/icon-maskable-512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
    ],
  };
}
