import type { CSSProperties, ReactNode } from "react";
import { AwardIcon, CheckIcon, FileIcon, GripIcon, PlayIcon, PlusIcon, QuizIcon, SearchIcon } from "./icons";
import { LearningPath, type PathSection } from "./learning-path";
import { PhoneFrame } from "./suite/phone";
import { cx } from "./suite/ui";

/*
 * LearnLink's real screens, redrawn in code for the tutors' landing (/teach). Each mirrors a screen in this app, in the
 * app's own faces (Figtree, Literata) and palette:
 * - the course builder: studio/courses/[id] (curriculum.tsx);
 * - the learner's course: the learning path from components/learning-path.tsx, rendered by the real component;
 * - the certificate: (site)/certificates/[code];
 * - the academy page, the quiz result, the marketplace card and the earnings page.
 * Drawings are decorative (aria-hidden) and carry no focusable element; each frame's label says what it shows.
 * Names and naira amounts are a tutor's own sample figures, never LinkSuite plan prices.
 */

const tiny = "text-[0.6875rem] leading-snug";
const paint = (i: number) => ({ "--ls-i": i }) as CSSProperties;

const COURSE = "JAMB Physics, the full syllabus";
const ACADEMY = "Tolu's Physics Academy";

const PATH: PathSection[] = [
  {
    id: "s1",
    title: "Mechanics",
    lessons: [
      { id: "l1", title: "Scalars and vectors", type: "Video", durationSec: 840, done: true },
      { id: "l2", title: "Equations of motion", type: "Video", durationSec: 1260, done: true },
      { id: "l3", title: "Mechanics checkpoint", type: "Quiz", isQuiz: true, passed: true },
    ],
  },
  {
    id: "s2",
    title: "Waves and optics",
    lessons: [
      { id: "l4", title: "Reflection and refraction", type: "Video", durationSec: 960 },
      { id: "l5", title: "Formula sheet", type: "File" },
      { id: "l6", title: "Waves checkpoint", type: "Quiz", isQuiz: true },
    ],
  },
];

/** A drawn screen that is not a phone: a labelled image whose drawing is hidden from assistive technology. */
function Drawn({ label, className, style, children }: { label: string; className?: string; style?: CSSProperties; children: ReactNode }) {
  return (
    <div role="img" aria-label={label} className={className} style={style}>
      <div aria-hidden="true" className="h-full">
        {children}
      </div>
    </div>
  );
}

/** The studio's course builder, on the Curriculum tab. */
function BuilderScreen() {
  const lessons = [
    { t: "Reflection and refraction", m: "Video, 16 min", icon: PlayIcon, preview: true },
    { t: "Formula sheet", m: "Download", icon: FileIcon },
    { t: "Waves checkpoint", m: "Quiz, 10 questions", icon: QuizIcon },
  ];
  return (
    <div className="ls-paint rounded-[14px] border border-[#dce3e3] bg-white font-sans text-[#10202b]" style={paint(0)}>
      <div className="px-4 pt-3.5 pb-2">
        <p className={`${tiny} text-[#4e626c]`}>Studio, courses</p>
        <p className="font-serif text-[0.95rem] leading-tight font-semibold">{COURSE}</p>
        <p className="mt-1.5 flex flex-wrap items-center gap-1.5">
          <span className={`rounded-full bg-[#eaf3e2] px-2 py-0.5 ${tiny} font-semibold text-[#437520]`}>Published</span>
          <span className={`rounded-full bg-[rgba(11,110,110,0.09)] px-2 py-0.5 ${tiny} font-semibold text-[#0b6e6e]`}>On the marketplace</span>
          <span className={`${tiny} text-[#4e626c]`}>12 lessons</span>
        </p>
      </div>
      <div className={`flex gap-3 border-b border-[#dce3e3] px-4 ${tiny} font-medium`}>
        <span className="relative pb-1.5 text-[#10202b]">
          Curriculum
          <span className="absolute inset-x-0 -bottom-px h-[3px] rounded-full bg-[#0b6e6e]" />
        </span>
        <span className="pb-1.5 text-[#4e626c]">Details</span>
        <span className="pb-1.5 text-[#4e626c]">Publishing</span>
      </div>
      <div className="p-3">
        <div className="overflow-hidden rounded-[10px] border border-[#dce3e3]">
          <div className="flex items-center gap-2 border-b border-[#e8eeee] bg-[#f7f9f9] px-2.5 py-1.5">
            <GripIcon className="h-3.5 w-3.5 text-[#82949b]" />
            <span>
              <span className="block text-[0.625rem] text-[#4e626c]">Section 2</span>
              <span className="block text-xs font-semibold">Waves and optics</span>
            </span>
          </div>
          <ul className="divide-y divide-[#e8eeee]">
            {lessons.map((l) => (
              <li key={l.t} className="flex items-center gap-2 px-2.5 py-1.5">
                <GripIcon className="h-3.5 w-3.5 shrink-0 text-[#82949b]" />
                <l.icon className="h-3.5 w-3.5 shrink-0 text-[#4e626c]" />
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-serif text-[0.78rem]">{l.t}</span>
                  <span className={`flex items-center gap-1.5 text-[0.625rem] text-[#4e626c]`}>
                    {l.m}
                    {l.preview && <span className="rounded-full bg-[rgba(11,110,110,0.09)] px-1.5 font-semibold text-[#0b6e6e]">Free preview</span>}
                  </span>
                </span>
              </li>
            ))}
          </ul>
        </div>
        <p className="mt-1 flex items-center gap-1 px-1 py-1 text-[0.6875rem] font-semibold text-[#0b6e6e]">
          <PlusIcon className="h-3.5 w-3.5" /> Add section
        </p>
      </div>
    </div>
  );
}

