import type { Metadata } from "next";
import { SearchPage, searchMetadata } from "@/components/search/search-page";

export function generateMetadata({ params, searchParams }: PageProps<"/rent/[location]">): Promise<Metadata> {
  return searchMetadata("Rent", params, searchParams);
}

export default function Page({ params, searchParams }: PageProps<"/rent/[location]">) {
  return <SearchPage purpose="Rent" params={params} searchParams={searchParams} />;
}
