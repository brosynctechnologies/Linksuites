import type { Metadata } from "next";
import { SearchPage, searchMetadata } from "@/components/search/search-page";

export function generateMetadata(): Promise<Metadata> {
  return searchMetadata("Rent", Promise.resolve({}));
}

export default function Page({ searchParams }: PageProps<"/rent">) {
  return <SearchPage purpose="Rent" params={Promise.resolve({})} searchParams={searchParams} />;
}
