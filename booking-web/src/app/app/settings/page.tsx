"use client";

import { useRef, useState, type FormEvent } from "react";
import { api, toApiError, upload, ApiError } from "@/lib/api";
import { patchAuth, useAuth } from "@/lib/auth";
import { normalizeHex } from "@/lib/color";
import { API_URL, CATEGORIES, DEFAULT_BRAND, SITE_URL, TIMEZONES } from "@/lib/config";
import { useCan } from "@/lib/permissions";
import { WEEK_ORDER, WEEKDAYS, cityOf, clockToMinutes, formatDuration } from "@/lib/time";
import type { BookingBusinessDto, BusinessCategory, OpeningHours, UpdateBookingBusinessRequest, Weekday } from "@/lib/types";
import { useApi } from "@/lib/use-api";
import { depositsOffReason, useBusiness, useServices } from "@/lib/use-business";
import { AccountActivity } from "@/components/account-activity";
import { ChangePassword } from "@/components/change-password";
import { CopyButton } from "@/components/copy-button";
import {
  ApiErrorView,
  Button,
  Card,
  ErrorBox,
  Input,
  Modal,
  NoAccess,
  Notice,
  PageHeader,
  PageLoader,
  SectionTitle,
  Select,
  StatusBadge,
  Textarea,
  Toggle,
  buttonClass,
  inputClass,
  cx,
} from "@/components/ui";
import { ImageIcon } from "@/components/icons";

export default function SettingsPage() {
  const canView = useCan("settings.view");
  const canEdit = useCan("settings.edit");
  const { business, error, reload, setData } = useBusiness();
  const hours = useApi<OpeningHours[]>(canView ? "/api/booking-business/hours" : null);

  if (!canView) {
    return (
      <div>
        <PageHeader title="Settings" />
        <NoAccess />
        <div className="mt-6 max-w-3xl space-y-6">
          <ChangePassword />
          <AccountActivity tz={business?.timezone} />
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-3xl space-y-6">
      <PageHeader title="Settings" subtitle="Your business details, opening hours and how customers book you." />
      {!canEdit && <Notice>You can view these settings. Only the owner or an admin can change them.</Notice>}
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!business && !error && <PageLoader />}
      {business && (
        <>
          <ShareCard business={business} />
          <fieldset disabled={!canEdit} className="min-w-0 space-y-6">
            <legend className="sr-only">Business settings</legend>
            <BusinessSettings key="business" business={business} onSaved={setData} readOnly={!canEdit} />
            {hours.error && <ApiErrorView error={hours.error} onRetry={hours.reload} />}
            {!hours.data && !hours.error && (
              <Card>
                <SectionTitle>Opening hours</SectionTitle>
                <PageLoader label="Loading opening hours…" />
              </Card>
            )}
            {hours.data && <HoursEditor key="hours" initial={hours.data} tz={business.timezone} readOnly={!canEdit} />}
            <PaystackCard business={business} onChange={setData} readOnly={!canEdit} />
          </fieldset>
        </>
      )}
      <ChangePassword />
      <AccountActivity tz={business?.timezone} />
    </div>
  );
}

// ---------- Business profile, booking rules and policies (one PUT body) ----------

type Section = "profile" | "rules" | "policies";

const SECTION_NAME: Record<Section, string> = {
  profile: "Business profile",
  rules: "Booking rules",
  policies: "Policies",
};

const SAVED_TEXT: Record<Section, string> = {
  profile: "Profile saved.",
  rules: "Booking rules saved.",
  policies: "Policies saved.",
};

interface Form {
  name: string;
  slug: string;
  category: BusinessCategory;
  description: string;
  address: string;
  phone: string;
  whatsappNumber: string;
  email: string;
  timezone: string;
  brandColor: string;
  policies: string;
  cancellationHours: string;
  slotIntervalMinutes: number;
  minNoticeMinutes: number;
  maxDaysAhead: string;
  bookingEnabled: boolean;
}

