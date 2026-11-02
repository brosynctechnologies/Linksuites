import Link from "next/link";
import { AppointmentCard } from "@/components/appointment-card";
import { BellIcon, CheckIcon, ShieldIcon, WhatsAppIcon } from "@/components/icons";
import { buttonClass, cx } from "@/components/styles";
import { APP_NAME } from "@/lib/config";

const PLANS = [
  { name: "Free", price: "₦0", staff: "1", bookings: "50 a month", deposits: false, branding: "BookLink note shown", seats: "1" },
  { name: "Solo", price: "₦5,000", staff: "1", bookings: "Unlimited", deposits: true, branding: "Removed", seats: "2" },
  { name: "Team", price: "₦12,000", staff: "Up to 5", bookings: "Unlimited", deposits: true, branding: "Removed", seats: "6" },
  { name: "Business", price: "₦25,000", staff: "Unlimited", bookings: "Unlimited", deposits: true, branding: "Removed", seats: "15" },
];

const STEPS = [
  { title: "Add your services and hours", text: "Name each service, how long it takes, the price and any deposit. Set who works when, breaks included." },
  { title: "Put your link in your bio", text: "Your page gets its own short link with your business name in it. Add it to Instagram, WhatsApp status or your website." },
  { title: "Customers book and pay the deposit", text: "They pick a service, a stylist and a free time, then pay the deposit by card or transfer through Paystack." },
  { title: "Run the day from the calendar", text: "Every chair in one view. Mark people completed or no-show, take walk-ins and phone bookings in the same diary." },
];

