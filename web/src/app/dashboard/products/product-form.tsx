"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState, type FormEvent } from "react";
import { api, ApiError, toApiError, upload } from "@/lib/api";
import { toMajorInput, toMinor } from "@/lib/money";
import { combinations } from "@/lib/products";
import { useApi } from "@/lib/use-api";
import type { CategoryDto, ProductDto, ProductImage, SaveProductRequest } from "@/lib/types";
import { ApiErrorView, Button, ErrorBox, FormSection, Input, Notice, Select, Spinner, Textarea, Toggle, inputClass } from "@/components/ui";
import { ImageIcon, PlusIcon, TrashIcon } from "@/components/icons";

interface OptionRow {
  name: string;
  values: string;
}
interface VariantOverride {
  id?: string;
  price: string;
  inStock: boolean;
  sku: string;
}

const MAX_BYTES = 5 * 1024 * 1024;
const ACCEPT = "image/jpeg,image/png,image/webp";
const variantKey = (values: string[]) => values.join(" / ");

function splitValues(text: string) {
  return Array.from(new Set(text.split(",").map((v) => v.trim()).filter(Boolean)));
}

export function ProductForm({ product, justCreated }: { product?: ProductDto; justCreated?: boolean }) {
  const router = useRouter();
  const { data: categories } = useApi<CategoryDto[]>("/api/categories");

  const [name, setName] = useState(product?.name ?? "");
  const [description, setDescription] = useState(product?.description ?? "");
  const [categoryId, setCategoryId] = useState(product?.categoryId ?? "");
  const [price, setPrice] = useState(toMajorInput(product?.priceMinor));
  const [compareAt, setCompareAt] = useState(toMajorInput(product?.compareAtPriceMinor));
  const [inStock, setInStock] = useState(product?.inStock ?? true);
  const [stockQty, setStockQty] = useState(product?.stockQty != null ? String(product.stockQty) : "");
  // The stock this form loaded. Saving sends it so the server only writes stock the seller changed, and refuses
  // (409) if orders moved the count meanwhile, instead of writing over those sales.
  const [loadedStock, setLoadedStock] = useState<number | null>(product?.stockQty ?? null);
  const [isActive, setIsActive] = useState(product?.isActive ?? true);
  const [options, setOptions] = useState<OptionRow[]>(
    product?.options.map((o) => ({ name: o.name, values: o.values.join(", ") })) ?? [],
  );
  const [overrides, setOverrides] = useState<Record<string, VariantOverride>>(() =>
    Object.fromEntries(
      (product?.variants ?? []).map((v) => [
        variantKey(v.optionValues),
        { id: v.id, price: toMajorInput(v.priceMinor), inStock: v.inStock, sku: v.sku ?? "" },
      ]),
    ),
  );
  const [images, setImages] = useState<ProductImage[]>(product?.images ?? []);

  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [saved, setSaved] = useState(false);
  const [uploading, setUploading] = useState(0);
  const [imageError, setImageError] = useState<ApiError | string | null>(null);
  const [deleting, setDeleting] = useState(false);

  const parsedOptions = useMemo(
    () => options.map((o) => ({ name: o.name.trim(), values: splitValues(o.values) })).filter((o) => o.name && o.values.length > 0),
    [options],
  );
  const combos = useMemo(() => combinations(parsedOptions.map((o) => o.values)), [parsedOptions]);

  const variantFor = (combo: string[]): VariantOverride => overrides[variantKey(combo)] ?? { price: "", inStock: true, sku: "" };
  const setVariant = (combo: string[], patch: Partial<VariantOverride>) =>
    setOverrides((o) => ({ ...o, [variantKey(combo)]: { ...variantFor(combo), ...patch } }));

  const updateOption = (i: number, patch: Partial<OptionRow>) =>
    setOptions((list) => list.map((o, j) => (j === i ? { ...o, ...patch } : o)));

  async function save(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setSaved(false);
    const priceMinor = toMinor(price);
    if (!name.trim()) return setError(new ApiError(400, { detail: "Enter a product name." }));
    if (priceMinor === null || priceMinor < 0) return setError(new ApiError(400, { detail: "Enter a valid price." }));
    const compareMinor = toMinor(compareAt);
    const qty = stockQty.trim() === "" ? null : Math.max(0, Math.floor(Number(stockQty)));
    if (qty !== null && Number.isNaN(qty)) return setError(new ApiError(400, { detail: "Stock quantity must be a number." }));

    const body: SaveProductRequest = {
      name: name.trim(),
      description: description.trim() || null,
      categoryId: categoryId || null,
      priceMinor,
      compareAtPriceMinor: compareMinor && compareMinor > 0 ? compareMinor : null,
      inStock,
      stockQty: qty,
      ...(product ? { expectedStockQty: loadedStock } : {}),
      isActive,
      position: product?.position ?? 0,
      options: parsedOptions,
      variants: combos.map((combo) => {
        const v = variantFor(combo);
        return {
          ...(v.id ? { id: v.id } : {}),
          optionValues: combo,
          priceMinor: toMinor(v.price),
          inStock: v.inStock,
          sku: v.sku.trim() || null,
        };
      }),
    };

    setSaving(true);
    try {
      if (product) {
        const updated = await api<ProductDto>(`/api/products/${product.id}`, { method: "PUT", body });
        setImages(updated.images);
        // Show the live count (sales may have lowered it) and compare the next save against it.
        setLoadedStock(updated.stockQty);
        setStockQty(updated.stockQty != null ? String(updated.stockQty) : "");
        setOverrides(
          Object.fromEntries(
            updated.variants.map((v) => [
              variantKey(v.optionValues),
              { id: v.id, price: toMajorInput(v.priceMinor), inStock: v.inStock, sku: v.sku ?? "" },
            ]),
          ),
        );
        setSaved(true);
        setSaving(false);
      } else {
        const created = await api<ProductDto>("/api/products", { method: "POST", body });
        router.replace(`/dashboard/products/${created.id}?created=1`);
      }
    } catch (err) {
      setError(toApiError(err));
      setSaving(false);
    }
  }

  async function onFiles(files: FileList | null) {
    if (!product || !files?.length) return;
    setImageError(null);
    const list = Array.from(files);
    const tooBig = list.filter((f) => f.size > MAX_BYTES);
    if (tooBig.length) setImageError(`${tooBig.map((f) => f.name).join(", ")} is larger than 5 MB.`);
    const ok = list.filter((f) => f.size <= MAX_BYTES);
    setUploading(ok.length);
    for (const file of ok) {
      try {
        const updated = await upload<ProductDto>(`/api/products/${product.id}/images`, file);
        setImages(updated.images);
      } catch (err) {
        setImageError(toApiError(err));
      }
      setUploading((n) => n - 1);
    }
  }

  async function deleteImage(img: ProductImage) {
    if (!product) return;
    setImageError(null);
    try {
      const updated = await api<ProductDto>(`/api/products/${product.id}/images/${img.id}`, { method: "DELETE" });
      setImages(updated.images);
    } catch (err) {
      setImageError(toApiError(err));
    }
  }

  async function deleteProduct() {
    if (!product || !window.confirm(`Delete “${product.name}”? This cannot be undone.`)) return;
    setDeleting(true);
    try {
      await api(`/api/products/${product.id}`, { method: "DELETE" });
      router.replace("/dashboard/products");
    } catch (err) {
      setError(toApiError(err));
      setDeleting(false);
    }
  }

  return (
    <form onSubmit={save} className="space-y-8" noValidate>
      {justCreated && <Notice tone="success">Product created. Now add some photos below.</Notice>}

      <FormSection title="Photos" description="JPEG, PNG or WebP up to 5 MB. The first photo is the cover.">
        {!product ? (
          <p className="text-sm text-muted">Save the product first, then you can add photos.</p>
        ) : (
          <>
            <div className="grid grid-cols-3 gap-3 sm:grid-cols-4 xl:grid-cols-5">
              {[...images]
                .sort((a, b) => a.position - b.position)
                .map((img, i) => (
                  <div key={img.id} className="relative aspect-square overflow-hidden rounded-control bg-paper ring-1 ring-line">
                    <img src={img.url} alt="" className="h-full w-full object-cover" />
                    {i === 0 && (
                      <span className="absolute bottom-1.5 left-1.5 rounded-full bg-ink/85 px-2 py-0.5 text-xs font-medium text-white">Cover</span>
                    )}
                    <button
                      type="button"
                      onClick={() => deleteImage(img)}
                      className="absolute top-0 right-0 flex h-11 w-11 items-center justify-center"
                      aria-label="Delete photo"
                    >
                      <span className="flex h-8 w-8 items-center justify-center rounded-full bg-surface/95 text-hibiscus-deep shadow-sm">
                        <TrashIcon width={16} height={16} />
                      </span>
                    </button>
                  </div>
                ))}
              {Array.from({ length: uploading }).map((_, i) => (
                <div key={`up-${i}`} className="flex aspect-square items-center justify-center rounded-control bg-paper text-subtle" role="status">
                  <Spinner />
                  <span className="sr-only">Uploading photo…</span>
                </div>
              ))}
              <label className="flex aspect-square cursor-pointer flex-col items-center justify-center gap-1.5 rounded-control border-2 border-dashed border-line-strong text-muted transition-colors hover:border-ink hover:text-ink has-[:focus-visible]:outline-3 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-ink">
                <ImageIcon />
                <span className="text-xs font-medium">Add photo</span>
                <input
                  type="file"
                  accept={ACCEPT}
                  multiple
                  className="sr-only"
                  onChange={(e) => {
                    onFiles(e.target.files);
                    e.target.value = "";
                  }}
                />
              </label>
            </div>
            {imageError && (typeof imageError === "string" ? <ErrorBox error={imageError} /> : <ApiErrorView error={imageError} />)}
          </>
        )}
      </FormSection>

      <FormSection title="Details" description="What customers see on the product page.">
        <Input label="Product name" required value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Ankara midi dress" />
        <Textarea label="Description" value={description} onChange={(e) => setDescription(e.target.value)} rows={4} />
        <Select
          label="Category"
          value={categoryId}
          onChange={(e) => setCategoryId(e.target.value)}
          hint={
            categories && categories.length === 0 ? (
              <>
                No categories yet.{" "}
                <Link href="/dashboard/categories" className="font-semibold text-ink underline underline-offset-2">
                  Create one
                </Link>
              </>
            ) : undefined
          }
        >
          <option value="">Uncategorised</option>
          {categories?.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </Select>
      </FormSection>

      <FormSection title="Price" description="Add a compare-at price to show a sale.">
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Price" inputMode="decimal" required value={price} onChange={(e) => setPrice(e.target.value)} placeholder="0" />
          <Input
            label="Compare-at price"
            inputMode="decimal"
            value={compareAt}
            onChange={(e) => setCompareAt(e.target.value)}
            placeholder="Optional"
            hint="Shown crossed out"
          />
        </div>
      </FormSection>

      <FormSection title="Stock and visibility">
        <Toggle label="In stock" description="Turn off to show “Out of stock”." checked={inStock} onChange={setInStock} />
        <Input
          label="Stock quantity (optional)"
          inputMode="numeric"
          value={stockQty}
          onChange={(e) => setStockQty(e.target.value.replace(/[^\d]/g, ""))}
          hint="Leave empty if you don't track stock."
        />
        <div className="border-t border-line pt-4">
          <Toggle label="Active" description="Hidden products don't appear in your store." checked={isActive} onChange={setIsActive} />
        </div>
      </FormSection>

      <FormSection
        title="Options"
        description="For example Size: S, M, L and Colour: Red, Black. A variant is created for each combination."
      >
        {options.length === 0 && <p className="text-sm text-muted">No options. Add one if this product comes in sizes or colours.</p>}
        {options.length > 0 && (
          <div className="space-y-4">
            {options.map((o, i) => (
              <div key={i} className="grid grid-cols-[minmax(0,1fr)_auto] items-end gap-2 sm:grid-cols-[minmax(0,1fr)_minmax(0,2fr)_auto]">
                <Input label="Option name" value={o.name} onChange={(e) => updateOption(i, { name: e.target.value })} placeholder="Size" />
                <div className="col-span-2 row-start-2 sm:col-span-1 sm:row-start-auto">
                  <Input
                    label="Values (comma-separated)"
                    value={o.values}
                    onChange={(e) => updateOption(i, { values: e.target.value })}
                    placeholder="S, M, L"
                  />
                </div>
                <button
                  type="button"
                  onClick={() => setOptions((list) => list.filter((_, j) => j !== i))}
                  className="col-start-2 row-start-1 flex h-11 w-11 items-center justify-center rounded-control text-hibiscus-deep hover:bg-hibiscus-soft sm:col-start-auto"
                  aria-label={`Remove option ${o.name || i + 1}`}
                >
                  <TrashIcon />
                </button>
              </div>
            ))}
          </div>
        )}
        {options.length < 3 && (
          <Button variant="secondary" onClick={() => setOptions((l) => [...l, { name: "", values: "" }])}>
            <PlusIcon width={18} height={18} /> Add option
          </Button>
        )}

        {combos.length > 0 && (
          <div className="border-t border-line pt-4">
            <h3 className="text-sm font-semibold text-ink">
              Variants <span className="font-normal text-muted num">({combos.length})</span>
            </h3>
            <p className="text-xs text-subtle">Leave price empty to use the product price.</p>
            <div className="mt-3 overflow-hidden rounded-control border border-line">
              <div className="hidden grid-cols-[minmax(0,1fr)_8rem_7rem] gap-3 bg-paper px-3 py-2 text-xs font-medium text-subtle sm:grid" aria-hidden="true">
                <span>Variant</span>
                <span>Price</span>
                <span>Stock</span>
              </div>
              <ul className="divide-y divide-line">
                {combos.map((combo) => {
                  const v = variantFor(combo);
                  const key = variantKey(combo);
                  return (
                    <li key={key} className="grid grid-cols-[minmax(0,1fr)_7rem] items-center gap-x-3 gap-y-1 px-3 py-2 sm:grid-cols-[minmax(0,1fr)_8rem_7rem]">
                      <span className="col-span-2 text-sm font-medium break-words text-ink sm:col-span-1">{key}</span>
                      <label className="sr-only" htmlFor={`vp-${key}`}>
                        Price for {key}
                      </label>
                      <input
                        id={`vp-${key}`}
                        inputMode="decimal"
                        value={v.price}
                        onChange={(e) => setVariant(combo, { price: e.target.value })}
                        placeholder={price || "Price"}
                        className={inputClass}
                      />
                      <label className="flex min-h-11 cursor-pointer items-center gap-2 text-sm text-ink-2">
                        <input
                          type="checkbox"
                          checked={v.inStock}
                          onChange={(e) => setVariant(combo, { inStock: e.target.checked })}
                          className="h-5 w-5 accent-ink"
                        />
                        In stock
                      </label>
                    </li>
                  );
                })}
              </ul>
            </div>
          </div>
        )}
      </FormSection>

      {error && <ApiErrorView error={error} />}
      {saved && <Notice tone="success">Saved.</Notice>}

      <div className="sticky bottom-[calc(4rem+1px+env(safe-area-inset-bottom))] z-20 -mx-4 border-y border-line bg-surface/95 px-4 py-3 backdrop-blur sm:-mx-6 sm:px-6 lg:bottom-4 lg:mx-0 lg:rounded-panel lg:border lg:px-4 lg:shadow-lift">
        <div className="flex items-center gap-2 lg:justify-end">
          {product && (
            <Button variant="danger" loading={deleting} onClick={deleteProduct} className="lg:mr-auto">
              Delete product
            </Button>
          )}
          <Button type="submit" loading={saving} className="flex-1 lg:flex-none">
            {product ? "Save changes" : "Save & add photos"}
          </Button>
        </div>
      </div>
    </form>
  );
}