/** A learner's course on their phone: the learning path, lime up to where they've reached. */
function LearnerScreen() {
  return (
    <div className="ls-paint bg-[#f3f6f6] pb-6 font-sans text-[#10202b]" style={paint(1)}>
      <div className="bg-white px-4 pt-8 pb-3 shadow-[0_1px_0_#dce3e3]">
        <p className={`${tiny} text-[#4e626c]`}>{ACADEMY}</p>
        <p className="font-serif text-[0.9rem] leading-tight font-semibold">{COURSE}</p>
        <div className="mt-2.5 flex items-center gap-2">
          <span className="h-1.5 flex-1 overflow-hidden rounded-full bg-[#e7eded]">
            <span className="block h-full w-1/2 rounded-full bg-[#5e9e2f]" />
          </span>
          <span className="tnum text-[0.6875rem] font-semibold text-[#437520]">50%</span>
        </div>
      </div>
      <div className="px-3 pt-3">
        <div className="rounded-[12px] border border-[#dce3e3] bg-white px-2.5 py-3">
          <LearningPath sections={PATH} currentId="l4" numberSections={false} certificate={{ earned: false, label: "Certificate of completion" }} />
        </div>
      </div>
    </div>
  );
}

/** The public certificate page: anyone with the code can check it. */
function CertificateScreen() {
  return (
    <div className="ls-paint rounded-[14px] border border-[#dce3e3] bg-white px-4 pt-3.5 pb-4 font-sans text-[#10202b]" style={paint(2)}>
      <p className={`flex items-center gap-1.5 ${tiny} font-semibold text-[#437520]`}>
        <span className="flex h-4 w-4 items-center justify-center rounded-full bg-[#5e9e2f] text-white">
          <AwardIcon className="h-2.5 w-2.5" />
        </span>
        Verified certificate
      </p>
      <p className={`mt-2.5 ${tiny} text-[#4e626c]`}>This certifies that</p>
      <p className="font-serif text-[1.15rem] leading-tight font-semibold">Chiamaka Obi</p>
      <p className={`mt-1.5 ${tiny} text-[#4e626c]`}>completed the course</p>
      <p className="font-serif text-[0.85rem] leading-snug font-semibold">{COURSE}</p>
      <div className={`mt-3 grid grid-cols-2 gap-2 border-t border-[#dce3e3] pt-2 ${tiny}`}>
        <span>
          <span className="block text-[0.625rem] text-[#4e626c]">Taught by</span>
          <span className="block font-semibold">Tolu Adeyemi</span>
        </span>
        <span>
          <span className="block text-[0.625rem] text-[#4e626c]">Issued</span>
          <span className="block font-semibold">12 October 2026</span>
        </span>
      </div>
    </div>
  );
}

