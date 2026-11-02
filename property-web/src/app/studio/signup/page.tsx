import type { Metadata } from "next";
import Link from "next/link";
import { AgencySignupForm, AuthSwitchLink } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";
import { CheckIcon } from "@/components/icons";
import { linkClass } from "@/components/styles";

export const metadata: Metadata = { title: "List your property" };

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
          <Link href="/pricing" className={`${linkClass} mt-4 inline-flex min-h-11 items-center`}>
            See plans and boosts
          </Link>
        </div>
      }
    >
      <AgencySignupForm />
    </AuthShell>
  );
}
