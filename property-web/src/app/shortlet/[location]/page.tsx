import type { Metadata } from "next";
import { SearchPage, searchMetadata } from "@/components/search/search-page";

export function generateMetadata({ params, searchParams }: PageProps<"/shortlet/[location]">): Promise<Metadata> {
  return searchMetadata("ShortLet", params, searchParams);
}

export default function Page({ params, searchParams }: PageProps<"/shortlet/[location]">) {
  return <SearchPage purpose="ShortLet" params={params} searchParams={searchParams} />;
}
