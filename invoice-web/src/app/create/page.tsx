import type { Metadata } from "next";
import { Generator } from "@/components/generator";
import { SiteFooter, SiteHeader } from "@/components/site-header";

export const metadata: Metadata = {
  title: "Free invoice generator",
  description: "Fill in the form, preview your invoice live and download a PDF. No sign-up needed.",
};

export default function CreatePage() {
  return (
    <div className="flex min-h-screen flex-col bg-desk">
      <SiteHeader />
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 pt-8 pb-14 sm:px-6">
        <Generator
          intro={
            <div className="pb-2">
              <h1 className="font-display text-[1.75rem] leading-tight font-[560] tracking-[-0.01em] text-graphite sm:text-[2.5rem]">
                Free invoice generator
              </h1>
              <p className="mt-2 max-w-md text-slate-600">Fill in the details, watch the invoice update, then download your PDF.</p>
            </div>
          }
        />
      </main>
      <SiteFooter />
    </div>
  );
}
