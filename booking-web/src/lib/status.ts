import type { BookingSource, BookingStatus, PaymentMethod, PaymentType } from "./types";

export type Tone = "neutral" | "rose" | "green" | "amber" | "red" | "void";

/** Status is always a colour paired with a word. */
export const STATUS_LABEL: Record<BookingStatus, string> = {
  Pending: "Pending deposit",
  Confirmed: "Confirmed",
  Completed: "Completed",
  Cancelled: "Cancelled",
  NoShow: "No-show",
  Expired: "Hold expired",
};

export const STATUS_TONE: Record<BookingStatus, Tone> = {
  Pending: "amber",
  Confirmed: "green",
  Completed: "neutral",
  Cancelled: "red",
  NoShow: "red",
  Expired: "void",
};

export const ALL_STATUSES: BookingStatus[] = ["Pending", "Confirmed", "Completed", "Cancelled", "NoShow", "Expired"];

/** Statuses that still hold a place in the diary. */
export function isLive(status: BookingStatus): boolean {
  return status === "Pending" || status === "Confirmed";
}

export const SOURCE_LABEL: Record<BookingSource, string> = {
  WalkIn: "Walk-in",
  Phone: "Phone",
  Online: "Online",
};

export const PAYMENT_TYPE_LABEL: Record<PaymentType, string> = {
  Deposit: "Deposit",
  Balance: "Balance",
  Refund: "Refund",
};

export const PAYMENT_METHOD_LABEL: Record<PaymentMethod, string> = {
  Online: "Online (Paystack)",
  Cash: "Cash",
  Transfer: "Bank transfer",
  Card: "Card (POS)",
};
