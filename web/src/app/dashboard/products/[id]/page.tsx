"use client";

import { useParams } from "next/navigation";
import { useState } from "react";
import { useApi } from "@/lib/use-api";
import type { ProductDto } from "@/lib/types";
import { ApiErrorView, PageHeader, PageLoader } from "@/components/ui";
import { ProductForm } from "../product-form";

export default function EditProductPage() {
  const { id } = useParams<{ id: string }>();
  const { data: product, error, reload } = useApi<ProductDto>(`/api/products/${id}`);
  const [justCreated] = useState(
    () => typeof window !== "undefined" && new URLSearchParams(window.location.search).has("created"),
  );

  return (
    <div>
      <PageHeader title={product?.name ?? "Edit product"} back="/dashboard/products" />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!product && !error && <PageLoader />}
      {product && <ProductForm key={product.id} product={product} justCreated={justCreated} />}
    </div>
  );
}
