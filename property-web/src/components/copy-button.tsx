"use client";

import { useState } from "react";
import { CheckIcon, CopyIcon } from "./icons";
import { Button } from "./ui";

/** Copies text to the clipboard and says so for two seconds. */
export function CopyButton({ text, label = "Copy", className }: { text: string; label?: string; className?: string }) {
  const [copied, setCopied] = useState(false);
  async function copy() {
    try {
      await navigator.clipboard.writeText(text);
    } catch {
      const ta = document.createElement("textarea");
      ta.value = text;
      document.body.appendChild(ta);
      ta.select();
      document.execCommand("copy");
      ta.remove();
    }
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }
  return (
    <Button variant="secondary" className={className} onClick={copy} aria-live="polite">
      {copied ? <CheckIcon className="h-4 w-4 text-seal" /> : <CopyIcon className="h-4 w-4" />}
      {copied ? "Copied" : label}
    </Button>
  );
}
