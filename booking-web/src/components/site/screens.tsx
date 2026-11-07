import type { CSSProperties, ReactNode } from "react";
import { CheckIcon, PhoneIcon, WhatsAppIcon } from "@/components/icons";
import { cx } from "@/components/styles";

/**
 * BookLink's real screens, redrawn in HTML for the public pages so they need no image files. Each mirrors the component
 * it names, in the app's own face and colours (Archivo, rose, aubergine, mist):
 * - BookingPhoneScreen: the public booking page (app/[slug]/booking-page.tsx, BusinessHeader, DateSlotPicker, the book bar).
 * - AppointmentCardDrawing: the appointment card (components/appointment-card.tsx) once the deposit is paid.
 * - ReminderDrawing: the ready-written WhatsApp reminder the booking panel opens (BookingCore's reminder text).
 * - SlotsDrawing, DepositDrawing, BookingPanelDrawing, CalendarDrawing: crops of the slot picker, the details step,
 *   the booking drawer and the calendar day view.
 * Sample data is one business's own: Kemi's Hair Studio in Lekki and its prices. Never a BookLink plan price.
 */

const tiny = "text-[0.6875rem] leading-snug";
const paint = (i: number) => ({ "--ls-i": i }) as CSSProperties;

/** A drawing with a text alternative: the drawing itself is hidden from screen readers. */
export function Drawing({ label, className, children }: { label: string; className?: string; children: ReactNode }) {
  return (
    <div role="img" aria-label={label} className={className}>
      <div aria-hidden="true">{children}</div>
    </div>
  );
}

function Tick({ className }: { className?: string }) {
  return (
    <span className={cx("flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-rose text-white", className)}>
      <CheckIcon className="h-3 w-3" />
    </span>
  );
}

function Chip({ children, on, off }: { children: ReactNode; on?: boolean; off?: boolean }) {
  return (
    <span
      className={cx(
        "rounded-[8px] border py-1.5 text-center text-xs font-semibold tnum",
        on ? "border-rose bg-rose text-white" : off ? "border-transparent text-faint line-through" : "border-line bg-surface",
      )}
    >
      {children}
    </span>
  );
}

/** The public booking page: knotless braids chosen, Saturday 14 picked, 10:30 selected, the deposit bar ready. */
export function BookingPhoneScreen() {
  const days = [
    { d: "Thu", n: 12, free: true },
    { d: "Fri", n: 13, free: false },
    { d: "Sat", n: 14, on: true, free: true },
    { d: "Sun", n: 15, free: false },
    { d: "Mon", n: 16, free: true },
  ];
  return (
    <div className="ls-paint bg-mist pb-28 font-sans text-aubergine" style={paint(0)}>
      <div className="h-9 bg-rose" />
      <div className="px-3.5 pb-3">
        <div className="-mt-6 flex h-12 w-12 items-center justify-center rounded-full border-4 border-mist bg-surface font-expanded text-sm font-extrabold text-rose">
          KH
        </div>
        <p className="mt-1 font-expanded text-[0.9375rem] leading-tight font-extrabold">Kemi&apos;s Hair Studio</p>
        <p className={`${tiny} text-muted`}>Lekki Phase 1, Lagos</p>

        <div className="mt-2.5 flex items-start gap-2 rounded-[10px] border border-line bg-surface px-2.5 py-2">
          <Tick className="mt-px" />
          <div className="min-w-0">
            <p className={`${tiny} font-semibold`}>Knotless braids, medium</p>
            <p className={`${tiny} text-muted`}>3 h, ₦25,000</p>
          </div>
        </div>

        <div className="mt-2 rounded-[10px] border border-line bg-surface px-2.5 py-2">
          <p className="text-[0.625rem] font-semibold text-slate-700">When</p>
          <div className="mt-1 grid grid-cols-5 gap-1">
            {days.map((x) => (
              <span
                key={x.n}
                className={cx(
                  "flex flex-col items-center rounded-[8px] border py-1",
                  x.on ? "border-rose bg-rose text-white" : x.free ? "border-line" : "border-transparent text-faint",
                )}
              >
                <span className="text-[0.625rem]">{x.d}</span>
                <span className="font-expanded text-sm font-bold tnum">{x.n}</span>
              </span>
            ))}
          </div>
          <p className="mt-2 text-[0.625rem] text-muted">Morning</p>
          <div className="mt-1 grid grid-cols-3 gap-1">
            <Chip>09:30</Chip>
            <Chip on>10:30</Chip>
            <Chip>11:00</Chip>
          </div>
        </div>
      </div>
      <div className="bg-surface px-3.5 pt-2.5 pb-3 shadow-bar">
        <p className="rounded-[10px] bg-rose py-2.5 text-center text-xs font-semibold text-white">Pay ₦5,000 deposit and book</p>
        <p className="mt-1.5 text-center text-[0.625rem] text-muted">With Tunde. Times are in Lagos time.</p>
      </div>
    </div>
  );
}

