import { products } from "@/lib/suite";

/**
 * The LinkSuite mark: a small signboard painted in the seven app colours, in hub order. Decorative; the text beside it
 * (or the link's label) carries the name.
 */
export function SuiteMark({ size = 22, className }: { size?: number; className?: string }) {
  const w = 22 / products.length;
  return (
    <svg
      viewBox="0 0 22 22"
      width={size}
      height={size}
      aria-hidden="true"
      className={className}
      style={{ borderRadius: size * 0.23, flexShrink: 0 }}
    >
      {products.map((p, i) => (
        <rect key={p.key} x={i * w} y="0" width={w + 0.05} height="22" fill={p.accent} />
      ))}
    </svg>
  );
}
