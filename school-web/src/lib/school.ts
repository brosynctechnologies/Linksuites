import type { Stage } from "./types";

export const STAGES: { value: Stage; label: string }[] = [
  { value: "Creche", label: "Creche" },
  { value: "Nursery", label: "Nursery" },
  { value: "Primary", label: "Primary" },
  { value: "Jss", label: "Junior secondary (JSS)" },
  { value: "Sss", label: "Senior secondary (SSS)" },
];

/** Relationships offered for a guardian (free text is accepted too). */
export const RELATIONSHIPS = ["Mother", "Father", "Guardian", "Grandparent", "Aunt", "Uncle", "Sibling", "Other"];
