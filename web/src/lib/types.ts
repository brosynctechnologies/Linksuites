import type { BankTransfer, PendingTransfer } from "@/lib/plan-payment";

// Types mirroring docs/API.md. Money fields ending in `Minor` are integers in minor units.

export type OrderStatus = "New" | "Confirmed" | "Paid" | "Delivered" | "Cancelled";
export type PaymentStatus = "Unpaid" | "Paid";
export type Fulfilment = "Delivery" | "Pickup";
export type DiscountType = "Percent" | "Fixed";

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
  code?: string;
}

// ---- Auth ----
export interface AuthUser {
  id: string;
  fullName: string;
  email: string;
}
export interface AuthTenant {
  id: string;
  name: string;
  slug: string;
  role: string;
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
  storeName: string;
  whatsappNumber: string;
  country: string;
}
export interface MeResponse {
  user: AuthUser;
  tenant: AuthTenant;
}

// ---- Store ----
export interface StoreDto {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  logoUrl: string | null;
  coverUrl: string | null;
  whatsappNumber: string;
  currency: string;
  themeColor: string | null;
  isOpen: boolean;
  openingHours: string | null;
  country: string;
  storeUrl: string;
}
export interface SaveStoreRequest {
  name: string;
  slug: string;
  description: string | null;
  whatsappNumber: string;
  currency: string;
  themeColor: string | null;
  isOpen: boolean;
  openingHours: string | null;
}

// ---- Catalogue ----
export interface CategoryDto {
  id: string;
  name: string;
  position: number;
  productCount: number;
}
export interface ProductImage {
  id: string;
  url: string;
  position: number;
}
export interface ProductOption {
  name: string;
  values: string[];
}
export interface VariantDto {
  id: string;
  optionValues: string[];
  title: string;
  priceMinor: number | null;
  inStock: boolean;
  sku: string | null;
}
export interface ProductDto {
  id: string;
  categoryId: string | null;
  categoryName: string | null;
  name: string;
  slug: string;
  description: string | null;
  priceMinor: number;
  compareAtPriceMinor: number | null;
  inStock: boolean;
  stockQty: number | null;
  isActive: boolean;
  position: number;
  images: ProductImage[];
  options: ProductOption[];
  variants: VariantDto[];
}
export interface SaveVariant {
  id?: string;
  optionValues: string[];
  priceMinor?: number | null;
  inStock: boolean;
  sku: string | null;
}
export interface SaveProductRequest {
  name: string;
  description: string | null;
  categoryId: string | null;
  priceMinor: number;
  compareAtPriceMinor: number | null;
  inStock: boolean;
  stockQty: number | null;
  /** The stock the edit form loaded. Stock is then only written if it changed, and only if the stored count still matches (else 409). */
  expectedStockQty?: number | null;
  isActive: boolean;
  position: number;
  options: ProductOption[];
  variants: SaveVariant[];
}

// ---- Delivery & discounts ----
export interface DeliveryZoneDto {
  id: string;
  name: string;
  feeMinor: number;
  etaText: string | null;
  isActive: boolean;
}
export type SaveDeliveryZone = Omit<DeliveryZoneDto, "id">;

export interface DiscountCodeDto {
  id: string;
  code: string;
  type: DiscountType;
  value: number;
  minOrderMinor: number;
  expiresAt: string | null;
  maxUses: number | null;
  uses: number;
  isActive: boolean;
}
export type SaveDiscountCode = Omit<DiscountCodeDto, "id" | "uses">;

