import { SitePage } from "@/components/site-page";

export default function ShelfLayout({ children }: { children: React.ReactNode }) {
  return (
    <SitePage>
      <div className="mx-auto w-full max-w-4xl px-4 py-7 sm:px-6 sm:py-10">{children}</div>
    </SitePage>
  );
}
