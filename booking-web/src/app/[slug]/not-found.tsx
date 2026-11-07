import Link from "next/link";
import { PublicFrame } from "@/components/public-parts";
import { buttonClass } from "@/components/styles";

/** An unknown booking address: a real 404, in the words of the old client-side message. */
export default function BookingPageNotFound() {
  return (
    <PublicFrame brand={null}>
      <main id="main" tabIndex={-1} className="px-4 py-16 text-center outline-none">
        <h1 className="font-expanded text-2xl font-extrabold">No booking page here</h1>
        <p className="mx-auto mt-2 max-w-sm text-sm text-muted">Check the link you were given. The business may have changed its booking address.</p>
        <Link href="/" className={buttonClass("secondary", "mt-6")}>
          Go to the home page
        </Link>
      </main>
    </PublicFrame>
  );
}
