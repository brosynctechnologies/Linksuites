"use client";

import { useState, type FormEvent } from "react";
import { api, asList, toApiError, upload, type ApiError } from "@/lib/api";
import { LANGUAGES, LEVELS, MARKETPLACE_FEE_PCT } from "@/lib/config";
import { toMajorInput, toMinor } from "@/lib/money";
import { useApi } from "@/lib/use-api";
import type { CategoryDto, Level, SaveCourseRequest, StudioCourseDto } from "@/lib/types";
import { ImageUpload, ListField, MarkdownField } from "@/components/editors";
import { ApiErrorView, Button, Card, ErrorBox, Input, Notice, SectionTitle, Select, Toggle } from "@/components/ui";

function toForm(c: StudioCourseDto) {
  return {
    title: c.title ?? "",
    subtitle: c.subtitle ?? "",
    description: c.description ?? "",
    categoryId: c.categoryId ?? "",
    level: (c.level ?? "Beginner") as Level,
    language: c.language || "English",
    price: toMajorInput(c.priceMinor),
    isFree: !!c.isFree,
    outcomes: c.outcomes ?? [],
    requirements: c.requirements ?? [],
    listOnMarketplace: !!c.listOnMarketplace,
  };
}

/** Everything about the course except its lessons: the words on the course page, price and listing. */
export function DetailsForm({ course, onSaved, canEdit }: { course: StudioCourseDto; onSaved: (c: StudioCourseDto) => void; canEdit: boolean }) {
  const cats = useApi<CategoryDto[]>("/api/catalog/categories", { auth: false });
  const categories = asList(cats.data);
  const [form, setForm] = useState(() => toForm(course));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [thumbError, setThumbError] = useState<ApiError | null>(null);
  const [saved, setSaved] = useState(false);
  const set = <K extends keyof ReturnType<typeof toForm>>(k: K, v: ReturnType<typeof toForm>[K]) => {
    setForm((f) => ({ ...f, [k]: v }));
    setSaved(false);
  };

  async function submit(e: FormEvent) {
    e.preventDefault();
    const priceMinor = form.isFree ? 0 : toMinor(form.price);
    if (!form.isFree && !priceMinor) {
      setError(toApiError(new Error("Enter a price, or make the course free.")));
      return;
    }
    const body: SaveCourseRequest = {
      title: form.title.trim(),
      subtitle: form.subtitle.trim() || null,
      description: form.description.trim() || null,
      categoryId: form.categoryId || null,
      level: form.level,
      language: form.language,
      priceMinor: priceMinor ?? 0,
      isFree: form.isFree,
      outcomes: form.outcomes.map((x) => x.trim()).filter(Boolean),
      requirements: form.requirements.map((x) => x.trim()).filter(Boolean),
      listOnMarketplace: form.listOnMarketplace,
    };
    setBusy(true);
    setError(null);
    try {
      const res = await api<StudioCourseDto>(`/api/studio/courses/${course.id}`, { method: "PUT", body });
      onSaved(res);
      setForm(toForm(res));
      setSaved(true);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-5">
      <fieldset disabled={!canEdit} className="space-y-5">
        <Card className="space-y-4">
          <SectionTitle>The basics</SectionTitle>
          <Input label="Title" required value={form.title} onChange={(e) => set("title", e.target.value)} error={error?.field("title")} />
          <Input
            label="Subtitle"
            value={form.subtitle}
            onChange={(e) => set("subtitle", e.target.value)}
            placeholder="One sentence: who it's for and what they'll be able to do"
            error={error?.field("subtitle")}
          />
          <div className="grid gap-4 sm:grid-cols-3">
            <Select label="Category" value={form.categoryId} onChange={(e) => set("categoryId", e.target.value)}>
              <option value="">No category</option>
              {categories.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.parentId ? `  ${c.name}` : c.name}
                </option>
              ))}
            </Select>
            <Select label="Level" value={form.level} onChange={(e) => set("level", e.target.value as Level)}>
              {LEVELS.map((l) => (
                <option key={l.value} value={l.value}>
                  {l.label}
                </option>
              ))}
            </Select>
            <Select label="Language" value={form.language} onChange={(e) => set("language", e.target.value)}>
              {[...new Set([form.language, ...LANGUAGES])].map((l) => (
                <option key={l} value={l}>
                  {l}
                </option>
              ))}
            </Select>
          </div>
        </Card>

        <Card className="space-y-4">
          <SectionTitle>Thumbnail</SectionTitle>
          <ImageUpload
            label="Thumbnail"
            src={course.thumbnailUrl}
            hint="16:9, at least 1280 by 720. Kept small for learners on mobile data."
            onUpload={async (file) => {
              setThumbError(null);
              try {
                onSaved(await upload<StudioCourseDto>(`/api/studio/courses/${course.id}/thumbnail`, file));
              } catch (e) {
                setThumbError(toApiError(e));
              }
            }}
          />
          {thumbError && <ErrorBox error={thumbError} />}
        </Card>

        <Card className="space-y-4">
          <SectionTitle>Price</SectionTitle>
          <Toggle checked={form.isFree} onChange={(v) => set("isFree", v)} label="Free course" description="Learners enrol without paying." />
          {!form.isFree && (
            <Input
              label="Price (₦)"
              inputMode="decimal"
              value={form.price}
              onChange={(e) => set("price", e.target.value)}
              hint={`Marketplace sales carry a ${MARKETPLACE_FEE_PCT}% fee; academy sales depend on your plan.`}
              error={error?.field("priceMinor")}
            />
          )}
        </Card>

        <Card className="space-y-5">
          <SectionTitle>Course page</SectionTitle>
          <ListField label="What learners will learn" items={form.outcomes} onChange={(v) => set("outcomes", v)} placeholder="e.g. Solve projectile questions in under a minute" hint="Shown as a checklist near the top of the course page." />
          <ListField label="Requirements" items={form.requirements} onChange={(v) => set("requirements", v)} placeholder="e.g. A calculator" hint="What learners need before they start. Leave empty if none." />
          <MarkdownField label="Description" value={form.description} onChange={(v) => set("description", v)} rows={12} />
        </Card>

        <Card>
          <Toggle
            checked={form.listOnMarketplace}
            onChange={(v) => set("listOnMarketplace", v)}
            label="List on the marketplace"
            description={`Lets you submit the course for review. Approved courses appear in the shared catalogue; marketplace sales carry a ${MARKETPLACE_FEE_PCT}% fee.`}
          />
        </Card>
      </fieldset>

      {error && <ApiErrorView error={error} />}
      {saved && <Notice tone="success">Changes saved.</Notice>}
      {canEdit && (
        <div className="sticky bottom-[calc(4.5rem+env(safe-area-inset-bottom))] z-10 flex justify-end rounded-panel border border-line bg-surface/95 p-3 shadow-bar backdrop-blur lg:bottom-4">
          <Button type="submit" loading={busy} disabled={!form.title.trim()}>
            Save changes
          </Button>
        </div>
      )}
    </form>
  );
}
