// Types mirroring docs/PROPERTY_API.md and the shared auth/team/billing parts of docs/API.md.
// Money fields ending in `Minor` are integers in kobo. Instants are ISO 8601 with an offset; dates are "YYYY-MM-DD".

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
  isPlatformAdmin?: boolean;
}
export type Role = "Owner" | "Admin" | "Staff" | "Accountant";
export interface AuthTenant {
  id: string;
  name: string;
  slug: string;
  role: Role | string;
  product?: string;
}
/** Agency tokens carry a tenant; seeker tokens have `tenant: null`. */
export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: AuthUser;
  tenant: AuthTenant | null;
}

// ---- Enums ----
export type ListingPurpose = "Rent" | "Sale" | "ShortLet";
export type PricePeriod = "Year" | "Month" | "Total" | "Night";
export type ListingStatus = "Draft" | "Pending" | "Active" | "Expired" | "Rented" | "Sold" | "Rejected" | "Hidden";
export type CostKind = "AgencyFee" | "LegalFee" | "Caution" | "ServiceCharge" | "Other";
export type MediaStatus = "Processing" | "Ready" | "Failed";
export type AgencyKind = "Agency" | "Agent" | "Landlord";
export type LocationKind = "State" | "City" | "Area";
export type EnquiryStatus = "New" | "Replied" | "Closed";
export type InspectionStatus = "Requested" | "Confirmed" | "Done" | "Cancelled" | "NoShow";
export type StayStatus = "Pending" | "Confirmed" | "Completed" | "Cancelled" | "Expired";
export type StayCanceller = "Guest" | "Host";
export type ReportReason = "Fake" | "AlreadyTaken" | "WrongPrice" | "Duplicate" | "Scam" | "Other";
export type VerificationStatus = "Pending" | "Approved" | "Rejected";
export type AlertFrequency = "Off" | "Instant" | "Daily";
export type BoostKind = "Featured" | "Top";
export type BoostStatus = "Pending" | "Active" | "Expired";
export type ReviewBasis = "Enquiry" | "Inspection" | "Stay";
export type ListingEventKind = "View" | "WhatsApp" | "Call";
export type PayoutStatus = "Requested" | "Paid" | "Rejected";

// ---- Public ----
export interface LocationDto {
  id: string;
  name: string;
  slug: string;
  kind: LocationKind;
  parentSlug: string | null;
  lat: number | null;
  lng: number | null;
}
export interface ListingCardDto {
  id: string;
  title: string;
  slug: string;
  agencySlug: string;
  agencyName: string;
  verified: boolean;
  purpose: ListingPurpose;
  typeName: string;
  priceMinor: number;
  currency: string;
  pricePeriod: PricePeriod;
  bedrooms: number;
  bathrooms: number;
  sizeSqm: number | null;
  areaName: string;
  cityName: string;
  coverUrl: string | null;
  photoCount: number;
  featured: boolean;
  top: boolean;
  lat: number;
  lng: number;
  publishedAt: string | null;
}
export interface MapPin {
  id: string;
  lat: number;
  lng: number;
  priceMinor: number;
  slug: string;
  agencySlug: string;
}
export interface SearchResultDto {
  items: Paged<ListingCardDto>;
  pins: MapPin[];
  location: LocationDto | null;
}
export interface Photo {
  thumbUrl: string;
  cardUrl: string;
  fullUrl: string;
  width: number;
  height: number;
}
export interface CostDto {
  kind: CostKind;
  label: string;
  amountMinor: number;
}
export interface ShortLetDto {
  cleaningFeeMinor: number;
  minNights: number;
  maxGuests: number;
  checkInTime: string | null;
  checkOutTime: string | null;
  houseRules: string | null;
}
export interface AgentCardDto {
  id: string;
  displayName: string;
  slug: string;
  photoUrl: string | null;
  phone: string | null;
  whatsApp: string | null;
  verified: boolean;
  ratingAverage: number;
  ratingCount: number;
}
export interface AgencyCardDto {
  id: string;
  name: string;
  slug: string;
  kind: AgencyKind;
  logoUrl: string | null;
  verified: boolean;
  phone: string | null;
}
export interface ListingDetailDto {
  id: string;
  title: string;
  slug: string;
  description: string;
  purpose: ListingPurpose;
  type: { name: string; slug: string };
  breadcrumb: LocationDto[];
  address: string | null;
  lat: number;
  lng: number;
  priceMinor: number;
  currency: string;
  pricePeriod: PricePeriod;
  bedrooms: number;
  bathrooms: number;
  toilets: number;
  sizeSqm: number | null;
  furnished: boolean;
  tourUrl: string | null;
  videoUrl: string | null;
  photos: Photo[];
  costs: CostDto[];
  moveInTotalMinor: number;
  amenities: { name: string; slug: string }[];
  shortLet: ShortLetDto | null;
  agent: AgentCardDto;
  agency: AgencyCardDto;
  publishedAt: string | null;
  lastConfirmedAt: string | null;
  similar: ListingCardDto[];
}
export interface AgentReviewDto {
  id: string;
  userName: string;
  rating: number;
  comment: string | null;
  basis: ReviewBasis;
  createdAt: string;
}
export interface AgentPageDto {
  agent: AgentCardDto;
  bio: string | null;
  licenceNo: string | null;
  agency: AgencyCardDto;
  listings: ListingCardDto[];
  reviews: AgentReviewDto[];
}
export interface AgencyPageDto {
  agency: AgencyCardDto;
  bio: string | null;
  address: string | null;
  agents: AgentCardDto[];
  listings: ListingCardDto[];
  ratingAverage: number;
  ratingCount: number;
}
export interface RentBand {
  bedrooms: number;
  medianMinor: number;
  count: number;
}
export interface AreaGuideDto {
  location: LocationDto;
  breadcrumb: LocationDto[];
  areas: LocationDto[];
  activeCount: number;
  rentByBedrooms: RentBand[];
  medianSaleMinor: number | null;
  medianNightlyMinor: number | null;
  latest: ListingCardDto[];
}
export interface DateRange {
  start: string;
  end: string;
}
export interface AvailabilityDto {
  booked: DateRange[];
  blocked: DateRange[];
}
export interface StayQuoteDto {
  nights: number;
  nightlyMinor: number;
  subtotalMinor: number;
  cleaningMinor: number;
  totalMinor: number;
  currency: string;
}
export interface RenewResultDto {
  title: string;
  status: ListingStatus;
  expiresAt: string | null;
  note: string | null;
}
export interface RefItem {
  id: string;
  name: string;
  slug: string;
}

