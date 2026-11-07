import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { cache } from "react";
import { API_URL } from "@/lib/config";
import type { SchoolPublicDto } from "@/lib/types";
import { SchoolSignIn, SchoolUnavailable } from "./school-sign-in";

/**
 * The school is looked up before anything streams, so an unknown address answers 404 and the title names the school. Allowed to
 * block: there is no static shell worth sending before the school is known.
 */
export const instant = false;

type Lookup = { school: SchoolPublicDto } | { status: number };

/** `GET /api/school/{slug}/public`, once per request for both the metadata and the page. Status 0: the API couldn't be reached. */
const lookup = cache(async (slug: string): Promise<Lookup> => {
  try {
    const res = await fetch(`${API_URL}/api/school/${encodeURIComponent(slug)}/public`, { headers: { Accept: "application/json" } });
    if (!res.ok) return { status: res.status };
    return { school: (await res.json()) as SchoolPublicDto };
  } catch {
    return { status: 0 };
  }
});

export async function generateMetadata({ params }: PageProps<"/[slug]">): Promise<Metadata> {
  const { slug } = await params;
  const found = await lookup(slug);
  if ("status" in found && found.status === 404) notFound();
  const name = "school" in found ? found.school.name : null;
  return {
    title: name ? `Sign in to ${name}` : "Sign in to your school",
    description: name ? `Parents, students and staff of ${name} sign in to SchoolLink here.` : undefined,
    robots: { index: false, follow: false },
    alternates: {},
  };
}

/** The school's own sign-in page (`/{slug}`): parents, students and staff. */
export default async function SchoolSignInPage({ params }: PageProps<"/[slug]">) {
  const { slug } = await params;
  const found = await lookup(slug);
  if ("school" in found) return <SchoolSignIn slug={slug} school={found.school} />;
  if (found.status === 404) notFound();
  if (found.status === 403) return <SchoolUnavailable />;
  // The API couldn't be reached from the server: the page loads the school in the browser instead.
  return <SchoolSignIn slug={slug} />;
}
