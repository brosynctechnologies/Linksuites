// Types mirroring docs/INVOICES_API.md, docs/INVOICES_PHASE2_API.md (and shared parts of docs/API.md).
// Money fields ending in `Minor` are integers in minor units. Dates without time are "YYYY-MM-DD".

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
  code?: string;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

// ---- Auth ----
export interface AuthUser {
  id: string;
  fullName: string;
  email: string;
}
export type Role = "Owner" | "Admin" | "Staff" | "Accountant";
export interface AuthTenant {
  id: string;
  name: string;
  slug: string;
  role: Role | string;
  product?: string;
}
export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: AuthUser;
  tenant: AuthTenant;
}
export interface SignupRequest {
  fullName: string;
  email: string;
  password: string;
  country: string;
  product: "invoices";
  businessName: string;
}

// ---- Business ----
export interface BusinessDto {
  legalName: string;
  logoUrl: string | null;
  address: string | null;
  email: string | null;
  phone: string | null;
  taxId: string | null;
  bankDetails: string | null;
  currency: string;
  country: string;
  paymentTermsDays: number;
  invoicePrefix: string;
  nextInvoiceNumber: number;
  quotePrefix: string;
  nextQuoteNumber: number;
  defaultTemplate: string;
  brandColor: string | null;
  defaultNotes: string | null;
  defaultTerms: string | null;
  remindersEnabled: boolean;
  reminderDaysBefore: number | null;
  reminderDaysAfter: number[];
  paystackConnected: boolean;
  tagline: string | null;
  registrationNumber: string | null;
  headerLine: HeaderLine;
  secondaryColor: string | null;
  logoSize: LogoSize;
  fontStyle: FontStyle;
  documentTitle: DocumentTitle;
  showTaxId: boolean;
  showRegistrationNumber: boolean;
  showBankDetails: boolean;
  showSignature: boolean;
  signatureName: string | null;
  signatureRole: string | null;
  /** Stored image URLs; the owner always sees them. They print only when `brandImagesAvailable`. */
  signatureImageUrl: string | null;
  stampImageUrl: string | null;
  footerText: string | null;
  /** The plan includes logo, signature and stamp images (upload and print). */
  brandImagesAvailable: boolean;
}
export type HeaderLine = "tagline" | "address" | "registration" | "none";
export type LogoSize = "small" | "medium" | "large";
export type FontStyle = "modern" | "serif" | "sans";
export type DocumentTitle = "Invoice" | "Tax invoice" | "Bill" | "Pro forma invoice" | "Statement of account";
/** Letterhead settings in the update body. Omitted choices and switches keep their saved value; omitted text is cleared. */
type LetterheadKeys =
  | "tagline"
  | "registrationNumber"
  | "headerLine"
  | "secondaryColor"
  | "logoSize"
  | "fontStyle"
  | "documentTitle"
  | "showTaxId"
  | "showRegistrationNumber"
  | "showBankDetails"
  | "showSignature"
  | "signatureName"
  | "signatureRole"
  | "footerText";
export type UpdateBusinessRequest = Omit<
  BusinessDto,
  "logoUrl" | "country" | "paystackConnected" | "signatureImageUrl" | "stampImageUrl" | "brandImagesAvailable" | LetterheadKeys
> &
  Partial<Pick<BusinessDto, LetterheadKeys>>;

// ---- Tax rates, clients, items ----
export interface TaxRateDto {
  id: string;
  name: string;
  ratePercent: number;
}
export interface SaveClientRequest {
  name: string;
  company: string | null;
  email: string | null;
  phone: string | null;
  address: string | null;
  taxId: string | null;
  notes: string | null;
}
export interface ClientDto extends SaveClientRequest {
  id: string;
  invoiceCount: number;
  /** In the business base currency: each invoice balance converted with its own exchange rate, as on the dashboard. */
  outstandingMinor: number;
}
export interface SaveItemRequest {
  name: string;
  description: string | null;
  unitPriceMinor: number;
  taxRateId: string | null;
  unit: string | null;
}
export interface ItemDto extends SaveItemRequest {
  id: string;
}