/** The appointment card the customer keeps: the time huge, the date, the service and who is doing it. */
export function AppointmentCardDrawing({ paintIndex }: { paintIndex?: number }) {
  return (
    <div
      className={cx("relative overflow-hidden rounded-card bg-surface font-sans text-aubergine shadow-card", paintIndex !== undefined && "ls-paint")}
      style={paintIndex !== undefined ? paint(paintIndex) : undefined}
    >
      <span className="absolute inset-x-0 top-0 h-1.5 bg-rose" />
      <div className="px-5 pt-6 pb-4">
        <p className="truncate font-expanded text-xs font-bold">Kemi&apos;s Hair Studio</p>
        <p className="mt-3 font-expanded text-[3.25rem] leading-[0.95] font-extrabold tnum">10:30</p>
        <p className="mt-1.5 font-expanded text-sm font-semibold">Saturday 14 November</p>
      </div>
      <div className="mx-5 border-t-2 border-dashed border-line" />
      <div className="space-y-2 px-5 pt-3 pb-5">
        <div>
          <p className="text-xs font-semibold">Knotless braids, medium</p>
          <p className={`${tiny} text-muted`}>3 h</p>
        </div>
        <p className={`flex items-center gap-2 ${tiny}`}>
          <span className="flex h-6 w-6 items-center justify-center rounded-full bg-mist-2 text-[0.625rem] font-semibold text-slate-700">T</span>
          <span>
            with <span className="font-semibold">Tunde</span>
          </span>
        </p>
        <p className="flex items-center gap-2 text-xs font-semibold text-mint-dark">
          <span className="h-2 w-2 rounded-full bg-mint" />
          Confirmed, ₦5,000 deposit paid
        </p>
      </div>
    </div>
  );
}

/** The reminder the business sends in one tap: BookLink writes it, WhatsApp on the owner's phone sends it. */
export function ReminderDrawing({ paintIndex }: { paintIndex?: number }) {
  return (
    <div
      className={cx("rounded-[14px] bg-surface px-3 pt-2.5 pb-3 font-sans text-aubergine shadow-card", paintIndex !== undefined && "ls-paint")}
      style={paintIndex !== undefined ? paint(paintIndex) : undefined}
    >
      <p className={`flex items-center gap-1.5 ${tiny} font-semibold text-slate-700`}>
        <WhatsAppIcon className="h-3.5 w-3.5 text-[#147a3b]" />
        Kemi&apos;s Hair Studio
        <span className="ml-auto font-normal text-muted tnum">Fri 18:04</span>
      </p>
      <p className={`mt-1.5 rounded-[10px] rounded-tl-[3px] bg-mint-wash px-2.5 py-2 ${tiny}`}>
        Hi Ada, this is a reminder of your Knotless braids, medium appointment at Kemi&apos;s Hair Studio on Saturday 14 November at
        10:30. Manage your booking: <span className="font-semibold text-mint-dark underline">your booking link</span>
      </p>
    </div>
  );
}

/** A panel rising from the floor of a feature stage, like the phone on the hero. */
function Rising({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className={cx("mx-auto w-full max-w-[17.5rem] rounded-t-[12px] border border-b-0 border-line bg-surface px-4 pt-4 pb-6 font-sans text-aubergine", className)}>
      {children}
    </div>
  );
}

/** The slot picker: only free times show, grouped by part of the day; a taken time is gone, not greyed. */
export function SlotsDrawing() {
  return (
    <Rising>
      <p className="font-expanded text-sm font-bold">Saturday 14 November</p>
      <p className="mt-2 text-[0.625rem] font-medium text-muted">Morning</p>
      <div className="mt-1 grid grid-cols-3 gap-1.5">
        <Chip>09:30</Chip>
        <Chip on>10:30</Chip>
        <Chip>11:00</Chip>
      </div>
      <p className="mt-2.5 text-[0.625rem] font-medium text-muted">Afternoon</p>
      <div className="mt-1 grid grid-cols-3 gap-1.5">
        <Chip>12:30</Chip>
        <Chip>14:00</Chip>
        <Chip>15:30</Chip>
      </div>
      <p className="mt-2.5 text-[0.625rem] text-muted">Times are in Lagos time.</p>
    </Rising>
  );
}