function toForm(b: BookingBusinessDto): Form {
  return {
    name: b.name ?? "",
    slug: b.slug ?? "",
    category: b.category || "Salon",
    description: b.description ?? "",
    address: b.address ?? "",
    phone: b.phone ?? "",
    whatsappNumber: b.whatsappNumber ?? "",
    email: b.email ?? "",
    timezone: b.timezone || "Africa/Lagos",
    brandColor: normalizeHex(b.brandColor, DEFAULT_BRAND),
    policies: b.policies ?? "",
    cancellationHours: String(b.cancellationHours ?? 24),
    slotIntervalMinutes: b.slotIntervalMinutes || 15,
    minNoticeMinutes: b.minNoticeMinutes ?? 60,
    maxDaysAhead: String(b.maxDaysAhead ?? 60),
    bookingEnabled: !!b.bookingEnabled,
  };
}

const orNull = (s: string) => (s.trim() ? s.trim() : null);
const SLUG_RE = /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/;
const HEX_RE = /^#[0-9a-f]{6}$/i;

/** Validate the whole form and build the request, or say which section needs fixing. */
function toRequest(f: Form): { body: UpdateBookingBusinessRequest } | { section: Section; message: string } {
  if (!f.name.trim()) return { section: "profile", message: "Enter your business name." };
  const slug = f.slug.trim();
  if (slug.length < 3 || !SLUG_RE.test(slug))
    return { section: "profile", message: "The link name needs at least 3 characters: lowercase letters, numbers and hyphens." };
  if (!HEX_RE.test(f.brandColor.trim())) return { section: "profile", message: "Enter the brand colour as a hex code, like #C2255C." };
  if (f.email.trim() && !/^\S+@\S+\.\S+$/.test(f.email.trim())) return { section: "profile", message: "Check the email address." };
  const maxDays = Number(f.maxDaysAhead);
  if (!Number.isInteger(maxDays) || maxDays < 1 || maxDays > 365)
    return { section: "rules", message: "How far ahead customers can book must be between 1 and 365 days." };
  const cancel = Number(f.cancellationHours);
  if (f.cancellationHours.trim() === "" || !Number.isInteger(cancel) || cancel < 0 || cancel > 168)
    return { section: "rules", message: "The cancellation window must be between 0 and 168 hours." };
  return {
    body: {
      name: f.name.trim(),
      slug,
      category: f.category,
      description: orNull(f.description),
      address: orNull(f.address),
      phone: orNull(f.phone),
      whatsappNumber: orNull(f.whatsappNumber),
      email: orNull(f.email),
      timezone: f.timezone,
      brandColor: normalizeHex(f.brandColor.trim()),
      policies: orNull(f.policies),
      cancellationHours: cancel,
      slotIntervalMinutes: f.slotIntervalMinutes,
      minNoticeMinutes: f.minNoticeMinutes,
      maxDaysAhead: maxDays,
      bookingEnabled: f.bookingEnabled,
    },
  };
}

const SLOT_INTERVALS = [5, 10, 15, 20, 30, 45, 60];
const NOTICE_PRESETS: { value: number; label: string }[] = [
  { value: 0, label: "No notice, right up to the start" },
  { value: 30, label: "30 minutes" },
  { value: 60, label: "1 hour" },
  { value: 120, label: "2 hours" },
  { value: 240, label: "4 hours" },
  { value: 720, label: "12 hours" },
  { value: 1440, label: "1 day" },
  { value: 2880, label: "2 days" },
  { value: 10080, label: "1 week" },
];

