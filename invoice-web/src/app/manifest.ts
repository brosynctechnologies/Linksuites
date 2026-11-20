import type { MetadataRoute } from "next";

/**
 * Wave 4 (PWA): the web app manifest, so InvoiceLink installs to the home screen and opens standalone. start_url is the signed-in home (/app); signed-out visitors are sent to sign in from there.
 * Icons are rendered from app/icon.svg into public/ (any and maskable, 192 and 512).
 */
export default function manifest(): MetadataRoute.Manifest {
  return {
    id: "/app",
    name: "InvoiceLink",
    short_name: "InvoiceLink",
    description: "Invoices, quotes, receipts and payments.",
    start_url: "/app",
    scope: "/",
    display: "standalone",
    theme_color: "#1e2430",
    background_color: "#edf0f4",
    icons: [
      { src: "/icon-192.png", sizes: "192x192", type: "image/png", purpose: "any" },
      { src: "/icon-512.png", sizes: "512x512", type: "image/png", purpose: "any" },
      { src: "/icon-maskable-192.png", sizes: "192x192", type: "image/png", purpose: "maskable" },
      { src: "/icon-maskable-512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
    ],
  };
}
