import type { ReactNode } from "react";
import { BuildingIcon, CalendarIcon, CheckIcon, HomeIcon, MenuIcon, MessageIcon, PhoneIcon, PlusIcon, WhatsAppIcon } from "./icons";
import { cx } from "./styles";

/**
 * PropertyLink's real screens, redrawn in HTML for the public pages, in the app's own palette and faces. Each mirrors:
 * - ListingEditorScreen: the listing wizard's Costs step with its ledger preview (studio/(console)/listings/wizard.tsx).
 * - EnquiriesScreen: the studio's enquiries inbox on a phone (studio/(console)/enquiries/page.tsx, studio-shell.tsx).
 * - ViewingsScreen: the viewings agenda with a request to confirm (studio/(console)/viewings/page.tsx).
 * - ListingCardScreen, WhatsAppScreen, StayScreen, TeamScreen: crops of the listing card, the WhatsApp message a
 *   seeker sends from a listing (contact.tsx), the short-let ledger (stay-booker.tsx) and the team page.
 * - MoveInLedger: the cost ledger on a listing page (components/ledger.tsx).
 * Names, places and naira amounts are a business's own sample figures, never PropertyLink plan prices.
 */

const tiny = "text-[0.6875rem] leading-snug";
const micro = "text-[0.625rem] leading-tight";

/** A drawing with a description for people who can't see it. */
export function Drawing({ label, className, children }: { label: string; className?: string; children: ReactNode }) {
  return (
    <div role="img" aria-label={label} className={className}>
      <div aria-hidden="true">{children}</div>
    </div>
  );
}

function Seal() {
  return (
    <span className="inline-flex items-center gap-0.5 rounded-[4px] bg-seal px-1.5 py-0.5 text-[0.625rem] leading-none font-semibold text-white">
      <CheckIcon className="h-2.5 w-2.5" strokeWidth={3} />
      Verified
    </span>
  );
}

function Dot({ tone }: { tone: "adire" | "seal" | "ochre" }) {
  return <span className={cx("h-1.5 w-1.5 shrink-0 rounded-full", tone === "adire" ? "bg-adire" : tone === "seal" ? "bg-seal" : "bg-ochre")} />;
}

function Status({ tone, children }: { tone: "adire" | "seal" | "ochre"; children: ReactNode }) {
  const look = tone === "adire" ? "bg-adire-wash text-adire" : tone === "seal" ? "bg-seal-wash text-seal" : "bg-ochre-wash font-semibold text-ochre";
  return (
    <span className={cx("inline-flex items-center gap-1 rounded-full px-1.5 py-0.5", micro, look)}>
      <Dot tone={tone} />
      {children}
    </span>
  );
}

/** The ledger's dotted leader between a label and its amount. */
function Leader() {
  return <span className="mx-1.5 min-w-4 flex-1 translate-y-[-0.25em] border-b-2 border-dotted border-line" />;
}

function LedgerRows({ rows, total, totalLabel }: { rows: [string, string][]; total: string; totalLabel: string }) {
  return (
    <>
      <dl className={cx("mt-2 space-y-1.5", tiny)}>
        {rows.map(([k, v]) => (
          <div key={k} className="flex items-baseline">
            <dt className="text-slate-700">{k}</dt>
            <Leader />
            <dd className="price text-asphalt">{v}</dd>
          </div>
        ))}
      </dl>
      <div className="mt-2.5 flex items-baseline justify-between border-t-[3px] border-double border-asphalt pt-2">
        <span className="text-xs font-semibold">{totalLabel}</span>
        <span className="price text-base leading-none">{total}</span>
      </div>
    </>
  );
}

const MOVE_IN: [string, string][] = [
  ["First year's rent", "₦3,500,000"],
  ["Agency fee", "₦350,000"],
  ["Legal fee", "₦350,000"],
  ["Caution deposit", "₦350,000"],
];

/** A two-storey block of flats, as a listing's cover photo. */
function HouseArt({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 248 150" className={cx("block w-full", className)} preserveAspectRatio="xMidYMid slice">
      <rect width="248" height="150" fill="#cfe0e6" />
      <rect y="118" width="248" height="32" fill="#9fae9a" />
      <rect x="38" y="34" width="172" height="96" fill="#efe6d6" />
      <rect x="30" y="26" width="188" height="10" fill="#8c5a3c" />
      {[0, 1, 2, 3].map((c) => [0, 1].map((r) => <rect key={`${c}-${r}`} x={52 + c * 40} y={48 + r * 30} width="24" height="18" fill="#5f7d8c" />))}
      <rect x="112" y="104" width="24" height="26" fill="#6b4a33" />
      <rect x="0" y="128" width="248" height="4" fill="#d9d2c3" />
    </svg>
  );
}

