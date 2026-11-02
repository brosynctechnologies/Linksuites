import type { Metadata } from "next";
import { SearchPage, searchMetadata } from "@/components/search/search-page";

export function generateMetadata(): Promise<Metadata> {
  return searchMetadata("Sale", Promise.resolve({}));
}

export default function Page({ searchParams }: PageProps<"/buy">) {
  return <SearchPage purpose="Sale" params={Promise.resolve({})} searchParams={searchParams} />;
}