// ---- Invoices ----
export type InvoiceStatus = "Draft" | "Sent" | "Viewed" | "PartiallyPaid" | "Paid" | "Overdue" | "Void";
export type PaymentMethod = "Cash" | "BankTransfer" | "Card" | "Online" | "Other";
export type InvoiceEventType =
  | "Created"
  | "Sent"
  | "Viewed"
  | "Reminded"
  | "PaymentRecorded"
  | "PaymentRemoved"
  | "Paid"
  | "Voided"
  | "Overpaid";

export interface LineRequest {
  itemId: string | null;
  description: string;
  quantity: number;
  unitPriceMinor: number;
  discountPercent: number;
  taxRateId: string | null;
}
export interface SaveInvoiceRequest {
  clientId: string;
  issueDate: string;
  dueDate: string;
  currency: string;
  template: string;
  items: LineRequest[];
  shippingMinor: number;
  notes: string | null;
  terms: string | null;
  /** Value of 1 unit of `currency` in the base currency. Omit to use today's rate; ignored for the base currency. */
  exchangeRate?: number;
}
export interface ClientSnapshot {
  id: string | null;
  name: string;
  company: string | null;
  email: string | null;
  phone: string | null;
  address: string | null;
  taxId: string | null;
}
export interface LineDto {
  id: string;
  itemId: string | null;
  description: string;
  quantity: number;
  unitPriceMinor: number;
  discountPercent: number;
  taxRateId: string | null;
  taxName: string | null;
  taxRatePercent: number | null;
  lineTotalMinor: number;
}
export interface TaxLine {
  name: string;
  ratePercent: number;
  amountMinor: number;
}
export interface PaymentDto {
  id: string;
  amountMinor: number;
  method: PaymentMethod;
  reference: string | null;
  paidAt: string;
  receiptNumber: string | null;
  note: string | null;
  online: boolean;
  /** Rate on the payment date (1 unit of the invoice currency in base). */
  exchangeRate: number;
  baseAmountMinor: number;
}
export interface InvoiceEventDto {
  type: InvoiceEventType | string;
  note: string | null;
  createdAt: string;
}
export interface InvoiceSummaryDto {
  id: string;
  number: string;
  clientName: string;
  issueDate: string;
  dueDate: string;
  currency: string;
  totalMinor: number;
  balanceMinor: number;
  totalBaseMinor: number;
  balanceBaseMinor: number;
  status: InvoiceStatus;
  createdAt: string;
}
export interface InvoiceDto {
  id: string;
  number: string;
  status: InvoiceStatus;
  client: ClientSnapshot;
  issueDate: string;
  dueDate: string;
  currency: string;
  template: string;
  items: LineDto[];
  subtotalMinor: number;
  discountTotalMinor: number;
  taxTotalMinor: number;
  taxes: TaxLine[];
  shippingMinor: number;
  totalMinor: number;
  amountPaidMinor: number;
  balanceMinor: number;
  notes: string | null;
  terms: string | null;
  publicUrl: string;
  whatsappShareUrl: string;
  sentAt: string | null;
  viewedAt: string | null;
  paidAt: string | null;
  quoteId: string | null;
  recurringScheduleId: string | null;
  payments: PaymentDto[];
  events: InvoiceEventDto[];
  createdAt: string;
  baseCurrency: string;
  exchangeRate: number;
  totalBaseMinor: number;
  balanceBaseMinor: number;
}
export interface RecordPaymentRequest {
  amountMinor: number;
  method: PaymentMethod;
  reference: string | null;
  paidAt: string;
  note: string | null;
  sendReceipt: boolean;
}
export interface InvoiceDashboardDto {
  currency: string;
  outstandingMinor: number;
  overdueMinor: number;
  overdueCount: number;
  paidThisMonthMinor: number;
  draftCount: number;
  invoicesThisMonth: number;
  invoiceLimit: number;
  recentInvoices: InvoiceSummaryDto[];
}
export interface TemplateDto {
  key: string;
  name: string;
  isPremium: boolean;
  available?: boolean;
}

