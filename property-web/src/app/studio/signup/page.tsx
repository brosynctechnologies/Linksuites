import type { Metadata } from "next";
import Link from "next/link";
import { AgencySignupForm, AuthSwitchLink } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";
import { CheckIcon } from "@/components/icons";
import { linkClass } from "@/components/styles";
import { shareMetadata } from "@/lib/site";

const TITLE = "List your property free";
const DESCRIPTION = "Create a free PropertyLink studio for your agency, your own agent business or your own homes. 3 live listings free, no card needed.";

export const metadata: Metadata = {
  title: TITLE,
  description: DESCRIPTION,
  alternates: { canonical: "/studio/signup" },
  ...shareMetadata({ title: TITLE, description: DESCRIPTION, url: "/studio/signup" }),
};

const POINTS = [
  "Post up to 3 listings free; each one shows the full move-in cost.",
  "Leads arrive on WhatsApp, by phone and in your enquiries inbox.",
  "Seekers book viewings into the times you set each week.",
  "Short-let guests pay online; you're paid after check-in.",
  "Upload your documents once to earn the green Verified seal.",
];

export default function StudioSignupPage() {
  return (
    <AuthShell
      title="List your property"
      subtitle="For agencies, solo agents and landlords. Free to start."
      footer={
        <p>
          Already listing? <AuthSwitchLink href="/studio/login">Log in to the studio</AuthSwitchLink>
        </p>
      }
      aside={
        <div>
          <h2 className="display-wide text-lg font-bold text-asphalt">What you get</h2>
          <ul className="mt-3 space-y-2.5 text-slate-700">
            {POINTS.map((p) => (
              <li key={p} className="flex gap-2.5">
                <CheckIcon className="mt-1 h-4 w-4 shrink-0 text-adire" />
                {p}
              </li>
            ))}
          </ul>
          <Link href="/for-agents#plans" className={`${linkClass} mt-4 inline-flex min-h-11 items-center`}>
            See what the free and paid plans include
          </Link>
        </div>
      }
    >
      <AgencySignupForm />
    </AuthShell>
  );
}
