import { cx } from "./styles";

/** The PropertyLink wordmark: Archivo at signboard width. Used inside the studio and on the auth pages. */
export function Wordmark({ className, dark }: { className?: string; dark?: boolean }) {
  return (
    <span className={cx("display-wide text-[1.375rem] leading-none font-bold", dark ? "text-white" : "text-asphalt", className)}>
      Property<span className={dark ? "text-adire-light" : "text-adire"}>Link</span>
    </span>
  );
}