export default function LandingPage() {
  return (
    <div className="bg-mist">
      <header className="mx-auto flex max-w-6xl items-center justify-between gap-4 px-4 py-5 sm:px-6">
        <Link href="/" className="font-expanded text-xl font-extrabold">
          {APP_NAME}
        </Link>
        <nav className="flex items-center gap-2" aria-label="Account">
          <Link href="/login" className={buttonClass("ghost")}>
            Log in
          </Link>
          <Link href="/signup" className={buttonClass("primary", "hidden sm:inline-flex")}>
            Create your booking page
          </Link>
        </nav>
      </header>

      <main>
        {/* Hero: the characteristic moment, a booking becoming an appointment card */}
        <section className="mx-auto grid max-w-6xl items-center gap-12 px-4 pt-8 pb-20 sm:px-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.05fr)] lg:pt-14">
          <div>
            <h1 className="font-expanded text-[clamp(2.25rem,6vw,3.5rem)] leading-[1.02] font-extrabold tracking-[-0.02em]">
              Customers book the chair. You keep the deposit if they don&apos;t show.
            </h1>
            <p className="mt-5 max-w-xl text-lg text-slate-700">
              A booking page for salons, barbers, braiders, make-up artists and studios. Customers choose a service and a time from your Instagram bio in under a
              minute, pay a deposit, and get a card that tells them exactly when to come.
            </p>
            <div className="mt-8 flex flex-wrap gap-3">
              <Link href="/signup" className={buttonClass("primary", "min-h-12 px-6 text-base")}>
                Create your free booking page
              </Link>
              <a href="#how-it-works" className={buttonClass("secondary", "min-h-12 px-6 text-base")}>
                See how booking works
              </a>
            </div>
            <p className="mt-4 text-sm text-muted">Free for 50 bookings a month. No card needed.</p>
          </div>

          <div className="relative mx-auto flex w-full max-w-[34rem] justify-center lg:justify-start" aria-label="Example: a customer books Saturday 10:30 with Tunde">
            <PhoneMock />
            <div className="absolute right-0 -bottom-10 w-[15.5rem] rotate-[3deg] sm:right-2 sm:w-[17rem]">
              <AppointmentCard
                businessName="Kemi's Hair Studio"
                startsAt="2028-10-14T10:30:00+01:00"
                timezone="Africa/Lagos"
                dateText="Saturday 14 October"
                serviceName="Knotless braids, medium"
                durationMinutes={180}
                staffName="Tunde"
                status="Confirmed"
              />
            </div>
          </div>
        </section>

        {/* How it works: a real sequence, so it's numbered */}
        <section id="how-it-works" className="scroll-mt-6 border-y border-line bg-surface">
          <div className="mx-auto max-w-6xl px-4 py-16 sm:px-6">
            <h2 className="text-2xl font-bold">How it works</h2>
            <ol className="mt-8 grid gap-8 sm:grid-cols-2 lg:grid-cols-4">
              {STEPS.map((s, i) => (
                <li key={s.title}>
                  <span className="font-expanded text-4xl font-extrabold text-rose tnum">{i + 1}</span>
                  <h3 className="mt-2 font-bold">{s.title}</h3>
                  <p className="mt-1 text-sm text-slate-700">{s.text}</p>
                </li>
              ))}
            </ol>
          </div>
        </section>

        {/* No-show protection */}
        <section className="mx-auto max-w-6xl px-4 py-16 sm:px-6">
          <h2 className="max-w-2xl text-2xl font-bold">An empty chair at 10:30 costs you the morning. Two things stop it.</h2>
          <div className="mt-8 grid gap-10 md:grid-cols-2">
            <div className="flex gap-4">
              <ShieldIcon className="h-7 w-7 shrink-0 text-rose" />
              <div>
                <h3 className="font-bold">Deposits that go straight to you</h3>
                <p className="mt-1 text-slate-700">
                  Set a fixed amount or a percentage per service. The time is held for 10 minutes while the customer pays, and the money lands in your own Paystack
                  account. BookLink never holds it.
                </p>
              </div>
            </div>
            <div className="flex gap-4">
              <BellIcon className="h-7 w-7 shrink-0 text-rose" />
              <div>
                <h3 className="font-bold">Reminders before every appointment</h3>
                <p className="mt-1 text-slate-700">
                  Customers get an email reminder automatically. For the ones who live on WhatsApp, one tap opens a ready-written reminder to send from your phone.
                </p>
                <p className="mt-3 inline-flex items-center gap-2 text-sm font-semibold text-mint-dark">
                  <WhatsAppIcon className="h-4 w-4" /> Remind on WhatsApp
                </p>
              </div>
            </div>
          </div>
          <p className="mt-8 max-w-2xl text-sm text-slate-700">
            Customers can move or cancel themselves until your cutoff, so the slot goes back on sale instead of sitting empty. Every customer record keeps a
            no-show count, so you know who to ask for a deposit.
          </p>
        </section>

        {/* Pricing */}
        <section id="pricing" className="border-t border-line bg-surface">
          <div className="mx-auto max-w-6xl px-4 py-16 sm:px-6">
            <h2 className="text-2xl font-bold">Pricing</h2>
            <p className="mt-1 text-slate-700">Monthly, in naira. Change or cancel any time.</p>
            <div className="mt-8 overflow-x-auto">
              <table className="w-full min-w-[40rem] text-left text-sm">
                <thead>
                  <tr className="border-b border-line">
                    <th className="py-3 pr-4 font-medium text-muted">
                      <span className="sr-only">Feature</span>
                    </th>
                    {PLANS.map((p) => (
                      <th key={p.name} className="px-3 py-3 align-bottom">
                        <span className="block text-base font-bold">{p.name}</span>
                        <span className="font-expanded text-2xl font-extrabold tnum">{p.price}</span>
                        <span className="text-muted"> /month</span>
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-line-soft">
                  <Row label="Staff" values={PLANS.map((p) => p.staff)} />
                  <Row label="Bookings" values={PLANS.map((p) => p.bookings)} />
                  <Row label="Deposits and email reminders" values={PLANS.map((p) => p.deposits)} />
                  <Row label="BookLink branding" values={PLANS.map((p) => p.branding)} />
                  <Row label="Team logins" values={PLANS.map((p) => p.seats)} />
                </tbody>
              </table>
            </div>
            <Link href="/signup" className={buttonClass("primary", "mt-8 min-h-12 px-6 text-base")}>
              Start free
            </Link>
          </div>
        </section>
      </main>

      <footer className="bg-aubergine text-on-aubergine">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-4 px-4 py-8 text-sm sm:px-6">
          <p>
            <span className="font-expanded font-extrabold text-white">{APP_NAME}</span>, online booking for salons, barbers and studios.
          </p>
          <nav className="flex gap-5" aria-label="Footer">
            <Link href="/signup" className="hover:text-white">
              Sign up
            </Link>
            <Link href="/login" className="hover:text-white">
              Log in
            </Link>
            <a href="#pricing" className="hover:text-white">
              Pricing
            </a>
          </nav>
        </div>
      </footer>
    </div>
  );
}

function Row({ label, values }: { label: string; values: (string | boolean)[] }) {
  return (
    <tr>
      <th scope="row" className="py-3 pr-4 font-medium text-slate-700">
        {label}
      </th>
      {values.map((v, i) => (
        <td key={i} className="px-3 py-3">
          {typeof v === "boolean" ? (
            v ? (
              <>
                <CheckIcon className="h-4 w-4 text-mint" />
                <span className="sr-only">Included</span>
              </>
            ) : (
              <span className="text-muted">Not included</span>
            )
          ) : (
            v
          )}
        </td>
      ))}
    </tr>
  );
}

/** A phone-sized booking flow, built in HTML: service chosen, Saturday picked, 10:30 selected. */
function PhoneMock() {
  const days = [
    { d: "Thu", n: 12, on: false, free: true },
    { d: "Fri", n: 13, on: false, free: false },
    { d: "Sat", n: 14, on: true, free: true },
    { d: "Sun", n: 15, on: false, free: false },
    { d: "Mon", n: 16, on: false, free: true },
  ];
  const times = ["09:00", "09:30", "10:30", "11:00", "12:30", "13:00"];
  return (
    <div className="w-[17rem] rounded-[2.25rem] bg-aubergine p-2.5 shadow-float sm:w-[18.5rem]" aria-hidden="true">
      <div className="overflow-hidden rounded-[1.8rem] bg-mist">
        <div className="h-14 bg-rose" />
        <div className="px-4 pb-4">
          <div className="-mt-6 flex h-12 w-12 items-center justify-center rounded-full border-4 border-mist bg-surface font-expanded text-sm font-extrabold text-rose">KH</div>
          <p className="mt-1.5 font-expanded text-base leading-tight font-extrabold">Kemi&apos;s Hair Studio</p>
          <p className="text-[0.6875rem] text-muted">Lekki Phase 1, 4.9 from 212 reviews</p>

          <div className="mt-3 rounded-[10px] border border-line bg-surface px-3 py-2">
            <p className="text-[0.6875rem] text-muted">1 Service</p>
            <div className="flex items-baseline justify-between gap-2">
              <p className="text-xs font-semibold">Knotless braids, medium</p>
              <p className="text-xs font-semibold tnum">₦25,000</p>
            </div>
            <p className="text-[0.6875rem] text-muted">3 h, ₦5,000 deposit</p>
          </div>

          <div className="mt-2 rounded-[10px] border border-line bg-surface px-3 py-2">
            <p className="text-[0.6875rem] text-muted">3 When</p>
            <div className="mt-1.5 grid grid-cols-5 gap-1">
              {days.map((x) => (
                <div
                  key={x.n}
                  className={cx(
                    "flex flex-col items-center rounded-[8px] border py-1",
                    x.on ? "border-rose bg-rose text-white" : x.free ? "border-line" : "border-transparent text-faint",
                  )}
                >
                  <span className="text-[0.625rem]">{x.d}</span>
                  <span className="font-expanded text-sm font-bold tnum">{x.n}</span>
                </div>
              ))}
            </div>
            <p className="mt-2 text-[0.625rem] text-muted">Morning</p>
            <div className="mt-1 grid grid-cols-3 gap-1">
              {times.map((t) => (
                <span
                  key={t}
                  className={cx("rounded-[8px] border py-1.5 text-center text-xs font-semibold tnum", t === "10:30" ? "border-rose bg-rose text-white" : "border-line")}
                >
                  {t}
                </span>
              ))}
            </div>
          </div>
        </div>
        <div className="bg-surface px-4 pt-2.5 pb-4 shadow-bar">
          <p className="truncate text-[0.6875rem] text-slate-700">Knotless braids, medium with Tunde</p>
          <p className="mt-1.5 rounded-[10px] bg-rose py-2.5 text-center text-xs font-semibold text-white">Pay ₦5,000 deposit and book</p>
        </div>
      </div>
    </div>
  );
}
