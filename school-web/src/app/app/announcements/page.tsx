"use client";

import { useRef, useState, type FormEvent } from "react";
import { api, upload } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useRole } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { ClassDto, LevelDto } from "@/lib/types";
import type { MyClassesDto } from "@/lib/types/academic";
import type { AnnouncementAudience, AnnouncementDto, PostAnnouncementRequest } from "@/lib/types/communication";
import { AnnouncementIcon } from "@/components/notification-bell";
import { CopyIcon, DownloadIcon, PlusIcon, UploadIcon } from "@/components/icons";
import {
  ApiErrorView,
  Badge,
  Button,
  Checkbox,
  EmptyState,
  ErrorBox,
  Input,
  Modal,
  Notice,
  PageHeader,
  PageLoader,
  RuledSheet,
  Segmented,
  Textarea,
  Toggle,
  WhatsAppLink,
  linkClass,
} from "@/components/ui";

/** AnnouncementService limits. */
const MAX_TITLE = 150;
const MAX_BODY = 5000;
const MAX_FILE_BYTES = 5 * 1024 * 1024;
const FILE_TYPES = "application/pdf,image/jpeg,image/png,image/webp";
const PAGE = 50;
/** The API returns at most 200 at a time. */
const MAX_TAKE = 200;

const AUDIENCES: { value: AnnouncementAudience; label: string }[] = [
  { value: "School", label: "Whole school" },
  { value: "Staff", label: "Staff" },
  { value: "Levels", label: "Levels" },
  { value: "Classes", label: "Classes" },
];

interface Lookups {
  levels: Map<string, string>;
  classes: Map<string, string>;
}

function audienceLabel(a: AnnouncementDto, l: Lookups): string {
  switch (a.audience) {
    case "School":
      return "Whole school";
    case "Staff":
      return "Staff only";
    case "Levels":
      return a.levelIds.map((id) => l.levels.get(id) ?? "a removed level").join(", ");
    case "Classes":
      return a.classIds.map((id) => l.classes.get(id) ?? "a past class").join(", ");
  }
}

