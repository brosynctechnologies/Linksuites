"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { LEVELS } from "@/lib/config";
import { toMinor } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { useApi } from "@/lib/use-api";
import type { CategoryDto, Level, SaveCourseRequest, StudioCourseDto } from "@/lib/types";
import { ApiErrorView, Button, Card, Input, NoAccess, PageHeader, Select, Toggle } from "@/components/ui";

export default function NewCoursePage() {
  const router = useRouter();
  const canCreate = useCan("courses.edit");
  const cats = useApi<CategoryDto[]>("/api/catalog/categories", { auth: false });
  const categories = asList(cats.data);
  const [title, setTitle] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [level, setLevel] = useState<Level>("Beginner");
  const [isFree, setIsFree] = useState(false);
  const [price, setPrice] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  if (!canCreate) {
    return (
      <div>
        <PageHeader title="New course" back="/studio/courses" />
        <NoAccess text="Your role can't create courses." />
      </div>
    );
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const priceMinor = isFree ? 0 : (toMinor(price) ?? 0);
    const body: SaveCourseRequest = {
      title: title.trim(),
      subtitle: null,
      description: null,
      categoryId: categoryId || null,
      level,
      language: "English",
      priceMinor,
      isFree,
      outcomes: [],
      requirements: [],
      listOnMarketplace: false,
    };
    setBusy(true);
    setError(null);
    try {
      const course = await api<StudioCourseDto>("/api/studio/courses", { method: "POST", body });
      router.replace(`/studio/courses/${course.id}`);
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <div className="max-w-xl">
      <PageHeader title="New course" back="/studio/courses" subtitle="The basics now. Everything else is in the course builder." />
      <Card>
        <form onSubmit={submit} className="space-y-4">
          <Input label="Course title" required autoFocus value={title} onChange={(e) => setTitle(e.target.value)} placeholder="e.g. JAMB Physics: score 300+ in 8 weeks" error={error?.field("title")} />
          <Select label="Category" value={categoryId} onChange={(e) => setCategoryId(e.target.value)} hint="Where learners find it on the marketplace.">
            <option value="">Choose later</option>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.parentId ? `  ${c.name}` : c.name}
              </option>
            ))}
          </Select>
          <Select label="Level" value={level} onChange={(e) => setLevel(e.target.value as Level)}>
            {LEVELS.map((l) => (
              <option key={l.value} value={l.value}>
                {l.label}
              </option>
            ))}
          </Select>
          <Toggle checked={isFree} onChange={setIsFree} label="Free course" description="Free courses are a good way to grow your audience." />
          {!isFree && (
            <Input label="Price (₦)" inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} placeholder="15000" hint="You can change it any time." error={error?.field("priceMinor")} />
          )}
          {error && <ApiErrorView error={error} />}
          <Button type="submit" loading={busy} disabled={!title.trim() || (!isFree && !toMinor(price))} className="w-full">
            Create course
          </Button>
        </form>
      </Card>
    </div>
  );
}