function BusinessSettings({
  business,
  onSaved,
  readOnly,
}: {
  business: BookingBusinessDto;
  onSaved: (b: BookingBusinessDto) => void;
  readOnly: boolean;
}) {
  const auth = useAuth();
  const [f, setF] = useState<Form>(() => toForm(business));
  const [busy, setBusy] = useState<Section | null>(null);
  const [saved, setSaved] = useState<Section | null>(null);
  const [error, setError] = useState<{ section: Section; error: ApiError | string } | null>(null);

  const set =
    <K extends keyof Form>(k: K) =>
    (v: Form[K]) =>
      setF((s) => ({ ...s, [k]: v }));
  const text = (k: keyof Form) => (e: { target: { value: string } }) => setF((s) => ({ ...s, [k]: e.target.value }));
  const apiError = error && error.error instanceof ApiError ? error.error : null;
  const fieldError = (name: string) => apiError?.field(name);

  async function save(section: Section, e?: FormEvent) {
    e?.preventDefault();
    setSaved(null);
    setError(null);
    const req = toRequest(f);
    if ("message" in req) {
      setError({
        section,
        error: req.section === section ? req.message : `${req.message} Fix this under ${SECTION_NAME[req.section]} first.`,
      });
      return;
    }
    setBusy(section);
    try {
      const updated = await api<BookingBusinessDto>("/api/booking-business", { method: "PUT", body: req.body });
      onSaved(updated);
      if (auth && updated.name && updated.name !== auth.tenant.name) {
        patchAuth({ tenant: { ...auth.tenant, name: updated.name } });
      }
      setSaved(section);
    } catch (err) {
      setError({ section, error: toApiError(err) });
    } finally {
      setBusy(null);
    }
  }

  function feedback(section: Section) {
    if (error?.section === section) {
      return typeof error.error === "string" ? <ErrorBox error={error.error} /> : <ApiErrorView error={error.error} />;
    }
    if (saved === section) return <Notice tone="success">{SAVED_TEXT[section]}</Notice>;
    return null;
  }

  const timezones: string[] = (TIMEZONES as readonly string[]).includes(f.timezone) ? [...TIMEZONES] : [f.timezone, ...TIMEZONES];
  const notices = NOTICE_PRESETS.some((p) => p.value === f.minNoticeMinutes)
    ? NOTICE_PRESETS
    : [...NOTICE_PRESETS, { value: f.minNoticeMinutes, label: formatDuration(f.minNoticeMinutes) }].sort((a, b) => a.value - b.value);
  const intervals = SLOT_INTERVALS.includes(f.slotIntervalMinutes)
    ? SLOT_INTERVALS
    : [...SLOT_INTERVALS, f.slotIntervalMinutes].sort((a, b) => a - b);
  const previewLink = `${SITE_URL}/${f.slug.trim() || "your-name"}`;
  const slugChanged = f.slug.trim() !== business.slug;
  const swatch = HEX_RE.test(f.brandColor.trim()) ? f.brandColor.trim() : DEFAULT_BRAND;

  return (
    <>
      {/* Business profile */}
      <Card>
        <SectionTitle>Business profile</SectionTitle>
        <form className="space-y-4" onSubmit={(e) => save("profile", e)} noValidate>
          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="Business name" value={f.name} onChange={text("name")} required error={fieldError("name")} autoComplete="organization" />
            <Select label="Type of business" value={f.category} onChange={(e) => set("category")(e.target.value as BusinessCategory)} error={fieldError("category")}>
              {CATEGORIES.map((c) => (
                <option key={c.value} value={c.value}>
                  {c.label}
                </option>
              ))}
            </Select>
          </div>
          <Input
            label="Booking link name"
            value={f.slug}
            onChange={(e) => set("slug")(e.target.value.toLowerCase().replace(/\s+/g, "-").replace(/[^a-z0-9-]/g, ""))}
            error={fieldError("slug")}
            autoCapitalize="none"
            autoCorrect="off"
            spellCheck={false}
            hint={
              <>
                Your booking link: <span className="font-medium break-all text-aubergine">{previewLink}</span>
                {slugChanged && business.bookingUrl && (
                  <span className="mt-0.5 block">Links to {business.bookingUrl} stop working once you save.</span>
                )}
              </>
            }
          />
          <Textarea
            label="About your business"
            value={f.description}
            onChange={text("description")}
            rows={3}
            error={fieldError("description")}
            hint="A line or two shown at the top of your booking page."
          />

          <div className="grid gap-4 sm:grid-cols-2">
            <ImageField kind="logo" url={business.logoUrl} onUploaded={onSaved} readOnly={readOnly} />
            <ImageField kind="cover" url={business.coverUrl} onUploaded={onSaved} readOnly={readOnly} />
          </div>

          <Input label="Address" value={f.address} onChange={text("address")} error={fieldError("address")} autoComplete="street-address" />
          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="Phone" type="tel" value={f.phone} onChange={text("phone")} error={fieldError("phone")} autoComplete="tel" />
            <Input
              label="WhatsApp number"
              type="tel"
              value={f.whatsappNumber}
              onChange={text("whatsappNumber")}
              error={fieldError("whatsappNumber")}
              hint="With the country code, like +234 803 000 0000."
            />
          </div>
          <Input label="Email" type="email" value={f.email} onChange={text("email")} error={fieldError("email")} autoComplete="email" />

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="min-w-0 space-y-1.5">
              <label htmlFor="brand-hex" className="block text-sm font-medium text-slate-700">
                Brand colour
              </label>
              <div className="flex items-center gap-2">
                <input
                  type="color"
                  aria-label="Pick brand colour"
                  value={normalizeHex(swatch)}
                  onChange={(e) => set("brandColor")(e.target.value.toUpperCase())}
                  className="h-11 w-12 shrink-0 cursor-pointer rounded-control border border-line bg-surface p-1 disabled:cursor-not-allowed"
                />
                <input
                  id="brand-hex"
                  className={cx(inputClass, "font-mono uppercase")}
                  value={f.brandColor}
                  maxLength={7}
                  onChange={(e) => set("brandColor")(e.target.value)}
                  aria-invalid={!!fieldError("brandColor") || undefined}
                  aria-describedby="brand-hint"
                />
              </div>
              <div id="brand-hint" className="flex items-center gap-2 text-xs text-muted">
                <span
                  className="inline-flex h-6 items-center rounded-full px-2.5 text-xs font-semibold text-white"
                  style={{ background: swatch }}
                  aria-hidden="true"
                >
                  Book now
                </span>
                Colours your booking page header and buttons.
              </div>
              {fieldError("brandColor") && <p className="text-xs font-medium text-brick">{fieldError("brandColor")}</p>}
            </div>
            <Select
              label="Time zone"
              value={f.timezone}
              onChange={text("timezone")}
              error={fieldError("timezone")}
              hint={`Every time in the app and on your booking page is in ${cityOf(f.timezone)} time.`}
            >
              {timezones.map((z) => (
                <option key={z} value={z}>
                  {z.replace(/_/g, " ")}
                </option>
              ))}
            </Select>
          </div>

          {feedback("profile")}
          {!readOnly && (
            <Button type="submit" loading={busy === "profile"}>
              Save profile
            </Button>
          )}
        </form>
      </Card>

      {/* Booking rules */}
      <Card>
        <SectionTitle>Booking rules</SectionTitle>
        <form className="space-y-4" onSubmit={(e) => save("rules", e)} noValidate>
          <Toggle
            label="Take online bookings"
            description="When this is off, your booking page stays up but customers can't book on it. You can still add bookings yourself."
            checked={f.bookingEnabled}
            onChange={set("bookingEnabled")}
            disabled={readOnly}
          />
          <div className="grid gap-4 border-t border-line-soft pt-4 sm:grid-cols-2">
            <Select
              label="Start times every"
              value={f.slotIntervalMinutes}
              onChange={(e) => set("slotIntervalMinutes")(Number(e.target.value))}
              error={fieldError("slotIntervalMinutes")}
              hint="How far apart the times offered to customers are."
            >
              {intervals.map((m) => (
                <option key={m} value={m}>
                  {m} minutes
                </option>
              ))}
            </Select>
            <Select
              label="Minimum notice"
              value={f.minNoticeMinutes}
              onChange={(e) => set("minNoticeMinutes")(Number(e.target.value))}
              error={fieldError("minNoticeMinutes")}
              hint="How soon before a start time customers can still book online."
            >
              {notices.map((p) => (
                <option key={p.value} value={p.value}>
                  {p.label}
                </option>
              ))}
            </Select>
            <Input
              label="Book up to (days ahead)"
              type="number"
              inputMode="numeric"
              min={1}
              max={365}
              value={f.maxDaysAhead}
              onChange={text("maxDaysAhead")}
              error={fieldError("maxDaysAhead")}
              hint="Between 1 and 365 days."
            />
            <Input
              label="Cancel or move up to (hours before)"
              type="number"
              inputMode="numeric"
              min={0}
              max={168}
              value={f.cancellationHours}
              onChange={text("cancellationHours")}
              error={fieldError("cancellationHours")}
              hint="After this, customers have to call you. 0 allows changes right up to the start."
            />
          </div>
          {feedback("rules")}
          {!readOnly && (
            <Button type="submit" loading={busy === "rules"}>
              Save booking rules
            </Button>
          )}
        </form>
      </Card>

      {/* Policies */}
      <Card>
        <SectionTitle>Policies</SectionTitle>
        <form className="space-y-4" onSubmit={(e) => save("policies", e)} noValidate>
          <Textarea
            label="Booking policies"
            value={f.policies}
            onChange={text("policies")}
            rows={6}
            error={fieldError("policies")}
            placeholder="For example: deposits are not refunded for cancellations within 24 hours or no-shows. Please arrive 10 minutes early."
            hint="Customers see this on your booking page and when they manage a booking. Say what happens to deposits when someone cancels late or doesn't turn up."
          />
          {feedback("policies")}
          {!readOnly && (
            <Button type="submit" loading={busy === "policies"}>
              Save policies
            </Button>
          )}
        </form>
      </Card>
    </>
  );
}

