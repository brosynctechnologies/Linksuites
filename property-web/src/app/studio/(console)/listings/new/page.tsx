"use client";

import { PageHeader } from "@/components/ui";
import { ListingWizard } from "../wizard";

export default function NewListingPage() {
  return (
    <div>
      <PageHeader title="New listing" back="/studio/listings" subtitle="Five short steps. Your draft is saved when you reach photos." />
      <ListingWizard listing={null} />
    </div>
  );
}