/**
 * The hero: what a tutor builds, what the learner sees, and how it ends. The sheen passes over the three in that order.
 * On phones the learner's path stands alone.
 */
export function TeachHeroVisual() {
  return (
    <div className="relative h-[26rem] overflow-hidden rounded-ls-panel bg-app-wash">
      <Drawn
        label={`The LearnLink studio: building "${COURSE}", with lessons, a downloadable formula sheet and a quiz in the Waves and optics section.`}
        className="absolute top-6 left-5 hidden w-[19.5rem] sm:block lg:left-6"
      >
        <BuilderScreen />
      </Drawn>
      <div className="absolute top-8 left-1/2 -translate-x-1/2 sm:right-6 sm:left-auto sm:translate-x-0 lg:right-8">
        <PhoneFrame label={`LearnLink on a learner's phone: ${COURSE}, half done, with the next lesson ringed and the certificate at the end of the path.`}>
          <LearnerScreen />
        </PhoneFrame>
      </div>
      <Drawn
        label={`A verified LearnLink certificate: Chiamaka Obi completed "${COURSE}", taught by Tolu Adeyemi.`}
        className="absolute bottom-5 left-12 hidden w-[16.5rem] sm:block lg:left-16"
      >
        <CertificateScreen />
      </Drawn>
    </div>
  );
}

/* ---------- Feature crops: each rises from the floor of its stage ---------- */

/** Sized to the feature stage's inner height, so a tall screen is cropped at the bottom, never the top. */
const crop = "h-[12.5rem] w-[18.5rem] max-w-full overflow-hidden rounded-t-[14px] border border-b-0 border-[#dce3e3] bg-white font-sans text-[#10202b]";

function Thumb({ title, tone }: { title: string; tone: string }) {
  return (
    <span className="flex aspect-video items-end rounded-[8px] p-2" style={{ background: tone }}>
      <span className="line-clamp-2 font-serif text-[0.6875rem] leading-snug font-semibold text-white/90">{title}</span>
    </span>
  );
}

export function AcademyVisual() {
  const courses = [
    { t: COURSE, price: "₦15,000", tone: "#1a2e3b" },
    { t: "Physics past questions, 2015 to 2025", price: "Free", tone: "#24404f" },
  ];
  return (
    <Drawn label={`An academy page on LearnLink: ${ACADEMY}, with its courses and their prices.`} className={crop}>
      <div className="bg-[#0b6e6e] px-4 pt-4 pb-3.5 text-white">
        <div className="flex items-center gap-2.5">
          <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full border-2 border-white/60 text-xs font-bold">TP</span>
          <span>
            <span className="block font-serif text-[0.95rem] leading-tight font-semibold">{ACADEMY}</span>
            <span className={`block ${tiny} opacity-85`}>4 courses</span>
          </span>
        </div>
      </div>
      <div className="grid grid-cols-2 gap-2.5 p-3">
        {courses.map((c) => (
          <span key={c.t} className="block">
            <Thumb title={c.t} tone={c.tone} />
            <span className={cx("tnum mt-1.5 block text-[0.6875rem] font-bold", c.price === "Free" ? "text-[#437520]" : "text-[#10202b]")}>{c.price}</span>
          </span>
        ))}
      </div>
    </Drawn>
  );
}