// ---- Seekers ----
export interface ListingFilter {
  purpose?: ListingPurpose | null;
  location?: string | null;
  type?: string | null;
  minPrice?: number | null;
  maxPrice?: number | null;
  minBeds?: number | null;
  amenities?: string[] | null;
  verifiedOnly: boolean;
  bbox?: number[] | null;
}
export interface SavedSearchDto {
  id: string;
  name: string;
  filter: ListingFilter;
  frequency: AlertFrequency;
  lastSentAt: string | null;
  createdAt: string;
}
export interface MyEnquiryDto {
  id: string;
  listingId: string;
  listingTitle: string;
  listingUrl: string | null;
  agencyName: string;
  message: string | null;
  status: EnquiryStatus;
  createdAt: string;
}
export interface InspectionDto {
  id: string;
  listingId: string;
  listingTitle: string;
  areaName: string;
  seekerName: string;
  seekerPhone: string | null;
  agentName: string;
  agentPhone: string | null;
  scheduledAt: string;
  status: InspectionStatus;
  notes: string | null;
}
export interface StayBookingResponse {
  stayId: string;
  authorizationUrl: string;
  totalMinor: number;
  holdExpiresAt: string;
}
export interface MyStayDto {
  id: string;
  listingId: string;
  listingTitle: string;
  listingUrl: string | null;
  coverUrl: string | null;
  areaName: string;
  checkIn: string;
  checkOut: string;
  guests: number;
  nights: number;
  totalMinor: number;
  currency: string;
  status: StayStatus;
  holdExpiresAt: string | null;
  refundMinor: number;
  agentName: string;
  agentPhone: string | null;
  checkInTime: string | null;
  checkOutTime: string | null;
  address: string | null;
  houseRules: string | null;
  /** Money reached PropertyLink for this stay (it may since have been refunded). */
  paid: boolean;
  nightlyMinor: number;
  cleaningMinor: number;
}

