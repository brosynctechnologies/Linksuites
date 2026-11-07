import type { Metadata } from "next";
import { SearchPage, searchMetadata } from "@/components/search/search-page";

export function generateMetadata({ searchParams }: PageProps<"/buy">): Promise<Metadata> {
  return searchMetadata("Sale", Promise.resolve({}), searchParams);
}

export default function Page({ searchParams }: PageProps<"/buy">) {
  return <SearchPage purpose="Sale" params={Promise.resolve({})} searchParams={searchParams} />;
}