/** The last step: the customer's details, what the deposit covers, and the bar that takes them to pay. */
export function DepositDrawing() {
  return (
    <Rising className="px-0 pb-0">
      <div className="space-y-2 px-4">
        <div>
          <p className={`${tiny} font-medium text-slate-700`}>Your name</p>
          <p className="mt-0.5 rounded-[8px] border border-field px-2.5 py-1.5 text-xs">Ada Okafor</p>
        </div>
        <div>
          <p className={`${tiny} font-medium text-slate-700`}>Phone number</p>
          <p className="mt-0.5 rounded-[8px] border border-field px-2.5 py-1.5 text-xs tnum">0803 123 4567</p>
        </div>
        <p className={`${tiny} text-slate-700`}>A ₦5,000 deposit holds your time. You pay the rest, ₦20,000, at Kemi&apos;s Hair Studio.</p>
      </div>
      <div className="mt-3 bg-surface px-4 pt-2.5 pb-6 shadow-bar">
        <p className="rounded-[10px] bg-rose py-2.5 text-center text-xs font-semibold text-white">Pay ₦5,000 deposit and book</p>
      </div>
    </Rising>
  );
}

/** The booking panel in the app: the booking at a glance, and WhatsApp opening the ready-written reminder. */
export function BookingPanelDrawing() {
  const rows = [
    ["Service", "Knotless braids, medium"],
    ["With", "Tunde"],
    ["Price", "₦25,000"],
    ["Paid", "₦5,000"],
  ];
  return (
    <Rising>
      <p className="font-expanded text-sm font-bold tnum">Sat 14 Nov, 10:30</p>
      <p className="text-xs font-semibold">Ada Okafor</p>
      <dl className={`mt-2 space-y-1 ${tiny}`}>
        {rows.map(([k, v]) => (
          <div key={k} className="flex justify-between gap-3">
            <dt className="text-muted">{k}</dt>
            <dd className="font-medium tnum">{v}</dd>
          </div>
        ))}
      </dl>
      <div className="mt-3 flex gap-1.5">
        <span className={`flex items-center gap-1 rounded-[8px] border border-line px-2.5 py-1.5 ${tiny} font-semibold`}>
          <PhoneIcon className="h-3 w-3" /> Call
        </span>
        <span className={`flex items-center gap-1 rounded-[8px] border border-rose bg-rose-wash px-2.5 py-1.5 ${tiny} font-semibold`}>
          <WhatsAppIcon className="h-3 w-3 text-[#147a3b]" /> WhatsApp
        </span>
      </div>
    </Rising>
  );
}

/** The calendar's day view: one column per person, each booking in that person's colour. */
export function CalendarDrawing() {
  const times = ["10:00", "10:30", "11:00", "11:30"];
  const block = "absolute inset-x-1 overflow-hidden rounded-[6px] border-l-[3px] px-1.5 py-1 text-[0.625rem] leading-tight";
  return (
    <div className="mx-auto w-full max-w-[18rem] overflow-hidden rounded-t-[12px] border border-b-0 border-line bg-surface font-sans text-aubergine">
      <div className="grid grid-cols-[2.5rem_1fr_1fr] bg-aubergine text-[0.6875rem] font-semibold text-white">
        <span />
        <span className="flex items-center gap-1.5 border-l border-white/10 px-2 py-1.5">
          <span className="h-2 w-2 rounded-full bg-[#C2255C]" />
          Tunde
        </span>
        <span className="flex items-center gap-1.5 border-l border-white/10 px-2 py-1.5">
          <span className="h-2 w-2 rounded-full bg-[#2F8F6E]" />
          Bisi
        </span>
      </div>
      <div className="relative grid grid-cols-[2.5rem_1fr_1fr] pb-4">
        <div>
          {times.map((t) => (
            <p key={t} className="h-8 pr-1.5 text-right text-[0.5625rem] text-muted tnum">
              {t}
            </p>
          ))}
        </div>
        <div className="relative border-l border-line">
          {times.map((t) => (
            <div key={t} className="h-8 border-t border-line-soft" />
          ))}
          <div className={block} style={{ top: "2rem", height: "8.5rem", background: "rgba(194, 37, 92, 0.16)", borderLeftColor: "#C2255C" }}>
            <span className="block font-semibold tnum">10:30</span>
            <span className="block truncate font-semibold">Ada Okafor</span>
            <span className="block truncate">Knotless braids</span>
          </div>
        </div>
        <div className="relative border-l border-line">
          {times.map((t) => (
            <div key={t} className="h-8 border-t border-line-soft" />
          ))}
          <div className={block} style={{ top: "0", height: "3.5rem", background: "rgba(47, 143, 110, 0.16)", borderLeftColor: "#2F8F6E" }}>
            <span className="block font-semibold tnum">10:00</span>
            <span className="block truncate font-semibold">Chioma Eze</span>
            <span className="block truncate">Wash and set</span>
          </div>
          <div
            className={cx(block, "border border-l-[3px] border-dashed border-amber")}
            style={{ top: "6rem", height: "3.5rem", background: "rgba(47, 143, 110, 0.08)", borderLeftColor: "#2F8F6E" }}
          >
            <span className="block font-semibold tnum">11:30</span>
            <span className="block truncate font-semibold">Zainab Bello</span>
            <span className="block truncate text-amber-text">Pending deposit</span>
          </div>
        </div>
      </div>
    </div>
  );
}
