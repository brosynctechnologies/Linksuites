/**
 * The suite components import product data from here, so the same component files work in every LinkSuite site.
 * In the hub this re-exports products.ts. In each app it is a verbatim copy of hub-web/src/lib/products.ts.
 */
export * from "./products";
