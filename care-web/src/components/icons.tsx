import type { SVGProps } from "react";

type P = SVGProps<SVGSVGElement>;

function Svg({ children, ...props }: P) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      width={20}
      height={20}
      aria-hidden="true"
      {...props}
    >
      {children}
    </svg>
  );
}

export const HomeIcon = (p: P) => (
  <Svg {...p}>
    <path d="M3 10.5 12 3l9 7.5V20a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z" />
  </Svg>
);
export const PatientsIcon = (p: P) => (
  <Svg {...p}>
    <circle cx="9" cy="8" r="3.5" />
    <path d="M2.5 20a6.5 6.5 0 0 1 13 0" />
    <path d="M16 4.5a3.5 3.5 0 0 1 0 7M18 14a6.5 6.5 0 0 1 3.5 6" />
  </Svg>
);
/** Sliders: settings. */
export const SettingsIcon = (p: P) => (
  <Svg {...p}>
    <path d="M4 21V10M4 6V3M12 21v-7M12 10V3M20 21v-5M20 12V3" />
    <path d="M2 8h4M10 12h4M18 14h4" />
  </Svg>
);
/** A checklist on a clipboard: setup. */
export const SetupIcon = (p: P) => (
  <Svg {...p}>
    <rect x="5" y="4" width="14" height="17" rx="2" />
    <path d="M9 4V3h6v1M8.5 10.5l1.5 1.5 3-3M8.5 16h7" />
  </Svg>
);
/** A staff ID badge on a lanyard. */
export const StaffIcon = (p: P) => (
  <Svg {...p}>
    <rect x="4" y="3" width="16" height="18" rx="2" />
    <circle cx="12" cy="10" r="3" />
    <path d="M7.5 18a4.5 4.5 0 0 1 9 0" />
  </Svg>
);
/** A price tag: the price list. */
export const PriceIcon = (p: P) => (
  <Svg {...p}>
    <path d="M3 12V4a1 1 0 0 1 1-1h8l9 9-9 9z" />
    <circle cx="8" cy="8" r="1.5" />
  </Svg>
);
/** A shield: protected clinical facts. */
export const ShieldIcon = (p: P) => (
  <Svg {...p}>
    <path d="M12 3 4.5 6v5.5c0 4.5 3.2 8.2 7.5 9.5 4.3-1.3 7.5-5 7.5-9.5V6z" />
    <path d="M12 9v6M9 12h6" />
  </Svg>
);
export const MoreIcon = (p: P) => (
  <Svg {...p}>
    <circle cx="5" cy="12" r="1" />
    <circle cx="12" cy="12" r="1" />
    <circle cx="19" cy="12" r="1" />
  </Svg>
);
export const AccountIcon = (p: P) => (
  <Svg {...p}>
    <circle cx="12" cy="8" r="4" />
    <path d="M4 21a8 8 0 0 1 16 0" />
  </Svg>
);
export const LogoutIcon = (p: P) => (
  <Svg {...p}>
    <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9" />
  </Svg>
);
export const BackIcon = (p: P) => (
  <Svg {...p}>
    <path d="M15 18l-6-6 6-6" />
  </Svg>
);
export const CloseIcon = (p: P) => (
  <Svg {...p}>
    <path d="M18 6 6 18M6 6l12 12" />
  </Svg>
);
export const SearchIcon = (p: P) => (
  <Svg {...p}>
    <circle cx="11" cy="11" r="7" />
    <path d="m20 20-3.5-3.5" />
  </Svg>
);
export const PlusIcon = (p: P) => (
  <Svg {...p}>
    <path d="M12 5v14M5 12h14" />
  </Svg>
);
export const CheckIcon = (p: P) => (
  <Svg {...p}>
    <path d="m5 12.5 4.5 4.5L19 7.5" />
  </Svg>
);
export const UploadIcon = (p: P) => (
  <Svg {...p}>
    <path d="M12 16V4M7 9l5-5 5 5M4 20h16" />
  </Svg>
);
export const DownloadIcon = (p: P) => (
  <Svg {...p}>
    <path d="M12 4v12M7 11l5 5 5-5M4 20h16" />
  </Svg>
);
export const CopyIcon = (p: P) => (
  <Svg {...p}>
    <rect x="9" y="9" width="13" height="13" rx="2" />
    <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
  </Svg>
);
export const WhatsAppIcon = (p: P) => (
  <svg viewBox="0 0 24 24" width={20} height={20} fill="currentColor" aria-hidden="true" {...p}>
    <path d="M17.5 14.4c-.3-.1-1.8-.9-2-1-.3-.1-.5-.1-.7.1l-.9 1.1c-.2.2-.3.2-.6.1-.3-.1-1.3-.5-2.4-1.5-.9-.8-1.5-1.8-1.7-2.1-.2-.3 0-.5.1-.6l.4-.5c.2-.2.2-.3.3-.5.1-.2 0-.4 0-.5l-.9-2.2c-.2-.6-.5-.5-.7-.5h-.6c-.2 0-.5.1-.8.4-.3.3-1 1-1 2.5s1.1 2.9 1.2 3.1c.1.2 2.1 3.2 5.1 4.5.7.3 1.3.5 1.7.6.7.2 1.4.2 1.9.1.6-.1 1.8-.7 2-1.4.2-.7.2-1.3.2-1.4-.1-.1-.3-.2-.6-.3zM12 21.8c-1.8 0-3.5-.5-5-1.4l-.4-.2-3.7 1 1-3.6-.2-.4A9.8 9.8 0 0 1 2.2 12 9.8 9.8 0 0 1 12 2.2a9.8 9.8 0 0 1 9.8 9.8A9.8 9.8 0 0 1 12 21.8zM12 0a12 12 0 0 0-10.3 18.1L0 24l6-1.6A12 12 0 1 0 12 0z" />
  </svg>
);
/** The queue: people waiting in line. */
export const QueueIcon = (p: P) => (
  <Svg {...p}>
    <circle cx="6" cy="7" r="2.5" />
    <circle cx="13" cy="7" r="2.5" />
    <path d="M2.5 19v-2.5A3.5 3.5 0 0 1 6 13h0a3.5 3.5 0 0 1 3.5 3.5V19M9.5 19v-2.5A3.5 3.5 0 0 1 13 13h0a3.5 3.5 0 0 1 3.5 3.5V19" />
    <path d="M19 9v6M17 12l2 3 2-3" />
  </Svg>
);
export const CalendarIcon = (p: P) => (
  <Svg {...p}>
    <rect x="3.5" y="5" width="17" height="15.5" rx="1.5" />
    <path d="M3.5 10h17M8 3v4M16 3v4" />
  </Svg>
);
/** An unsigned note: a sheet with a pen line. */
export const NoteIcon = (p: P) => (
  <Svg {...p}>
    <path d="M6 3h9l4 4v14H6z" />
    <path d="M14 3v5h5M9 13h6M9 17h3" />
  </Svg>
);
export const ChevronLeftIcon = (p: P) => (
  <Svg {...p}>
    <path d="m15 5-7 7 7 7" />
  </Svg>
);
export const ChevronRightIcon = (p: P) => (
  <Svg {...p}>
    <path d="m9 5 7 7-7 7" />
  </Svg>
);
/** A ward bed: a frame, a pillow and a mattress line. */
export const BedIcon = (p: P) => (
  <Svg {...p}>
    <path d="M3 6v13M3 15h18v4M21 15v-3a3 3 0 0 0-3-3h-7v6" />
    <rect x="5.5" y="10" width="4" height="3" rx="1" />
  </Svg>
);
/** An admitted patient: a person beside a ward bed. */
export const WardIcon = (p: P) => (
  <Svg {...p}>
    <circle cx="7" cy="6" r="2.5" />
    <path d="M3 21v-6a4 4 0 0 1 8 0v6M13 13h8v8M13 21v-8M15 13v-2h4v2" />
  </Svg>
);
/** A bill: a torn-off sheet with lines and a total. */
export const BillIcon = (p: P) => (
  <Svg {...p}>
    <path d="M6 3h12v18l-2-1.5-2 1.5-2-1.5-2 1.5-2-1.5L6 21z" />
    <path d="M9 8h6M9 12h6M9 16h3" />
  </Svg>
);
/** Reports: three bars on a baseline. */
export const ReportIcon = (p: P) => (
  <Svg {...p}>
    <path d="M4 20h16M7 16v-5M12 16V7M17 16v-8" />
  </Svg>
);