// ---------- Hero: the three studio screens ----------

/** The listing editor's Costs step: every fee in, one move-in total out. */
export function ListingEditorScreen() {
  const steps = ["Basics", "Location", "Costs", "Photos", "Review"];
  const costs = [
    ["Agency fee", "Agency fee", "350,000"],
    ["Legal fee", "Legal fee", "350,000"],
    ["Caution deposit", "Caution", "350,000"],
  ];
  return (
    <div className="w-[25rem] rounded-[10px] border border-line bg-plaster font-sans text-asphalt">
      <div className="flex items-center justify-between border-b border-line bg-surface px-4 py-2.5">
        <span className="text-xs font-semibold">New listing</span>
        <span className={cx(tiny, "text-muted")}>Draft saved</span>
      </div>
      <div className="px-4 pt-3 pb-4">
        <ol className="flex gap-1">
          {steps.map((s, i) => (
            <li
              key={s}
              className={cx(
                "flex items-center gap-1 rounded-full border px-2 py-1",
                micro,
                i === 2 ? "border-adire bg-adire text-white" : i < 2 ? "border-line bg-surface" : "border-line bg-surface text-muted",
              )}
            >
              <span className={cx("flex h-3.5 w-3.5 items-center justify-center rounded-full text-[0.5625rem]", i === 2 ? "bg-white text-adire" : "bg-plaster-2")}>
                {i < 2 ? <CheckIcon className="h-2 w-2" strokeWidth={3} /> : i + 1}
              </span>
              {s}
            </li>
          ))}
        </ol>
        <p className="mt-3 text-sm font-semibold">Costs</p>
        <p className={cx(tiny, "text-muted")}>List every fee a tenant pays to move in. Seekers see them added up.</p>
        <div className="mt-2.5 grid grid-cols-[minmax(0,1fr)_9.5rem] gap-2.5">
          <div className="space-y-1.5">
            {costs.map(([kind, label, amount]) => (
              <div key={kind} className="rounded-[6px] border border-line bg-surface p-1.5">
                <p className={cx(micro, "text-muted")}>{kind}</p>
                <div className="mt-0.5 flex gap-1">
                  <span className={cx("flex-1 truncate rounded-[4px] border border-field px-1.5 py-1", micro)}>{label}</span>
                  <span className={cx("tnum w-14 rounded-[4px] border border-field px-1.5 py-1 text-right", micro)}>{amount}</span>
                </div>
              </div>
            ))}
            <span className={cx("inline-flex items-center gap-1 rounded-[6px] border border-field bg-surface px-2 py-1 font-semibold", micro)}>
              <PlusIcon className="h-2.5 w-2.5" /> Add a cost
            </span>
          </div>
          <div className="self-start rounded-[8px] border border-line bg-surface px-2.5 pt-2 pb-2.5">
            <p className="display-wide text-[0.6875rem] font-bold">What it costs to move in</p>
            <dl className={cx("mt-1.5 space-y-1", micro)}>
              {[["Rent", "3,500,000"], ["Agency fee", "350,000"], ["Legal fee", "350,000"], ["Caution", "350,000"]].map(([k, v]) => (
                <div key={k} className="flex items-baseline">
                  <dt className="text-slate-700">{k}</dt>
                  <Leader />
                  <dd className="price">{v}</dd>
                </div>
              ))}
            </dl>
            <div className="mt-1.5 border-t-[3px] border-double border-asphalt pt-1">
              <p className={cx(micro, "font-semibold")}>Move-in total</p>
              <p className="price text-sm leading-tight">₦4,550,000</p>
            </div>
          </div>
        </div>
        <div className="mt-3 flex justify-between">
          <span className={cx("rounded-[6px] px-2 py-1.5 font-semibold text-slate-700", tiny)}>Back</span>
          <span className={cx("rounded-[6px] bg-adire px-3 py-1.5 font-semibold text-white", tiny)}>Save and continue</span>
        </div>
      </div>
    </div>
  );
}

