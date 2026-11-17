#!/usr/bin/env node
// Checks that the plan-payment files copied into every product app are still the same (OPay review M10).
// The apps don't share a package, so a fix made in one copy must be made in all of them; this script says when they drift.
//
//   node scripts/check-shared-copies.mjs
//
// Exits 1 and prints a line diff for each copy that differs from the reference copy (property-web). The `web` app (OrderLink) has its
// own UI kit, so its two component copies are compared after the known differences below are applied.

import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const APPS = ["property-web", "booking-web", "care-web", "course-web", "invoice-web", "school-web", "web"];
const REFERENCE = "property-web";
const FILES = ["src/lib/plan-payment.ts", "src/components/plan-payment-notice.tsx", "src/components/plan-transfer.tsx"];

/** Known, intended differences: [app, file] → pairs of [text in that app, the reference text it stands for]. */
const VARIANTS = {
  "web|src/components/plan-payment-notice.tsx": [['<Notice tone="warning">', '<Notice tone="error">']],
  "web|src/components/plan-transfer.tsx": [
    ['import { Button, ErrorBox, Notice, Panel as Box } from "@/components/ui";', 'import { Button, Card as Box, ErrorBox, Notice } from "@/components/ui";'],
  ],
};

const read = (app, file) => readFileSync(join(root, app, file), "utf8").replace(/\r\n/g, "\n");

function normalise(app, file, text) {
  for (const [theirs, ours] of VARIANTS[`${app}|${file}`] ?? []) text = text.split(theirs).join(ours);
  return text;
}

let drift = 0;
for (const file of FILES) {
  const reference = read(REFERENCE, file);
  for (const app of APPS) {
    if (app === REFERENCE) continue;
    let text;
    try {
      text = normalise(app, file, read(app, file));
    } catch {
      console.error(`MISSING ${app}/${file}`);
      drift++;
      continue;
    }
    if (text === reference) continue;
    drift++;
    console.error(`DIFFERS ${app}/${file} (from ${REFERENCE})`);
    const a = reference.split("\n");
    const b = text.split("\n");
    for (let i = 0; i < Math.max(a.length, b.length); i++) {
      if (a[i] === b[i]) continue;
      console.error(`  line ${i + 1}`);
      if (a[i] !== undefined) console.error(`  - ${a[i]}`);
      if (b[i] !== undefined) console.error(`  + ${b[i]}`);
    }
  }
}

if (drift > 0) {
  console.error(`\n${drift} shared cop${drift === 1 ? "y has" : "ies have"} drifted. Make the same change in every app (see README, "Shared copies").`);
  process.exit(1);
}
console.log(`Shared copies match: ${FILES.length} files in ${APPS.length} apps.`);