// ---- Quotes ----
export type QuoteStatus = "Draft" | "Sent" | "Accepted" | "Declined" | "Converted" | "Expired";
export interface SaveQuoteRequest {
  clientId: string;
  issueDate: string;
  expiryDate: string;
  currency: string;
  template: string;
  items: LineRequest[];
  shippingMinor: number;
  notes: string | null;
  terms: string | null;
  exchangeRate?: number;
}
export interface QuoteSummaryDto {
  id: string;
  number: string;
  clientName: string;
  issueDate: string;
  expiryDate: string;
  currency: string;
  totalMinor: number;
  status: QuoteStatus;
  createdAt: string;
}
export interface QuoteDto {
  id: string;
  number: string;
  status: QuoteStatus;
  client: ClientSnapshot;
  issueDate: string;
  expiryDate: string;
  currency: string;
  template: string;
  items: LineDto[];
  subtotalMinor: number;
  discountTotalMinor: number;
  taxTotalMinor: number;
  taxes: TaxLine[];
  shippingMinor: number;
  totalMinor: number;
  notes: string | null;
  terms: string | null;
  publicUrl: string;
  whatsappShareUrl: string;
  sentAt: string | null;
  viewedAt: string | null;
  convertedInvoiceId: string | null;
  events: InvoiceEventDto[];
  createdAt: string;
  baseCurrency: string;
  exchangeRate: number;
  totalBaseMinor: number;
}

// ---- Recurring ----
export type RecurringFrequency = "Weekly" | "Monthly" | "Quarterly" | "Yearly";
export interface SaveRecurringRequest {
  clientId: string;
  frequency: RecurringFrequency;
  startDate: string;
  endDate: string | null;
  dueInDays: number;
  autoSend: boolean;
  isActive: boolean;
  currency: string;
  template: string;
  items: LineRequest[];
  shippingMinor: number;
  notes: string | null;
  terms: string | null;
}
export interface RecurringDto extends SaveRecurringRequest {
  id: string;
  clientName: string;
  nextRunDate: string | null;
  lastRunDate: string | null;
  invoicesCreated: number;
  totalMinor: number;
  items: (LineRequest & Partial<LineDto>)[];
}

// ---- Public ----
export interface PublicBusiness {
  name: string;
  logoUrl: string | null;
  address: string | null;
  email: string | null;
  phone: string | null;
  taxId: string | null;
  bankDetails: string | null;
  brandColor: string | null;
  tagline: string | null;
  registrationNumber: string | null;
  headerLine: HeaderLine;
  secondaryColor: string | null;
  logoSize: LogoSize;
  fontStyle: FontStyle;
  documentTitle: DocumentTitle;
  showTaxId: boolean;
  showRegistrationNumber: boolean;
  showBankDetails: boolean;
  showSignature: boolean;
  signatureName: string | null;
  signatureRole: string | null;
  /** Null unless the business's plan includes images (as are `logoUrl` and `stampImageUrl`). */
  signatureImageUrl: string | null;
  stampImageUrl: string | null;
  footerText: string | null;
}
export interface PublicPayment {
  id: string;
  amountMinor: number;
  method: PaymentMethod;
  paidAt: string;
  receiptNumber: string | null;
}
export interface PublicInvoiceDto {
  number: string;
  status: InvoiceStatus;
  business: PublicBusiness;
  client: ClientSnapshot;
  issueDate: string;
  dueDate: string;
  currency: string;
  template: string;
  items: LineDto[];
  subtotalMinor: number;
  discountTotalMinor: number;
  taxTotalMinor: number;
  taxes: TaxLine[];
  shippingMinor: number;
  totalMinor: number;
  amountPaidMinor: number;
  balanceMinor: number;
  notes: string | null;
  terms: string | null;
  canPayOnline: boolean;
  payments: PublicPayment[];
  showBranding: boolean;
  /** Only on the verify response: true when this return from Paystack recorded a payment, or the invoice is paid. */
  paymentConfirmed?: boolean | null;
}
export interface PublicQuoteDto {
  number: string;
  status: QuoteStatus;
  business: PublicBusiness;
  client: ClientSnapshot;
  issueDate: string;
  expiryDate: string;
  currency: string;
  template: string;
  items: LineDto[];
  subtotalMinor: number;
  discountTotalMinor: number;
  taxTotalMinor: number;
  taxes: TaxLine[];
  shippingMinor: number;
  totalMinor: number;
  notes: string | null;
  terms: string | null;
  canRespond: boolean;
  showBranding: boolean;
}
export interface PreviewRequest {
  business: {
    name: string;
    address: string;
    email: string;
    phone: string;
    taxId: string;
    bankDetails: string;
    brandColor: string;
    /** The free letterhead fields; the generator takes no images. */
    tagline?: string;
    registrationNumber?: string;
    headerLine?: HeaderLine;
    secondaryColor?: string | null;
    fontStyle?: FontStyle;
    documentTitle?: DocumentTitle;
    showSignature?: boolean;
    signatureName?: string;
    signatureRole?: string;
    footerText?: string;
  };
  client: { name: string; company: string; email: string; phone: string; address: string };
  number: string;
  issueDate: string;
  dueDate: string;
  currency: string;
  template: string;
  items: { description: string; quantity: number; unitPriceMinor: number; discountPercent: number; taxRatePercent: number }[];
  shippingMinor: number;
  notes: string;
  terms: string;
}

