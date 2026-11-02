import type { Metadata } from "next";
import { SearchPage, searchMetadata } from "@/components/search/search-page";

export function generateMetadata({ params }: PageProps<"/rent/[location]">): Promise<Metadata> {
  return searchMetadata("Rent", params);
}

export default function Page({ params, searchParams }: PageProps<"/rent/[location]">) {
  return <SearchPage purpose="Rent" params={params} searchParams={searchParams} />;
}
