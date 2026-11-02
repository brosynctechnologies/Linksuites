import type { Metadata } from "next";
import { SiteFooter, SiteHeader } from "@/components/site-chrome";
import { DemoForm } from "./demo-form";

export const metadata: Metadata = { title: "Book a demo", description: "See SchoolLink with your own school's setup." };

export default function DemoPage() {
  return (
    <div className="flex min-h-screen flex-col">
      <SiteHeader />
      <main className="mx-auto grid w-full max-w-6xl gap-8 px-4 py-10 sm:px-6 sm:py-14 md:grid-cols-[minmax(0,1fr)_minmax(0,26rem)]">
        <div className="max-w-prose">
          <h1 className="slab text-3xl font-bold text-ink">Book a demo</h1>
          <p className="mt-3 text-slate-700">
            We&apos;ll call you to walk through SchoolLink with your own classes and fee items: importing your students, the register, scores and
            report cards, and how parents pay.
          </p>
          <p className="mt-3 text-slate-700">Tell us roughly how many students you have and the best number to reach you on.</p>
        </div>
        <div className="rounded-panel border border-rule bg-surface p-5 sm:p-6">
          <DemoForm />
        </div>
      </main>
      <SiteFooter />
    </div>
  );
}
