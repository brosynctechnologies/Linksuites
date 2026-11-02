import type { Metadata } from "next";
import { AuthSwitchLink } from "@/components/auth-links";
import { TutorSignupForm } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";
import { CheckIcon } from "@/components/icons";
import { MARKETPLACE_FEE_PCT, REFUND_WINDOW_DAYS } from "@/lib/config";

export const metadata: Metadata = { title: "Create your academy" };

const POINTS = [
  "Your own academy page in your colours, to share from your bio",
  `Optional listing on the marketplace (a ${MARKETPLACE_FEE_PCT}% fee applies to marketplace sales)`,
  `Learners pay in naira; earnings clear ${REFUND_WINDOW_DAYS} days after each sale`,
  "Free plan: 2 courses and 2 GB of video. Upgrade when you need more",
];

export default function TutorSignupPage() {
  return (
    <AuthShell
      title="Create your academy"
      subtitle="Free to start. Publish your first course today."
      footer={
        <p>
          Already teaching here? <AuthSwitchLink href="/teach/login">Log in to the studio</AuthSwitchLink>
        </p>
      }
      aside={
        <ul className="space-y-3.5 text-[0.9375rem] text-slate-700">
          {POINTS.map((p) => (
            <li key={p} className="flex gap-3">
              <CheckIcon className="mt-1 h-4 w-4 shrink-0 text-lagoon-text" />
              {p}
            </li>
          ))}
        </ul>
      }
    >
      <TutorSignupForm />
    </AuthShell>
  );
}
