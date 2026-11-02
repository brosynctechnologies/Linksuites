"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { MARKETPLACE_FEE_PCT } from "@/lib/config";
import { formatDay } from "@/lib/format";
import { useCan } from "@/lib/permissions";
import { COURSE_STATUS, MARKETPLACE_STATUS, statusOf } from "@/lib/status";
import { useApi } from "@/lib/use-api";
import type { StudioCourseDto } from "@/lib/types";
import { CopyButton } from "@/components/copy-button";
import { ExternalIcon } from "@/components/icons";
import { ApiErrorView, Button, Card, Notice, PageHeader, PageLoader, SectionTitle, StatusBadge, Tabs, buttonClass } from "@/components/ui";
import { Curriculum } from "./curriculum";
import { DetailsForm } from "./details-form";

type Tab = "curriculum" | "details" | "publishing";

export default function CourseBuilderPage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const canEdit = useCan("courses.edit");
  const { data: course, error, reload, setData } = useApi<StudioCourseDto>(`/api/studio/courses/${id}`);
  const [tab, setTab] = useState<Tab>("curriculum");
  const [busy, setBusy] = useState<string | null>(null);
  const [actionError, setActionError] = useState<ApiError | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  if (!course) {
    return (
      <div>
        <PageHeader title="Course" back="/studio/courses" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader />}
      </div>
    );
  }

  async function act(key: string, fn: () => Promise<StudioCourseDto | void>, done?: string) {
    setBusy(key);
    setActionError(null);
    setNotice(null);
    try {
      const res = await fn();
      if (res) setData(res);
      if (done) setNotice(done);
    } catch (e) {
      setActionError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  const st = statusOf(COURSE_STATUS, course.status);
  const mk = statusOf(MARKETPLACE_STATUS, course.marketplaceStatus);
  const lessons = course.sections.reduce((n, s) => n + s.lessons.length, 0);
  const published = course.status === "Published";
  const canSubmit = course.listOnMarketplace && (course.marketplaceStatus === "NotListed" || course.marketplaceStatus === "Rejected");

  const publishButton = canEdit &&
    (published ? (
      <Button variant="secondary" loading={busy === "unpublish"} onClick={() => act("unpublish", () => api(`/api/studio/courses/${course.id}/unpublish`, { method: "POST" }), "Unpublished. Enrolled learners keep access; no new enrolments.")}>
        Unpublish
      </Button>
    ) : (
      <Button
        loading={busy === "publish"}
        disabled={lessons === 0}
        title={lessons === 0 ? "Add at least one lesson first" : undefined}
        onClick={() => act("publish", () => api(`/api/studio/courses/${course.id}/publish`, { method: "POST" }), "Published. Your course is live on your academy page.")}
      >
        Publish
      </Button>
    ));

  return (
    <div className="space-y-5">
      <PageHeader
        title={<span className="font-serif">{course.title}</span>}
        back="/studio/courses"
        subtitle={
          <span className="mt-1 flex flex-wrap items-center gap-1.5">
            <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
            <StatusBadge tone={mk.tone}>{mk.label}</StatusBadge>
            <span className="tnum text-xs text-muted">{lessons} lessons</span>
          </span>
        }
        action={
          <>
            {course.courseUrl && published && (
              <a href={course.courseUrl} target="_blank" rel="noopener noreferrer" className={buttonClass("ghost")}>
                <ExternalIcon className="h-4 w-4" /> View
              </a>
            )}
            {publishButton}
          </>
        }
      />

      {actionError && <ApiErrorView error={actionError} />}
      {notice && <Notice tone="success">{notice}</Notice>}
      {course.marketplaceStatus === "Rejected" && course.marketplaceNote && (
        <Notice tone="error">
          <strong>Not approved for the marketplace:</strong> {course.marketplaceNote}
        </Notice>
      )}

      <Tabs<Tab>
        label="Course builder"
        value={tab}
        onChange={setTab}
        items={[
          { value: "curriculum", label: "Curriculum" },
          { value: "details", label: "Details" },
          { value: "publishing", label: "Publishing" },
        ]}
      />

      {tab === "curriculum" && <Curriculum course={course} onChange={setData} canEdit={canEdit} />}
      {tab === "details" && <DetailsForm key={course.id} course={course} onSaved={setData} canEdit={canEdit} />}
      {tab === "publishing" && (
        <div className="space-y-4">
          <Card>
            <SectionTitle>Your academy</SectionTitle>
            <p className="text-sm text-slate-700">
              {published
                ? `Live since ${formatDay(course.publishedAt)}. Anyone with the link can enrol.`
                : lessons === 0
                  ? "Add at least one lesson, then publish to put the course on your academy page."
                  : "Draft. Publish to put the course on your academy page."}
            </p>
            {course.courseUrl && (
              <div className="mt-3 flex flex-wrap items-center gap-2">
                <code className="min-w-0 flex-1 rounded-control bg-mist px-3 py-2 text-sm break-all text-ink">{course.courseUrl}</code>
                <CopyButton text={course.courseUrl} label="Copy link" />
              </div>
            )}
            <div className="mt-4">{publishButton}</div>
          </Card>

          <Card>
            <SectionTitle>The marketplace</SectionTitle>
            <p className="flex flex-wrap items-center gap-2 text-sm text-slate-700">
              Status: <StatusBadge tone={mk.tone}>{mk.label}</StatusBadge>
            </p>
            <p className="mt-2 text-sm text-slate-700">
              {course.marketplaceStatus === "InReview"
                ? "Our team is reviewing the course. You'll see the result here."
                : course.marketplaceStatus === "Approved"
                  ? "Approved: the course is in the shared catalogue."
                  : course.marketplaceStatus === "Rejected"
                    ? "Fix what the note asks for, then submit again."
                    : `Submit the course for review to list it in the shared catalogue. Marketplace sales carry a ${MARKETPLACE_FEE_PCT}% fee.`}
            </p>
            {course.marketplaceNote && course.marketplaceStatus === "Rejected" && <p className="mt-2 border-l-2 border-brick pl-3 text-sm text-brick-dark">{course.marketplaceNote}</p>}
            {!course.listOnMarketplace && course.marketplaceStatus !== "Approved" && (
              <p className="mt-3 text-sm text-muted">
                Turn on “List on the marketplace” in{" "}
                <button type="button" className="font-semibold text-lagoon-text hover:underline" onClick={() => setTab("details")}>
                  Details
                </button>{" "}
                first.
              </p>
            )}
            {canEdit && canSubmit && (
              <Button
                className="mt-4"
                loading={busy === "submit"}
                disabled={!published}
                title={!published ? "Publish the course first" : undefined}
                onClick={() => act("submit", () => api(`/api/studio/courses/${course.id}/submit-to-marketplace`, { method: "POST" }), "Submitted for review.")}
              >
                Submit to marketplace
              </Button>
            )}
            {canEdit && canSubmit && !published && <p className="mt-2 text-xs text-muted">Publish the course before submitting it.</p>}
          </Card>

          {canEdit && (
            <Card>
              <SectionTitle>Delete course</SectionTitle>
              <p className="text-sm text-slate-700">Only courses nobody has enrolled in can be deleted. Unpublish a course with learners instead.</p>
              <Button
                variant="danger"
                className="mt-3"
                loading={busy === "delete"}
                onClick={() => {
                  if (!window.confirm(`Delete "${course.title}" with all its lessons and videos? This can't be undone.`)) return;
                  void act("delete", async () => {
                    await api(`/api/studio/courses/${course.id}`, { method: "DELETE" });
                    router.replace("/studio/courses");
                  });
                }}
              >
                Delete course
              </Button>
            </Card>
          )}

          <p className="text-sm text-muted">
            <Link href="/studio/courses" className="font-semibold text-lagoon-text hover:underline">
              Back to all courses
            </Link>
          </p>
        </div>
      )}
    </div>
  );
}