export default function AnnouncementsPage() {
  const auth = useAuth();
  const role = useRole();
  const support = !!auth?.support;
  const isLeadership = role === "Owner" || role === "Admin";
  const isTeacher = role === "Staff";
  const [take, setTake] = useState(PAGE);
  const [posting, setPosting] = useState(false);
  const list = useApi<AnnouncementDto[]>(`/api/school/announcements${qs({ take })}`);
  const levels = useApi<LevelDto[]>("/api/school/levels");
  const classes = useApi<ClassDto[]>("/api/school/classes");
  const mine = useApi<MyClassesDto>(isTeacher ? "/api/school/my-classes" : null);
  const del = useAction();

  const lookups: Lookups = {
    levels: new Map((levels.data ?? []).map((l) => [l.id, l.name])),
    classes: new Map((classes.data ?? []).map((c) => [c.id, c.name])),
  };

  // A teacher posts to the classes they are class teacher of or teach a subject in.
  const teacherClasses = (() => {
    if (!mine.data) return [];
    const seen = new Map<string, string>();
    for (const c of mine.data.classTeacherOf) seen.set(c.classId, c.className);
    for (const s of mine.data.subjects) if (!seen.has(s.classId)) seen.set(s.classId, s.className);
    return [...seen].map(([id, name]) => ({ id, name }));
  })();
  const teacherCannotPost = isTeacher && !!mine.data && teacherClasses.length === 0;

  async function remove(a: AnnouncementDto) {
    if (!window.confirm(`Delete "${a.title}"? Parents, students and staff will no longer see it. Emails already sent stay sent.`)) return;
    if ((await del.run(() => api(`/api/school/announcements/${a.id}`, { method: "DELETE" }))).ok) {
      list.setData((d) => (d ?? []).filter((x) => x.id !== a.id));
    }
  }

  const rows = list.data;
  const canPost = !support && !teacherCannotPost && (!isTeacher || !!mine.data);

  return (
    <div className="space-y-4">
      <PageHeader
        title="Announcements"
        subtitle="News for the whole school, the staff, some levels or some classes."
        action={
          !support && (
            <Button onClick={() => setPosting(true)} disabled={!canPost}>
              <PlusIcon className="h-4 w-4" /> Post an announcement
            </Button>
          )
        }
      />
      {teacherCannotPost && (
        <Notice>You don&apos;t teach a class this session yet, so you can&apos;t post an announcement. Ask the principal to assign you a class or subject.</Notice>
      )}
      <ErrorBox error={del.error} />
      {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
      {!rows && !list.error && <PageLoader />}
      {rows && rows.length === 0 && (
        <EmptyState
          icon={<AnnouncementIcon />}
          title="No announcements yet"
          text="Post news, reminders and circulars here. Parents see them in their portal, and you can share them on WhatsApp."
        />
      )}
      {rows && rows.length > 0 && (
        <RuledSheet label="Announcements" busy={list.loading}>
          {rows.map((a) => {
            const canDelete = !support && (isLeadership || a.postedBy === auth?.user.id);
            return (
              <li key={a.id} className="ruled-pad space-y-2 py-4">
                <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-1">
                  <div className="min-w-0">
                    <h2 className="font-bold text-ink">{a.title}</h2>
                    <p className="mt-0.5 text-xs text-muted">
                      <span className="tnum">{formatStamp(a.postedAt)}</span>
                      {a.postedByName && <span> · {a.postedByName}</span>}
                    </p>
                  </div>
                  <div className="flex flex-wrap items-center gap-1.5">
                    <Badge tone={a.audience === "Staff" ? "crest" : "blazer"}>{audienceLabel(a, lookups)}</Badge>
                    {a.emailGuardians && <Badge>Emailed to parents</Badge>}
                  </div>
                </div>
                <p className="text-sm break-words whitespace-pre-line text-slate-800">{a.body}</p>
                <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
                  {a.attachmentUrl && (
                    <a href={a.attachmentUrl} target="_blank" rel="noopener noreferrer" className={`${linkClass} inline-flex min-h-9 items-center gap-1.5 text-sm`}>
                      <DownloadIcon className="h-4 w-4" />
                      {a.attachmentName ?? "Attachment"}
                    </a>
                  )}
                  {canDelete && (
                    <Button variant="ghost" className="-ml-2 px-2 text-sm text-brick" onClick={() => remove(a)} disabled={del.busy}>
                      Delete
                    </Button>
                  )}
                </div>
              </li>
            );
          })}
        </RuledSheet>
      )}
      {rows && rows.length >= take && take < MAX_TAKE && (
        <Button variant="secondary" loading={list.loading} onClick={() => setTake((t) => Math.min(MAX_TAKE, t + PAGE))}>
          Show older announcements
        </Button>
      )}

      {posting && (
        <PostDialog
          teacherClasses={isTeacher ? teacherClasses : null}
          levels={levels.data ?? []}
          classes={classes.data ?? []}
          onClose={() => setPosting(false)}
          onPosted={() => list.reload()}
        />
      )}
    </div>
  );
}

function PostDialog({
  teacherClasses,
  levels,
  classes,
  onClose,
  onPosted,
}: {
  /** Null for leadership and the bursar, who post to any audience; a teacher's own classes otherwise. */
  teacherClasses: { id: string; name: string }[] | null;
  levels: LevelDto[];
  classes: ClassDto[];
  onClose: () => void;
  onPosted: () => void;
}) {
  const isTeacher = teacherClasses !== null;
  const [title, setTitle] = useState("");
  const [body, setBody] = useState("");
  const [audience, setAudience] = useState<AnnouncementAudience>(isTeacher ? "Classes" : "School");
  const [levelIds, setLevelIds] = useState<string[]>([]);
  const [classIds, setClassIds] = useState<string[]>(teacherClasses?.length === 1 ? [teacherClasses[0].id] : []);
  const [emailGuardians, setEmailGuardians] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const [posted, setPosted] = useState<AnnouncementDto | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const post = useAction();
  const attach = useAction();

  const classOptions = teacherClasses ?? classes.map((c) => ({ id: c.id, name: c.name }));

  function toggle(list: string[], id: string, on: boolean) {
    return on ? [...list, id] : list.filter((x) => x !== id);
  }

  function pick(f: File | undefined) {
    setFileError(null);
    if (!f) return;
    if (f.size > MAX_FILE_BYTES) {
      setFileError("Attachments must be 5 MB or smaller. Choose a smaller file.");
      if (fileInput.current) fileInput.current.value = "";
      return;
    }
    setFile(f);
  }

  async function uploadAttachment(a: AnnouncementDto, f: File) {
    const res = await attach.run(() => upload<AnnouncementDto>(`/api/school/announcements/${a.id}/attachment`, f));
    if (res.ok) {
      // Keep the share text from the post response: the upload response leaves it empty.
      setPosted({ ...res.data, whatsAppText: a.whatsAppText });
      onPosted();
    }
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const request: PostAnnouncementRequest = {
      title: title.trim(),
      body: body.trim(),
      audience,
      levelIds: audience === "Levels" ? levelIds : [],
      classIds: audience === "Classes" ? classIds : [],
      emailGuardians: audience !== "Staff" && emailGuardians,
    };
    const res = await post.run(() => api<AnnouncementDto>("/api/school/announcements", { method: "POST", body: request }));
    if (!res.ok) return;
    setPosted(res.data);
    onPosted();
    if (file) await uploadAttachment(res.data, file);
  }

  if (posted) {
    const text = posted.whatsAppText;
    return (
      <Modal open title="Announcement posted" onClose={onClose} footer={<Button onClick={onClose}>Done</Button>}>
        <div className="space-y-4">
          <Notice tone="success">
            {posted.audience === "Staff"
              ? "The staff can see it now."
              : posted.emailGuardians
                ? "Parents and students in the audience can see it now. Emails to parents go out over the next few minutes."
                : "Parents and students in the audience can see it now."}
          </Notice>
          {file && !posted.attachmentUrl && (
            <div className="space-y-2">
              <ErrorBox error={attach.error ?? (attach.busy ? null : "The attachment didn't upload.")} />
              <Button variant="secondary" loading={attach.busy} onClick={() => uploadAttachment(posted, file)}>
                <UploadIcon className="h-4 w-4" /> Try the attachment again
              </Button>
            </div>
          )}
          {text && (
            <div className="space-y-2">
              <Textarea label="Share on WhatsApp" value={text} readOnly rows={6} hint="Send this to your school's WhatsApp groups. The link opens the announcement on SchoolLink." />
              <div className="flex flex-wrap gap-2">
                <WhatsAppLink href={`https://wa.me/?text=${encodeURIComponent(text)}`}>Share on WhatsApp</WhatsAppLink>
                <CopyButton text={text} />
              </div>
            </div>
          )}
        </div>
      </Modal>
    );
  }

  const err = post.error;
  const fieldErr = (name: string) => err?.field(name);
  const knownFields = ["title", "body", "audience", "levelIds", "classIds"];
  const otherError = err && !knownFields.some((f) => err.field(f)) ? err : null;

  return (
    <Modal
      open
      wide
      title="Post an announcement"
      onClose={onClose}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="post-announcement" loading={post.busy || attach.busy}>
            Post
          </Button>
        </>
      }
    >
      <form id="post-announcement" onSubmit={submit} className="space-y-4">
        <Input label="Title" value={title} onChange={(e) => setTitle(e.target.value)} maxLength={MAX_TITLE} required error={fieldErr("title")} />
        <Textarea
          label="Announcement"
          value={body}
          onChange={(e) => setBody(e.target.value)}
          maxLength={MAX_BODY}
          rows={6}
          required
          error={fieldErr("body")}
          hint={body.length > MAX_BODY - 300 ? `${MAX_BODY - body.length} characters left` : undefined}
        />

        <fieldset className="space-y-2">
          <legend className="mb-1.5 block text-sm font-medium text-slate-700">Who is it for?</legend>
          {isTeacher ? (
            <p className="text-sm text-muted">Teachers post to their own classes.</p>
          ) : (
            <Segmented label="Audience" items={AUDIENCES} value={audience} onChange={setAudience} />
          )}
          {audience === "Levels" && (
            <div className="grid gap-2 pt-1 sm:grid-cols-3">
              {levels.length === 0 && <p className="text-sm text-muted">Add levels in Setup first.</p>}
              {levels.map((l) => (
                <Checkbox key={l.id} label={l.name} checked={levelIds.includes(l.id)} onChange={(on) => setLevelIds((x) => toggle(x, l.id, on))} />
              ))}
            </div>
          )}
          {audience === "Classes" && (
            <div className="grid gap-2 pt-1 sm:grid-cols-3">
              {classOptions.length === 0 && <p className="text-sm text-muted">There are no classes this session.</p>}
              {classOptions.map((c) => (
                <Checkbox key={c.id} label={c.name} checked={classIds.includes(c.id)} onChange={(on) => setClassIds((x) => toggle(x, c.id, on))} />
              ))}
            </div>
          )}
          {(fieldErr("audience") || fieldErr("levelIds") || fieldErr("classIds")) && (
            <p className="text-xs font-medium text-brick">{fieldErr("audience") ?? fieldErr("levelIds") ?? fieldErr("classIds")}</p>
          )}
          {audience === "Staff" && <p className="text-xs text-muted">Only staff see it. Parents and students don&apos;t.</p>}
        </fieldset>

        {audience !== "Staff" && (
          <Toggle
            label="Email parents too"
            description="Each parent in the audience with an email address gets a copy."
            checked={emailGuardians}
            onChange={setEmailGuardians}
          />
        )}

        <div className="space-y-1.5">
          <p className="text-sm font-medium text-slate-700">Attachment (optional)</p>
          <input ref={fileInput} id="announcement-file" type="file" accept={FILE_TYPES} className="sr-only" onChange={(e) => pick(e.target.files?.[0])} />
          {file ? (
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <span className="max-w-[18rem] truncate text-ink">{file.name}</span>
              <Button
                variant="ghost"
                className="px-2 text-sm"
                onClick={() => {
                  setFile(null);
                  if (fileInput.current) fileInput.current.value = "";
                }}
              >
                Remove
              </Button>
            </div>
          ) : (
            <label
              htmlFor="announcement-file"
              className="inline-flex min-h-11 cursor-pointer items-center gap-2 rounded-control border border-rule px-4 text-sm font-semibold text-ink hover:bg-slate-50"
            >
              <UploadIcon className="h-4 w-4" /> Choose a file
            </label>
          )}
          {fileError ? (
            <p className="text-xs font-medium text-brick">{fileError}</p>
          ) : (
            <p className="text-xs text-muted">A PDF, or a JPEG, PNG or WebP image, up to 5 MB.</p>
          )}
        </div>

        <ErrorBox error={otherError} />
      </form>
    </Modal>
  );
}

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <Button
      variant="secondary"
      onClick={async () => {
        try {
          await navigator.clipboard.writeText(text);
          setCopied(true);
          setTimeout(() => setCopied(false), 2000);
        } catch {
          setCopied(false);
        }
      }}
    >
      <CopyIcon className="h-4 w-4" /> {copied ? "Copied" : "Copy the text"}
    </Button>
  );
}
