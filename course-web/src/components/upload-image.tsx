import Image from "next/image";
import { API_URL } from "@/lib/config";

/** Files the API stores and serves itself; their host is added to images.remotePatterns in next.config.ts. */
const UPLOADS = `${API_URL}/uploads/`;
/** The optimizer refuses local addresses (Next 16), so a local API (development) keeps plain <img> tags. */
const OPTIMIZE = !/^https?:\/\/(localhost|127\.|\[::1\])/i.test(API_URL);

/**
 * An uploaded image (course thumbnail) filling its (positioned, sized) parent. Photos from the API's own uploads go through
 * next/image, so phones get a small resized copy; any other host (an old R2 URL, a pasted link) stays a plain <img>,
 * because next/image refuses hosts that aren't configured.
 */
export function UploadImage({
  src,
  alt,
  sizes,
  className,
  eager = false,
}: {
  src: string;
  alt: string;
  sizes: string;
  className?: string;
  eager?: boolean;
}) {
  if (OPTIMIZE && src.startsWith(UPLOADS)) {
    return <Image src={src} alt={alt} fill sizes={sizes} className={className} loading={eager ? "eager" : "lazy"} />;
  }
  return <img src={src} alt={alt} loading={eager ? "eager" : "lazy"} className={`absolute inset-0 h-full w-full ${className ?? ""}`} />;
}