// ---------- Logo and cover ----------

const MAX_IMAGE_BYTES = 5 * 1024 * 1024;

function ImageField({
  kind,
  url,
  onUploaded,
  readOnly,
}: {
  kind: "logo" | "cover";
  url: string | null;
  onUploaded: (b: BookingBusinessDto) => void;
  readOnly: boolean;
}) {
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | string | null>(null);
  const [done, setDone] = useState(false);
  const name = kind === "logo" ? "Logo" : "Cover photo";

  async function onFile(file: File | undefined) {
    if (!file) return;
    setDone(false);
    if (!file.type.startsWith("image/")) return setError("Choose an image file, such as a JPEG or PNG.");
    if (file.size > MAX_IMAGE_BYTES) return setError("The image must be 5 MB or smaller.");
    setBusy(true);
    setError(null);
    try {
      onUploaded(await upload<BookingBusinessDto>(`/api/booking-business/${kind}`, file));
      setDone(true);
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(false);
      if (input.current) input.current.value = "";
    }
  }

  return (
    <div className="min-w-0 space-y-2">
      <p className="text-sm font-medium text-slate-700">{name}</p>
      <div className="flex items-center gap-3">
        {kind === "logo" ? (
          <div className="flex h-16 w-16 shrink-0 items-center justify-center overflow-hidden rounded-full border border-line bg-mist">
            {url ? <img src={url} alt="Current logo" className="h-full w-full object-cover" /> : <ImageIcon className="h-6 w-6 text-faint" />}
          </div>
        ) : (
          <div className="flex aspect-[16/7] w-28 shrink-0 items-center justify-center overflow-hidden rounded-control border border-line bg-mist">
            {url ? <img src={url} alt="Current cover photo" className="h-full w-full object-cover" /> : <ImageIcon className="h-6 w-6 text-faint" />}
          </div>
        )}
        <div className="min-w-0">
          <input ref={input} type="file" accept="image/*" className="sr-only" tabIndex={-1} aria-hidden="true" onChange={(e) => onFile(e.target.files?.[0])} />
          {!readOnly && (
            <Button variant="secondary" loading={busy} onClick={() => input.current?.click()}>
              {url ? `Change ${name.toLowerCase()}` : `Upload ${name.toLowerCase()}`}
            </Button>
          )}
          <p className="mt-1 text-xs text-muted">
            {kind === "logo" ? "Square works best. Up to 5 MB." : "A wide photo of your space or work. Up to 5 MB."}
          </p>
        </div>
      </div>
      {done && <Notice tone="success">{name} updated.</Notice>}
      {error && <ErrorBox error={error} />}
    </div>
  );
}

