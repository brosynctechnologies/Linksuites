import type { ProductDto, SaveProductRequest } from "./types";

/** Build a full PUT body from an existing product (used for quick toggles). */
export function toSaveRequest(p: ProductDto, patch: Partial<SaveProductRequest> = {}): SaveProductRequest {
  return {
    name: p.name,
    description: p.description,
    categoryId: p.categoryId,
    priceMinor: p.priceMinor,
    compareAtPriceMinor: p.compareAtPriceMinor,
    inStock: p.inStock,
    // Stock is sent back unchanged with the count it was read at, so a quick toggle never writes over sales made meanwhile.
    stockQty: p.stockQty,
    expectedStockQty: p.stockQty,
    isActive: p.isActive,
    position: p.position,
    options: p.options,
    variants: p.variants.map((v) => ({
      id: v.id,
      optionValues: v.optionValues,
      priceMinor: v.priceMinor,
      inStock: v.inStock,
      sku: v.sku,
      stockQty: v.stockQty ?? null,
      expectedStockQty: v.stockQty ?? null,
    })),
    ...patch,
  };
}

/** Cartesian product of option values: [["S","M"],["Red"]] -> [["S","Red"],["M","Red"]]. */
export function combinations(lists: string[][]): string[][] {
  if (lists.length === 0) return [];
  return lists.reduce<string[][]>((acc, values) => acc.flatMap((combo) => values.map((v) => [...combo, v])), [[]]);
}