// ---- Orders ----
export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}
export interface OrderSummaryDto {
  id: string;
  number: number;
  code: string;
  customerName: string;
  phone: string;
  fulfilment: Fulfilment;
  totalMinor: number;
  currency: string;
  status: OrderStatus;
  paymentStatus: PaymentStatus;
  itemCount: number;
  createdAt: string;
}
export interface OrderItemDto {
  /** Null when the product was deleted after the order was placed. */
  productId: string | null;
  variantId: string | null;
  name: string;
  variant: string | null;
  unitPriceMinor: number;
  quantity: number;
  lineTotalMinor: number;
}
export interface OrderHistoryDto {
  status: OrderStatus;
  note: string | null;
  createdAt: string;
}
export interface OrderDto extends OrderSummaryDto {
  address: string | null;
  note: string | null;
  deliveryZoneName: string | null;
  subtotalMinor: number;
  deliveryFeeMinor: number;
  discountMinor: number;
  discountCode: string | null;
  items: OrderItemDto[];
  history: OrderHistoryDto[];
  whatsappMessage: string | null;
  customerWhatsappUrl: string;
}
export interface DashboardDto {
  currency: string;
  todayOrders: number;
  todaySalesMinor: number;
  monthSalesMinor: number;
  newOrders: number;
  productCount: number;
  recentOrders: OrderSummaryDto[];
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
export interface BillingDto {
  currentPlan: PlanDto;
  subscription: { status: string; currentPeriodEnd: string | null; complimentary?: boolean } | null;
  plans: PlanDto[];
  usage: { products: number; maxProducts: number };
  paymentsEnabled: boolean;
  /** The company account to pay a plan into by bank transfer: only while online payment is off. */
  bankTransfer?: BankTransfer | null;
  /** A bank transfer this business said it sent, waiting for confirmation. */
  pendingTransfer?: PendingTransfer | null;
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

// ---- Public storefront ----
export interface PublicDeliveryZone {
  id: string;
  name: string;
  feeMinor: number;
  etaText: string | null;
}
export interface PublicStoreDto {
  name: string;
  slug: string;
  description: string | null;
  logoUrl: string | null;
  coverUrl: string | null;
  currency: string;
  themeColor: string | null;
  isOpen: boolean;
  deliveryZones: PublicDeliveryZone[];
  categories: { id: string; name: string }[];
  showBranding: boolean;
  hasDiscounts: boolean;
}
export interface PublicVariant {
  id: string;
  optionValues: string[];
  title: string;
  priceMinor: number | null;
  inStock: boolean;
}
export interface PublicProductDto {
  id: string;
  slug: string;
  name: string;
  description: string | null;
  priceMinor: number;
  compareAtPriceMinor: number | null;
  inStock: boolean;
  categoryId: string | null;
  images: { url: string }[];
  options: ProductOption[];
  variants: PublicVariant[];
}
export interface QuoteItem {
  productId: string;
  variantId: string | null;
  quantity: number;
}
export interface QuoteRequest {
  items: QuoteItem[];
  fulfilment: Fulfilment;
  deliveryZoneId: string | null;
  discountCode: string | null;
}
export interface QuoteLine {
  productId: string;
  variantId: string | null;
  name: string;
  variant: string | null;
  unitPriceMinor: number;
  quantity: number;
  lineTotalMinor: number;
  /** Null when this product does not track stock. */
  stockQty: number | null;
}
export interface QuoteDto {
  lines: QuoteLine[];
  subtotalMinor: number;
  deliveryFeeMinor: number;
  discountMinor: number;
  totalMinor: number;
  currency: string;
  discountMessage: string | null;
}
export interface PlaceOrderRequest extends QuoteRequest {
  customerName: string;
  phone: string;
  address: string | null;
  note: string | null;
}
export interface PlaceOrderResponse {
  code: string;
  number: number;
  totalMinor: number;
  currency: string;
  whatsappUrl: string;
  orderUrl: string;
}
export interface PublicOrderDto {
  code: string;
  number: number;
  storeName: string;
  storeSlug: string;
  status: OrderStatus;
  paymentStatus: PaymentStatus;
  fulfilment: Fulfilment;
  items: { name: string; variant: string | null; quantity: number; lineTotalMinor: number }[];
  subtotalMinor: number;
  deliveryFeeMinor: number;
  discountMinor: number;
  totalMinor: number;
  currency: string;
  createdAt: string;
  whatsappUrl: string;
  /** False on plans that remove the "Powered by" line. */
  showBranding: boolean;
}