// ---- Billing ----
export interface PlanDto {
  code: string;
  name: string;
  priceMinor: number;
  currency: string;
  interval: string;
  limits: Record<string, number>;
  features: string[];
}
export interface UsageItem {
  key: string;
  label: string;
  used: number;
  limit: number;
}
export interface BillingDto {
  currentPlan: PlanDto;
  subscription: { status: string; currentPeriodEnd: string | null; complimentary?: boolean } | null;
  plans: PlanDto[];
  usage: { items?: UsageItem[] } & Record<string, unknown>;
  paymentsEnabled: boolean;
}

// ---- Account activity ----
/** A read-only support view of this business (`GET /api/account/activity`, last 90 days). */
export interface SupportViewDto {
  sessionId: string;
  adminName: string;
  reason: string;
  startedAt: string;
  endsAt: string;
  /** Set when the view was ended before it expired. */
  endedAt: string | null;
}

// ---- Phase 2: team & businesses (docs/INVOICES_PHASE2_API.md) ----
export type InviteRole = Exclude<Role, "Owner">;
export interface MemberDto {
  /** Membership id. */
  id: string;
  userId: string;
  fullName: string;
  email: string;
  role: Role;
  isYou: boolean;
  joinedAt: string;
}
export interface InviteDto {
  id: string;
  email: string;
  role: InviteRole;
  invitedAt: string;
  expiresAt: string;
}
export interface TeamDto {
  members: MemberDto[];
  invites: InviteDto[];
  /** used = members + pending invites. */
  seats: { used: number; limit: number };
}
export interface PublicInviteDto {
  businessName: string;
  email: string;
  role: Role;
  hasAccount: boolean;
  expired: boolean;
}
export interface MyBusinessDto {
  tenantId: string;
  name: string;
  role: Role;
  current: boolean;
}

// ---- Phase 2: exchange rates ----
export interface ExchangeRateDto {
  currency: string;
  /** Value of 1 unit of `currency` in the base currency. */
  toBase: number;
}
export interface ExchangeRatesDto {
  base: string;
  asOf: string;
  source: string;
  rates: ExchangeRateDto[];
}

// ---- Phase 2: expenses (Business plan) ----
export interface SaveExpenseRequest {
  date: string;
  category: string;
  vendor: string | null;
  description: string | null;
  amountMinor: number;
  currency: string;
  /** VAT included in `amountMinor`. */
  taxMinor: number;
  paymentMethod: PaymentMethod;
  reference: string | null;
  clientId: string | null;
  notes: string | null;
  exchangeRate?: number | null;
}
export interface ExpenseDto extends SaveExpenseRequest {
  id: string;
  baseAmountMinor: number;
  receiptUrl: string | null;
  clientName: string | null;
  createdAt: string;
}

// ---- Phase 2: profit report (Business plan) ----
export interface ProfitMonth {
  /** "YYYY-MM" */
  month: string;
  incomeMinor: number;
  expensesMinor: number;
  profitMinor: number;
}
export interface ProfitReportDto {
  currency: string;
  from: string;
  to: string;
  incomeMinor: number;
  expensesMinor: number;
  profitMinor: number;
  invoicedMinor: number;
  outstandingMinor: number;
  taxCollectedMinor: number;
  taxOnExpensesMinor: number;
  expensesByCategory: { category: string; amountMinor: number }[];
  months: ProfitMonth[];
}
