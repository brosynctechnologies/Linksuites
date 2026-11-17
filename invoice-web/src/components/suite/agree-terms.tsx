import { hubUrl } from "@/lib/suite";

const link = "font-semibold underline underline-offset-4";

/** The line above every "Create my …" button: signing up means agreeing to the LinkSuite terms and privacy policy. */
export function AgreeTerms() {
  return (
    <p className="text-sm text-muted">
      By creating an account you agree to the{" "}
      <a href={`${hubUrl}/terms`} target="_blank" rel="noopener" className={link}>
        terms of use
      </a>{" "}
      and{" "}
      <a href={`${hubUrl}/privacy`} target="_blank" rel="noopener" className={link}>
        privacy policy
      </a>
      .
    </p>
  );
}