// ---------- Opening hours ----------

interface HoursRow {
  weekday: Weekday;
  open: string;
  close: string;
  closed: boolean;
}

function toRows(list: OpeningHours[] | undefined): HoursRow[] {
  const src = Array.isArray(list) ? list : [];
  return WEEK_ORDER.map((w) => {
    const h = src.find((x) => x.weekday === w);
    return h
      ? { weekday: w, open: h.open?.slice(0, 5) || "09:00", close: h.close?.slice(0, 5) || "18:00", closed: !!h.closed }
      : { weekday: w, open: "09:00", close: "18:00", closed: true };
  });
}

function HoursEditor({ initial, tz, readOnly }: { initial: OpeningHours[]; tz: string; readOnly: boolean }) {
  const [rows, setRows] = useState<HoursRow[]>(() => toRows(initial));
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<ApiError | string | null>(null);
  const [invalid, setInvalid] = useState<number[]>([]);

  const update = (w: Weekday, patch: Partial<HoursRow>) => {
    setSaved(false);
    setRows((rs) => rs.map((r) => (r.weekday === w ? { ...r, ...patch } : r)));
  };

  async function save(e: FormEvent) {
    e.preventDefault();
    setSaved(false);
    setError(null);
    const bad = rows.filter((r) => !r.closed && (!r.open || !r.close || clockToMinutes(r.close) <= clockToMinutes(r.open))).map((r) => r.weekday as number);
    setInvalid(bad);
    if (bad.length) {
      setError(`Closing time must be after opening time on ${bad.map((w) => WEEKDAYS[w]).join(", ")}.`);
      return;
    }
    setBusy(true);
    try {
      const res = await api<OpeningHours[]>("/api/booking-business/hours", { method: "PUT", body: rows });
      if (Array.isArray(res) && res.length) setRows(toRows(res));
      setSaved(true);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <SectionTitle>Opening hours</SectionTitle>
      <p className="-mt-1 mb-3 text-sm text-muted">
        Customers can only book while you are open, and each staff member&apos;s own hours apply on top. Times are in {cityOf(tz)} time.
      </p>
      <form onSubmit={save} noValidate>
        <ul className="divide-y divide-line-soft border-y border-line-soft">
          {rows.map((r) => {
            const day = WEEKDAYS[r.weekday];
            const wrong = invalid.includes(r.weekday);
            return (
              <li key={r.weekday} className="grid gap-x-4 gap-y-2 py-3 sm:grid-cols-[11rem_minmax(0,1fr)] sm:items-center">
                <Toggle
                  label={day}
                  description={r.closed ? "Closed" : "Open"}
                  checked={!r.closed}
                  onChange={(open) => update(r.weekday, { closed: !open })}
                  disabled={readOnly}
                />
                {r.closed ? (
                  <p className="text-sm text-muted sm:pl-2">Closed all day</p>
                ) : (
                  <div className="flex items-center gap-2 sm:pl-2">
                    <input
                      type="time"
                      step={300}
                      aria-label={`${day} opens at`}
                      className={cx(inputClass, "tnum max-w-[9rem]")}
                      value={r.open}
                      aria-invalid={wrong || undefined}
                      onChange={(e) => update(r.weekday, { open: e.target.value })}
                    />
                    <span className="text-sm text-muted">to</span>
                    <input
                      type="time"
                      step={300}
                      aria-label={`${day} closes at`}
                      className={cx(inputClass, "tnum max-w-[9rem]")}
                      value={r.close}
                      aria-invalid={wrong || undefined}
                      onChange={(e) => update(r.weekday, { close: e.target.value })}
                    />
                  </div>
                )}
              </li>
            );
          })}
        </ul>
        <div className="mt-4 space-y-3">
          {saved && <Notice tone="success">Opening hours saved.</Notice>}
          {typeof error === "string" ? <ErrorBox error={error} /> : <ApiErrorView error={error} />}
          {!readOnly && (
            <Button type="submit" loading={busy}>
              Save opening hours
            </Button>
          )}
        </div>
      </form>
    </Card>
  );
}

// ---------- Paystack ----------

function PaystackCard({
  business,
  onChange,
  readOnly,
}: {
  business: BookingBusinessDto;
  onChange: (b: BookingBusinessDto) => void;
  readOnly: boolean;
}) {
  const auth = useAuth();
  const [key, setKey] = useState("");
  const [busy, setBusy] = useState<"connect" | "disconnect" | null>(null);
  const [error, setError] = useState<ApiError | string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const connected = business.paystackConnected;
  const { services } = useServices();
  const offReason = depositsOffReason(business);
  const depositsSet = services.some((s) => s.depositType !== "None");
  const webhook = auth ? `${API_URL}/api/webhooks/paystack/bookings/${auth.tenant.id}` : null;

  async function connect(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setDone(null);
    const secretKey = key.trim();
    if (!/^sk_(live|test)_\S+$/.test(secretKey)) {
      setError("Paste your Paystack secret key. It starts with sk_live_ (or sk_test_ while you try things out).");
      return;
    }
    setBusy("connect");
    try {
      const saved = await api<BookingBusinessDto>("/api/booking-business/paystack", { method: "PUT", body: { secretKey } });
      onChange(saved);
      setKey("");
      if (connected) setDone("Paystack key updated.");
      else if (saved.depositsEnabled) setDone("Paystack connected. Services with a deposit now ask for it when customers book.");
      else setDone("Paystack connected. Your plan doesn't include deposits yet, so customers aren't asked for one until you upgrade.");
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(null);
    }
  }

  async function disconnect() {
    setError(null);
    setDone(null);
    setBusy("disconnect");
    try {
      onChange(await api<BookingBusinessDto>("/api/booking-business/paystack", { method: "DELETE" }));
      setConfirming(false);
      setDone("Paystack disconnected. New bookings are confirmed without a deposit.");
    } catch (err) {
      setConfirming(false);
      setError(toApiError(err));
    } finally {
      setBusy(null);
    }
  }

  return (
    <Card>
      <SectionTitle action={connected ? <StatusBadge tone="green">Connected</StatusBadge> : <StatusBadge tone="neutral">Not connected</StatusBadge>}>
        Online deposits
      </SectionTitle>
      <div className="max-w-prose space-y-2 text-sm text-slate-700">
        <p>
          Take a deposit when customers book online, so fewer people fail to turn up. Deposits are paid through Paystack straight into your own
          Paystack account. BookLink never holds your money.
        </p>
        <p className="text-muted">
          Deposits need a paid plan. Set the deposit on each service; services without one are confirmed straight away.
        </p>
      </div>
      {offReason && depositsSet && (
        <div className="mt-4">
          <Notice tone="warning">Some services have a deposit, but customers aren&apos;t asked for it. {offReason}</Notice>
        </div>
      )}

      {!readOnly && (
        <form onSubmit={connect} className="mt-4 space-y-3" noValidate>
          <Input
            label={connected ? "Replace secret key" : "Paystack secret key"}
            type="password"
            autoComplete="off"
            spellCheck={false}
            placeholder="sk_live_..."
            value={key}
            onChange={(e) => setKey(e.target.value)}
            hint="Find it in Paystack under Settings, API keys and webhooks."
          />
          {done && <Notice tone="success">{done}</Notice>}
          {typeof error === "string" ? <ErrorBox error={error} /> : <ApiErrorView error={error} plan="Solo" />}
          <div className="flex flex-wrap gap-2">
            <Button type="submit" loading={busy === "connect"}>
              {connected ? "Update key" : "Connect Paystack"}
            </Button>
            {connected && (
              <Button variant="danger" onClick={() => setConfirming(true)}>
                Disconnect Paystack
              </Button>
            )}
          </div>
        </form>
      )}

      {connected && webhook && (
        <div className="mt-4 border-t border-line-soft pt-4">
          <p className="text-sm font-medium text-slate-700">Webhook URL</p>
          <p className="mt-0.5 text-xs text-muted">
            Optional. Add this in Paystack so a payment still counts when the customer closes the page before it finishes.
          </p>
          <div className="mt-2 flex flex-col gap-2 sm:flex-row sm:items-center">
            <code className="min-w-0 flex-1 rounded-control border border-line bg-mist px-3 py-2 text-xs break-all">{webhook}</code>
            <CopyButton text={webhook} label="Copy URL" />
          </div>
        </div>
      )}

      <Modal
        open={confirming}
        title="Disconnect Paystack?"
        onClose={() => setConfirming(false)}
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirming(false)}>
              Keep connected
            </Button>
            <Button variant="danger" loading={busy === "disconnect"} onClick={disconnect}>
              Disconnect Paystack
            </Button>
          </>
        }
      >
        <p className="text-sm text-slate-700">
          New online bookings will be confirmed without a deposit. Deposits already paid stay in your Paystack account, and refunds for them will
          need to be made in Paystack.
        </p>
      </Modal>
    </Card>
  );
}

