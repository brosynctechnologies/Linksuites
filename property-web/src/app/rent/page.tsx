import type { Metadata } from "next";
import { SearchPage, searchMetadata } from "@/components/search/search-page";

export function generateMetadata({ searchParams }: PageProps<"/rent">): Promise<Metadata> {
  return searchMetadata("Rent", Promise.resolve({}), searchParams);
}

export default function Page({ searchParams }: PageProps<"/rent">) {
  return <SearchPage purpose="Rent" params={Promise.resolve({})} searchParams={searchParams} />;
}