/** The enquiries inbox on a phone: a new enquiry, answered on WhatsApp in one tap. */
export function EnquiriesScreen() {
  const nav = [
    { label: "Dashboard", Icon: HomeIcon },
    { label: "Listings", Icon: BuildingIcon },
    { label: "Enquiries", Icon: MessageIcon, on: true },
    { label: "Viewings", Icon: CalendarIcon },
    { label: "More", Icon: MenuIcon },
  ];
  return (
    <div className="relative flex h-[30rem] flex-col bg-plaster font-sans text-asphalt">
      <div className="flex items-center justify-between border-b border-line bg-surface px-3 pt-8 pb-2">
        <span className="flex items-baseline gap-1">
          <span className="display-wide text-[0.8125rem] font-bold">
            Property<span className="text-adire">Link</span>
          </span>
          <span className={cx(micro, "text-muted")}>Studio</span>
        </span>
        <span className={cx("inline-flex items-center gap-0.5 rounded-[6px] bg-adire px-1.5 py-1 font-semibold text-white", micro)}>
          <PlusIcon className="h-2.5 w-2.5" /> New listing
        </span>
      </div>
      <div className="px-3 pt-3">
        <p className="text-[0.9375rem] font-semibold">Enquiries</p>
        <div className={cx("mt-1.5 flex gap-3 border-b border-line", tiny)}>
          {["New", "Replied", "Closed", "All"].map((t, i) => (
            <span key={t} className={cx("relative pb-1.5", i === 0 ? "font-semibold text-asphalt" : "text-muted")}>
              {t}
              {i === 0 && <span className="absolute inset-x-0 -bottom-px h-[3px] rounded-full bg-adire" />}
            </span>
          ))}
        </div>
      </div>
      <ul className="space-y-2 px-3 pt-2.5">
        <li className="rounded-[8px] border border-adire-line bg-surface p-2.5">
          <div className="flex items-start justify-between gap-2">
            <span className="min-w-0">
              <span className="block text-xs font-semibold">Chiamaka Obi</span>
              <span className={cx("block truncate text-muted", micro)}>About 2 bedroom flat, Yaba. Today 09:14</span>
            </span>
            <Status tone="adire">New</Status>
          </div>
          <p className={cx("mt-1.5 text-slate-700", tiny)}>Hello, is it still available? Can I see it on Saturday morning?</p>
          <div className="mt-2 flex gap-1.5">
            <span className={cx("inline-flex flex-1 items-center justify-center gap-1 rounded-[6px] bg-whatsapp py-1.5 font-semibold text-white", micro)}>
              <WhatsAppIcon className="h-3 w-3" /> WhatsApp
            </span>
            <span className={cx("inline-flex flex-1 items-center justify-center gap-1 rounded-[6px] border border-field bg-surface py-1.5 font-semibold", micro)}>
              <PhoneIcon className="h-2.5 w-2.5" /> 0803 123 4567
            </span>
          </div>
        </li>
        <li className="rounded-[8px] border border-adire-line bg-surface p-2.5">
          <div className="flex items-start justify-between gap-2">
            <span className="min-w-0">
              <span className="block text-xs font-semibold">Femi Adeyemi</span>
              <span className={cx("block truncate text-muted", micro)}>About 3 bedroom duplex, Lekki. Yesterday</span>
            </span>
            <Status tone="adire">New</Status>
          </div>
          <p className={cx("mt-1.5 text-slate-700", tiny)}>Is the service charge paid yearly?</p>
        </li>
      </ul>
      <div className="mt-auto grid grid-cols-5 bg-asphalt pt-1.5 pb-2">
        {nav.map(({ label, Icon, on }) => (
          <span key={label} className={cx("flex flex-col items-center gap-0.5", micro, on ? "text-white" : "text-on-asphalt")}>
            <Icon className={cx("h-3.5 w-3.5", on && "text-adire-light")} />
            {label}
          </span>
        ))}
      </div>
    </div>
  );
}

