import type { Metadata } from "next";
import { SearchPage, searchMetadata } from "@/components/search/search-page";

export function generateMetadata({ params, searchParams }: PageProps<"/buy/[location]">): Promise<Metadata> {
  return searchMetadata("Sale", params, searchParams);
}

export default function Page({ params, searchParams }: PageProps<"/buy/[location]">) {
  return <SearchPage purpose="Sale" params={params} searchParams={searchParams} />;
}