// ---- Studio ----
export interface CostInput {
  kind: CostKind;
  label: string;
  amountMinor: number;
}
export interface ShortLetInput {
  cleaningFeeMinor: number;
  minNights: number;
  maxGuests: number;
  checkInTime?: string | null;
  checkOutTime?: string | null;
  houseRules?: string | null;
}
export interface SaveListingRequest {
  title: string;
  description?: string | null;
  purpose: ListingPurpose;
  propertyTypeId: string;
  locationId: string;
  address?: string | null;
  lat: number;
  lng: number;
  priceMinor: number;
  pricePeriod: PricePeriod;
  bedrooms: number;
  bathrooms: number;
  toilets: number;
  sizeSqm?: number | null;
  furnished: boolean;
  tourUrl?: string | null;
  videoUrl?: string | null;
  costs?: CostInput[];
  amenityIds?: string[];
  shortLet?: ShortLetInput | null;
  agentProfileId?: string | null;
}
export interface ListingMediaDto {
  id: string;
  fileName: string;
  thumbUrl: string | null;
  cardUrl: string | null;
  fullUrl: string | null;
  status: MediaStatus;
  position: number;
  error: string | null;
  duplicateOfListingId: string | null;
  duplicateOfAgency: string | null;
}
export interface StudioListingDto {
  id: string;
  title: string;
  slug: string;
  description: string;
  purpose: ListingPurpose;
  propertyTypeId: string;
  locationId: string;
  address: string;
  lat: number;
  lng: number;
  priceMinor: number;
  currency: string;
  pricePeriod: PricePeriod;
  bedrooms: number;
  bathrooms: number;
  toilets: number;
  sizeSqm: number | null;
  furnished: boolean;
  tourUrl: string | null;
  videoUrl: string | null;
  status: ListingStatus;
  moderationNote: string | null;
  publishedAt: string | null;
  expiresAt: string | null;
  featuredUntil: string | null;
  topUntil: string | null;
  viewsCount: number;
  costs: CostDto[];
  amenityIds: string[];
  shortLet: ShortLetDto | null;
  moveInTotalMinor: number;
  agentProfileId: string;
  publicUrl: string;
  media: ListingMediaDto[];
  createdAt: string;
  updatedAt: string | null;
}
export interface StudioListingSummaryDto {
  id: string;
  title: string;
  slug: string;
  purpose: ListingPurpose;
  status: ListingStatus;
  priceMinor: number;
  currency: string;
  pricePeriod: PricePeriod;
  areaName: string;
  coverUrl: string | null;
  viewsCount: number;
  expiresAt: string | null;
  featuredUntil: string | null;
  topUntil: string | null;
  updatedAt: string;
}
export interface BlockDto {
  id: string;
  start: string;
  end: string;
  note: string | null;
}
export interface AgencyProfileDto {
  id: string;
  name: string;
  slug: string;
  kind: AgencyKind;
  logoUrl: string | null;
  phone: string | null;
  email: string | null;
  address: string | null;
  bio: string | null;
  verified: boolean;
  verifiedAt: string | null;
  publicUrl: string;
}
export interface AgentProfileDto {
  id: string;
  userId: string;
  displayName: string;
  slug: string;
  bio: string | null;
  phone: string | null;
  whatsApp: string | null;
  licenceNo: string | null;
  photoUrl: string | null;
  verified: boolean;
  ratingAverage: number;
  ratingCount: number;
}
export interface EnquiryDto {
  id: string;
  listingId: string;
  listingTitle: string;
  name: string;
  phone: string;
  email: string | null;
  message: string | null;
  status: EnquiryStatus;
  createdAt: string;
}
export type Weekday = "Monday" | "Tuesday" | "Wednesday" | "Thursday" | "Friday" | "Saturday" | "Sunday";
export interface SlotDto {
  weekday: Weekday;
  start: string;
  end: string;
  slotMinutes: number;
}
export interface StudioStayDto {
  id: string;
  listingId: string;
  listingTitle: string;
  guestName: string;
  guestPhone: string | null;
  checkIn: string;
  checkOut: string;
  guests: number;
  nights: number;
  totalMinor: number;
  commissionMinor: number;
  status: StayStatus;
  refundMinor: number;
  cancelledBy: StayCanceller | null;
  createdAt: string;
  /** The host's earning after commission and any refund, from the server's earning row (0 when reversed or never paid). */
  hostNetMinor: number;
}
export interface ListingStatsRow {
  listingId: string;
  title: string;
  status: ListingStatus;
  views: number;
  whatsAppClicks: number;
  callClicks: number;
  enquiries: number;
}
export interface ListingStatsDto {
  days: number;
  views: number;
  whatsAppClicks: number;
  callClicks: number;
  enquiries: number;
  listings: ListingStatsRow[];
}
export interface VerificationDocument {
  index: number;
  label: string;
  fileName: string;
  contentType: string;
}
export interface VerificationDto {
  id: string;
  kind: string;
  status: VerificationStatus;
  licenceNo: string | null;
  note: string | null;
  documents: VerificationDocument[];
  createdAt: string;
  reviewedAt: string | null;
}
export interface BoostOptionDto {
  code: string;
  kind: BoostKind;
  days: number;
  priceMinor: number;
  currency: string;
  name: string;
}
export interface BoostDto {
  id: string;
  listingId: string;
  listingTitle: string;
  code: string;
  kind: BoostKind;
  days: number;
  amountMinor: number;
  status: BoostStatus;
  paidAt: string | null;
  endsAt: string | null;
  createdAt: string;
}
export interface EarningEntry {
  id: string;
  stayId: string;
  description: string;
  grossMinor: number;
  feeMinor: number;
  netMinor: number;
  availableAt: string;
  createdAt: string;
  status: string;
}
export interface EarningsDto {
  currency: string;
  availableMinor: number;
  pendingMinor: number;
  paidOutMinor: number;
  entries: EarningEntry[];
}
export interface PayoutAccount {
  bankName: string;
  accountNumber: string;
  accountName: string;
}
export interface PayoutDto {
  id: string;
  amountMinor: number;
  status: PayoutStatus;
  bankName: string;
  accountNumber: string;
  accountName: string;
  reference: string | null;
  requestedAt: string;
  paidAt: string | null;
  note: string | null;
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
  /** Nothing renews automatically: the plan ends at currentPeriodEnd unless paid again. A comp can't be extended by paying. */
  subscription: { status: string; currentPeriodEnd: string | null; complimentary: boolean } | null;
  plans: PlanDto[];
  usage: { items?: UsageItem[] } & Record<string, unknown>;
  paymentsEnabled: boolean;
}

// ---- Team ----
export type InviteRole = Exclude<Role, "Owner">;
export interface MemberDto {
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