// ---------- Share ----------

function ShareCard({ business }: { business: BookingBusinessDto }) {
  const link = business.bookingUrl || `${SITE_URL}/${business.slug}`;
  const safeName = business.name.replace(/&/g, "&amp;").replace(/"/g, "&quot;").replace(/</g, "&lt;");
  const embed = `<iframe src="${SITE_URL}/${business.slug}?embed=1" width="100%" height="900" style="border:0;max-width:560px" title="Book with ${safeName}"></iframe>`;

  return (
    <Card>
      <SectionTitle>Share your booking page</SectionTitle>
      <p className="-mt-1 mb-3 text-sm text-muted">Put this link in your Instagram bio, WhatsApp status and Google profile.</p>
      {!business.bookingEnabled && (
        <div className="mb-3">
          <Notice tone="warning">Online booking is off. Customers can see your page but can&apos;t book until you turn it on under Booking rules.</Notice>
        </div>
      )}
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
        <p className="min-w-0 flex-1 rounded-control border border-line bg-mist px-3 py-2.5 text-sm font-medium break-all text-aubergine">{link}</p>
        <div className="flex gap-2">
          <CopyButton text={link} label="Copy link" />
          <a href={link} target="_blank" rel="noopener noreferrer" className={buttonClass("secondary")}>
            Open booking page
          </a>
        </div>
      </div>

      <div className="mt-5 border-t border-line-soft pt-4">
        <label htmlFor="embed-code" className="text-sm font-medium text-slate-700">
          Embed on your website
        </label>
        <p className="mt-0.5 text-xs text-muted">Paste this code into your website where you want the booking form to appear.</p>
        <textarea
          id="embed-code"
          readOnly
          rows={4}
          value={embed}
          onFocus={(e) => e.currentTarget.select()}
          className={cx(inputClass, "mt-2 font-mono text-xs leading-relaxed sm:text-xs")}
        />
        <div className="mt-2">
          <CopyButton text={embed} label="Copy embed code" />
        </div>
      </div>
    </Card>
  );
}
