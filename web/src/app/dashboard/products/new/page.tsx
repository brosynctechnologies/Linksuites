"use client";

import { PageHeader } from "@/components/ui";
import { ProductForm } from "../product-form";

export default function NewProductPage() {
  return (
    <div>
      <PageHeader title="New product" back="/dashboard/products" />
      <ProductForm />
    </div>
  );
}