/** The viewings agenda: seekers book into the agent's weekly windows; the agent confirms. */
export function ViewingsScreen() {
  const rows = [
    { at: "10:00", who: "Tunde Bakare", what: "2 bedroom flat, Yaba. Agent: Ngozi", tone: "ochre" as const, status: "Requested", confirm: true },
    { at: "11:30", who: "Aisha Bello", what: "Mini flat, Surulere. Agent: Ngozi", tone: "seal" as const, status: "Confirmed" },
    { at: "14:00", who: "Kunle Ojo", what: "3 bedroom duplex, Lekki. Agent: Tunde", tone: "seal" as const, status: "Confirmed" },
  ];
  return (
    <div className="w-[18rem] rounded-[10px] border border-line bg-plaster p-3 font-sans text-asphalt">
      <p className="text-sm font-semibold">Viewings</p>
      <p className={cx(tiny, "text-muted")}>Seekers book into your weekly windows.</p>
      <p className={cx("mt-2.5 mb-1 font-semibold text-slate-700", tiny)}>Today, Sat 14 Nov</p>
      <ul className="divide-y divide-line-soft overflow-hidden rounded-[8px] border border-line bg-surface">
        {rows.map((r) => (
          <li key={r.at} className="flex gap-2 p-2">
            <span className="tnum w-9 shrink-0 text-xs font-semibold">{r.at}</span>
            <span className="min-w-0 flex-1">
              <span className="block text-xs font-semibold">{r.who}</span>
              <span className={cx("block truncate text-muted", micro)}>{r.what}</span>
              <span className="mt-1 flex items-center gap-1.5">
                <Status tone={r.tone}>{r.status}</Status>
                {r.confirm && <span className={cx("rounded-[5px] bg-adire px-2 py-0.5 font-semibold text-white", micro)}>Confirm</span>}
              </span>
            </span>
          </li>
        ))}
      </ul>
      <div className="mt-2.5 rounded-[8px] border border-line bg-surface p-2">
        <p className={cx("font-semibold", tiny)}>My weekly viewing windows</p>
        <p className={cx("mt-0.5 text-muted", micro)}>Saturday 10:00 to 15:00, 30 min each</p>
        <p className={cx("text-muted", micro)}>Wednesday 16:00 to 18:00, 30 min each</p>
      </div>
    </div>
  );
}

// ---------- Feature crops ----------

/** The listing card seekers see: price, rooms, place and the agency's Verified seal. */
export function ListingCardScreen() {
  return (
    <div className="w-[15rem] overflow-hidden rounded-t-[10px] border border-b-0 border-line bg-surface font-sans text-asphalt">
      <HouseArt className="h-20" />
      <div className="px-3 pt-2.5 pb-4">
        <p className="price text-base text-adire">
          ₦3,500,000<span className="text-xs font-semibold text-muted">/year</span>
        </p>
        <p className="text-xs font-semibold">2 bedroom flat in a gated close</p>
        <p className={cx(tiny, "text-slate-700")}>2 beds, 2 baths. Flat</p>
        <p className={cx(tiny, "text-muted")}>Yaba, Lagos</p>
        <p className={cx("mt-1.5 flex items-center gap-1.5 text-slate-700", micro)}>
          Haven Homes <Seal />
        </p>
      </div>
    </div>
  );
}

/** The message a seeker sends from "Chat on WhatsApp": it names the listing. */
export function WhatsAppScreen() {
  return (
    <div className="w-[16rem] rounded-t-[12px] bg-[#e9e2d8] px-3 pt-3 pb-6 font-sans text-[#111b21]">
      <div className="flex items-center gap-2 rounded-[8px] bg-[#075e54] px-2.5 py-2 text-white">
        <span className="flex h-6 w-6 items-center justify-center rounded-full bg-white/25 text-[0.625rem] font-bold">NE</span>
        <span className="text-xs font-semibold">Ngozi, Haven Homes</span>
      </div>
      <div className="mt-3 ml-6 rounded-[8px] rounded-tr-none bg-[#d9fdd3] px-2.5 py-2">
        <p className={tiny}>
          Hello Ngozi, I saw &quot;2 bedroom flat in a gated close&quot; on PropertyLink. Is it still available?
        </p>
        <p className="mt-1 text-right text-[0.5625rem] text-[#5d6a70]">09:14</p>
      </div>
      <div className="mt-2 mr-8 rounded-[8px] rounded-tl-none bg-white px-2.5 py-2">
        <p className={tiny}>Yes, it is. Would Saturday at 10 work for a viewing?</p>
        <p className="mt-1 text-right text-[0.5625rem] text-[#5d6a70]">09:20</p>
      </div>
    </div>
  );
}

