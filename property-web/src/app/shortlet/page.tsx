import type { Metadata } from "next";
import { SearchPage, searchMetadata } from "@/components/search/search-page";

export function generateMetadata(): Promise<Metadata> {
  return searchMetadata("ShortLet", Promise.resolve({}));
}

export default function Page({ searchParams }: PageProps<"/shortlet">) {
  return <SearchPage purpose="ShortLet" params={Promise.resolve({})} searchParams={searchParams} />;
}
