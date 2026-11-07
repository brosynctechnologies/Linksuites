import type { Metadata } from "next";
import { SearchPage, searchMetadata } from "@/components/search/search-page";

export function generateMetadata({ searchParams }: PageProps<"/shortlet">): Promise<Metadata> {
  return searchMetadata("ShortLet", Promise.resolve({}), searchParams);
}

export default function Page({ searchParams }: PageProps<"/shortlet">) {
  return <SearchPage purpose="ShortLet" params={Promise.resolve({})} searchParams={searchParams} />;
}