/** The seeker's side of viewings: a day and a time inside the agent's weekly windows (contact.tsx, ViewingPicker). */
export function ViewingPickerScreen() {
  const days = [
    ["Sat", "14 Nov"],
    ["Sun", "15 Nov"],
    ["Wed", "18 Nov"],
    ["Sat", "21 Nov"],
  ];
  const times = ["10:00", "10:30", "11:00", "11:30"];
  return (
    <div className="w-[16.5rem] rounded-t-[10px] border border-b-0 border-line bg-surface px-3 pt-3 pb-5 font-sans text-asphalt">
      <div className={cx("flex gap-3 border-b border-line", tiny)}>
        <span className="pb-1.5 text-muted">Send enquiry</span>
        <span className="relative pb-1.5 font-semibold">
          Book a viewing
          <span className="absolute inset-x-0 -bottom-px h-[3px] rounded-full bg-adire" />
        </span>
      </div>
      <p className={cx("mt-2 text-slate-700", micro)}>Day</p>
      <div className="mt-1 flex gap-1">
        {days.map(([d, n], i) => (
          <span key={n} className={cx("flex min-w-11 flex-col items-center rounded-[6px] border px-1 py-1", i === 0 ? "border-adire bg-adire text-white" : "border-line")}>
            <span className="text-[0.5625rem]">{d}</span>
            <span className="text-[0.6875rem] font-semibold">{n}</span>
          </span>
        ))}
      </div>
      <p className={cx("mt-2 text-slate-700", micro)}>Time</p>
      <div className="mt-1 flex flex-wrap gap-1">
        {times.map((t) => (
          <span key={t} className={cx("tnum rounded-full border px-2 py-0.5 text-[0.6875rem]", t === "10:30" ? "border-adire bg-adire text-white" : "border-line")}>
            {t}
          </span>
        ))}
      </div>
      <span className="mt-2.5 flex min-h-8 items-center justify-center rounded-[6px] bg-adire text-[0.6875rem] font-semibold text-white">Book viewing</span>
      <p className={cx("mt-1.5 text-muted", micro)}>Viewings are free. Never pay an &quot;inspection fee&quot;.</p>
    </div>
  );
}

/** A short-let guest's stay, paid online. */
export function StayScreen() {
  return (
    <div className="w-[15.5rem] rounded-t-[10px] border border-b-0 border-line bg-surface px-3.5 pt-3 pb-5 font-sans text-asphalt">
      <p className="display-wide text-xs font-bold">Your stay</p>
      <LedgerRows rows={[["₦85,000 × 3 nights", "₦255,000"], ["Cleaning", "₦15,000"]]} total="₦270,000" totalLabel="Total" />
      <p className={cx("mt-1.5 text-muted", micro)}>Fri 20 Nov to Mon 23 Nov, 2 guests. Paid securely; the host is paid after check-in.</p>
      <span className="mt-2.5 flex min-h-8 items-center justify-center rounded-[6px] bg-adire text-[0.6875rem] font-semibold text-white">Reserve and pay ₦270,000</span>
    </div>
  );
}

/** The team page: each agent with their own login and role. */
export function TeamScreen() {
  const people = [
    ["TB", "Tunde Bakare", "Owner"],
    ["NE", "Ngozi Eze", "Admin"],
    ["KN", "Kelechi Nwosu", "Staff"],
  ];
  return (
    <div className="w-[16rem] rounded-t-[10px] border border-b-0 border-line bg-surface px-3 pt-3 pb-5 font-sans text-asphalt">
      <p className="text-sm font-semibold">Team</p>
      <ul className="mt-2 divide-y divide-line-soft">
        {people.map(([initials, name, role]) => (
          <li key={name} className="flex items-center gap-2 py-1.5">
            <span className="flex h-6 w-6 items-center justify-center rounded-full bg-plaster-2 text-[0.5625rem] font-semibold text-slate-700">{initials}</span>
            <span className="min-w-0 flex-1 truncate text-xs font-medium">{name}</span>
            <span className={cx("rounded-full bg-plaster-2 px-1.5 py-0.5 text-slate-700", micro)}>{role}</span>
          </li>
        ))}
      </ul>
      <span className={cx("mt-2 inline-flex items-center gap-1 rounded-[6px] border border-field px-2 py-1 font-semibold", micro)}>
        <PlusIcon className="h-2.5 w-2.5" /> Invite someone
      </span>
    </div>
  );
}

// ---------- Home ----------

/** A listing's move-in ledger, the promise behind every PropertyLink listing. */
export function MoveInLedger() {
  return (
    <div className="w-[19rem] overflow-hidden rounded-[10px] border border-line bg-surface font-sans text-asphalt">
      <HouseArt className="h-32" />
      <div className="px-4 pt-3 pb-4">
        <p className="text-xs font-semibold">2 bedroom flat in a gated close, Yaba</p>
        <p className={cx("flex items-center gap-1.5 text-muted", micro)}>
          Haven Homes <Seal />
        </p>
        <p className="display-wide mt-3 text-xs font-bold">What it costs to move in</p>
        <LedgerRows rows={MOVE_IN} total="₦4,550,000" totalLabel="Move-in total" />
        <p className={cx("mt-2.5 border-t border-line-soft pt-2 font-medium", micro)}>Never pay before you have inspected the property and met the agent.</p>
      </div>
    </div>
  );
}