export function QuizVisual() {
  return (
    <Drawn label="A quiz result in LearnLink: passed with 80%, 8 of 10 right, against a pass mark of 70%." className={cx(crop, "px-3 pt-3")}>
      <div className="rounded-[10px] border-l-[3px] border-[#5e9e2f] bg-[#eaf3e2] px-3 py-2.5">
        <p className="text-[0.8125rem] font-semibold text-[#437520]">Passed</p>
        <p className={`${tiny} text-[#384c57]`}>
          You scored <span className="tnum font-bold text-[#10202b]">80%</span> (8 of 10 right). The pass mark is 70%.
        </p>
      </div>
      <div className="mt-2.5 flex items-start gap-2.5 rounded-[10px] border border-[#dce3e3] px-3 py-2.5">
        <span className="mt-0.5 flex h-4 w-4 shrink-0 items-center justify-center rounded-full bg-[#5e9e2f] text-white">
          <CheckIcon className="h-2.5 w-2.5" strokeWidth={3} />
        </span>
        <span className="min-w-0">
          <span className="block text-[0.625rem] text-[#4e626c]">Question 3: correct</span>
          <span className="block font-serif text-[0.75rem] leading-snug">A car starts from rest and speeds up at 2 m/s² for 5 s. How far does it go?</span>
          <span className={`mt-1 block ${tiny} text-[#384c57]`}>
            Your answer: <span className="font-medium">25 m</span>
          </span>
        </span>
      </div>
    </Drawn>
  );
}

export function MarketplaceVisual() {
  return (
    <Drawn label={`The LearnLink marketplace: a search for JAMB physics finds "${COURSE}" from ${ACADEMY}.`} className={cx(crop, "px-3 pt-3")}>
      <span className="flex items-center gap-2 rounded-[10px] border border-[#7f9294] px-2.5 py-1.5 text-[0.75rem]">
        <SearchIcon className="h-3.5 w-3.5 text-[#4e626c]" />
        JAMB physics
      </span>
      <p className={`mt-2 ${tiny} text-[#4e626c]`}>3 courses</p>
      <span className="mt-1.5 grid grid-cols-[7rem_minmax(0,1fr)] gap-2.5 rounded-[12px] border border-[#dce3e3] p-2">
        <Thumb title={COURSE} tone="#1a2e3b" />
        <span className="min-w-0">
          <span className="line-clamp-2 block font-serif text-[0.75rem] leading-snug font-semibold">{COURSE}</span>
          <span className="mt-0.5 block truncate text-[0.625rem] text-[#4e626c]">{ACADEMY}</span>
          <span className="mt-1 flex items-baseline justify-between gap-1">
            <span className="tnum text-[0.75rem] font-bold">₦15,000</span>
            <span className="text-[0.625rem] text-[#4e626c]">24 lessons</span>
          </span>
        </span>
      </span>
    </Drawn>
  );
}

export function EarningsVisual() {
  return (
    <Drawn label="The earnings page in the LearnLink studio: money available to pay out, money still clearing, and a marketplace sale with its fee." className={crop}>
      <div className="grid grid-cols-2 border-b border-[#dce3e3]">
        <span className="border-r border-[#dce3e3] px-3 py-2.5">
          <span className="block text-[0.6875rem] text-[#4e626c]">Available</span>
          <span className="tnum block text-[1.05rem] font-bold text-[#437520]">₦184,500</span>
        </span>
        <span className="px-3 py-2.5">
          <span className="block text-[0.6875rem] text-[#4e626c]">Pending</span>
          <span className="tnum block text-[1.05rem] font-bold">₦42,000</span>
          <span className="block text-[0.625rem] text-[#4e626c]">Clears 7 days after each sale</span>
        </span>
      </div>
      <div className="px-3 py-2.5">
        <p className="font-serif text-[0.75rem] leading-snug">{COURSE}</p>
        <p className="text-[0.625rem] text-[#4e626c]">Marketplace sale</p>
        <dl className="tnum mt-1.5 grid grid-cols-3 gap-1 text-[0.625rem]">
          <div>
            <dt className="text-[#4e626c]">Price</dt>
            <dd className="font-semibold">₦15,000</dd>
          </div>
          <div>
            <dt className="text-[#4e626c]">Fee</dt>
            <dd className="font-semibold">₦4,500</dd>
          </div>
          <div>
            <dt className="text-[#4e626c]">You earn</dt>
            <dd className="font-semibold text-[#437520]">₦10,500</dd>
          </div>
        </dl>
        <span className="mt-2.5 flex min-h-8 items-center justify-center rounded-[10px] bg-[#0b6e6e] text-[0.6875rem] font-semibold text-white">Request payout</span>
      </div>
    </Drawn>
  );
}
