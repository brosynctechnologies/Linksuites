import { Fragment, type ReactNode } from "react";
import { cx } from "./styles";

/*
 * A deliberately small, safe Markdown renderer for course descriptions and lesson bodies.
 * It never produces HTML strings: input is parsed into React elements, so any HTML in the
 * source is shown as text. Supported: # headings (1–4), paragraphs, **bold**, *italic* / _italic_,
 * `inline code`, ``` fenced code ```, - / * / 1. lists, > quotes, [links](https://…) and line breaks.
 */

type Block =
  | { kind: "heading"; level: number; text: string }
  | { kind: "para"; text: string }
  | { kind: "code"; text: string }
  | { kind: "quote"; text: string }
  | { kind: "list"; ordered: boolean; items: string[] }
  | { kind: "rule" };

function parseBlocks(src: string): Block[] {
  const lines = src.replace(/\r\n?/g, "\n").split("\n");
  const blocks: Block[] = [];
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];
    if (!line.trim()) {
      i++;
      continue;
    }
    const fence = line.match(/^\s*```/);
    if (fence) {
      const body: string[] = [];
      i++;
      while (i < lines.length && !/^\s*```/.test(lines[i])) body.push(lines[i++]);
      i++;
      blocks.push({ kind: "code", text: body.join("\n") });
      continue;
    }
    const h = line.match(/^(#{1,4})\s+(.*)$/);
    if (h) {
      blocks.push({ kind: "heading", level: h[1].length, text: h[2].trim() });
      i++;
      continue;
    }
    if (/^\s*([-*_])(\s*\1){2,}\s*$/.test(line)) {
      blocks.push({ kind: "rule" });
      i++;
      continue;
    }
    if (/^\s*>/.test(line)) {
      const body: string[] = [];
      while (i < lines.length && /^\s*>/.test(lines[i])) body.push(lines[i++].replace(/^\s*>\s?/, ""));
      blocks.push({ kind: "quote", text: body.join("\n") });
      continue;
    }
    const ul = /^\s*[-*+]\s+/;
    const ol = /^\s*\d+[.)]\s+/;
    if (ul.test(line) || ol.test(line)) {
      const ordered = ol.test(line);
      const re = ordered ? ol : ul;
      const items: string[] = [];
      while (i < lines.length && lines[i].trim()) {
        if (re.test(lines[i])) items.push(lines[i].replace(re, ""));
        else if (items.length && /^\s+/.test(lines[i])) items[items.length - 1] += ` ${lines[i].trim()}`;
        else break;
        i++;
      }
      blocks.push({ kind: "list", ordered, items });
      continue;
    }
    const body: string[] = [];
    while (
      i < lines.length &&
      lines[i].trim() &&
      !/^(#{1,4})\s+/.test(lines[i]) &&
      !/^\s*```/.test(lines[i]) &&
      !/^\s*>/.test(lines[i]) &&
      !ul.test(lines[i]) &&
      !ol.test(lines[i])
    ) {
      body.push(lines[i++]);
    }
    blocks.push({ kind: "para", text: body.join("\n") });
  }
  return blocks;
}

/** Only http(s), mailto and same-site paths survive; anything else (javascript:, data:) is dropped. */
export function safeHref(url: string): string | null {
  const u = url.trim();
  if (/^https?:\/\//i.test(u) || /^mailto:/i.test(u)) return u;
  if (u.startsWith("/") && !u.startsWith("//")) return u;
  if (u.startsWith("#")) return u;
  return null;
}

const INLINE = /(`[^`]+`)|(\*\*[^*]+\*\*|__[^_]+__)|(\*[^*\s][^*]*\*|_[^_\s][^_]*_)|(\[[^\]]+\]\([^)\s]+\))|(https?:\/\/[^\s<]+[^\s<.,;:!?)\]'"])|(\n)/g;

function inline(text: string, keyBase: string): ReactNode[] {
  const out: ReactNode[] = [];
  let last = 0;
  let m: RegExpExecArray | null;
  let n = 0;
  INLINE.lastIndex = 0;
  const re = new RegExp(INLINE.source, "g");
  while ((m = re.exec(text))) {
    if (m.index > last) out.push(text.slice(last, m.index));
    const k = `${keyBase}-${n++}`;
    const [tok] = m;
    if (m[1]) {
      out.push(
        <code key={k} className="rounded-[4px] bg-ink/[0.06] px-1 py-0.5 font-mono text-[0.9em]">
          {tok.slice(1, -1)}
        </code>,
      );
    } else if (m[2]) {
      out.push(<strong key={k}>{inline(tok.slice(2, -2), k)}</strong>);
    } else if (m[3]) {
      out.push(<em key={k}>{inline(tok.slice(1, -1), k)}</em>);
    } else if (m[4]) {
      const lm = tok.match(/^\[([^\]]+)\]\(([^)\s]+)\)$/);
      const href = lm ? safeHref(lm[2]) : null;
      if (lm && href) {
        const external = /^https?:/i.test(href);
        out.push(
          <a
            key={k}
            href={href}
            className="font-medium text-lagoon-text underline underline-offset-2"
            {...(external ? { target: "_blank", rel: "noopener noreferrer nofollow" } : {})}
          >
            {inline(lm[1], k)}
          </a>,
        );
      } else {
        out.push(lm ? lm[1] : tok);
      }
    } else if (m[5]) {
      out.push(
        <a key={k} href={tok} target="_blank" rel="noopener noreferrer nofollow" className="break-all font-medium text-lagoon-text underline underline-offset-2">
          {tok}
        </a>,
      );
    } else if (m[6]) {
      out.push(<br key={k} />);
    }
    last = m.index + tok.length;
  }
  if (last < text.length) out.push(text.slice(last));
  return out;
}

/**
 * Render Markdown safely. `variant="lesson"` is the reading face (Literata, 68ch, 1.0625rem/1.7);
 * `variant="ui"` is compact Figtree for descriptions inside the UI.
 */
export function Markdown({ source, variant = "ui", className }: { source: string | null | undefined; variant?: "lesson" | "ui"; className?: string }) {
  if (!source || !source.trim()) return null;
  const blocks = parseBlocks(source);
  const lesson = variant === "lesson";
  return (
    <div className={cx(lesson ? "prose-lesson" : "prose-ui", className)}>
      {blocks.map((b, i) => {
        const k = `b${i}`;
        switch (b.kind) {
          case "heading": {
            const Tag = (`h${Math.min(4, b.level + 1)}` as "h2" | "h3" | "h4");
            return <Tag key={k}>{inline(b.text, k)}</Tag>;
          }
          case "para":
            return <p key={k}>{inline(b.text, k)}</p>;
          case "code":
            return (
              <pre key={k}>
                <code>{b.text}</code>
              </pre>
            );
          case "quote":
            return <blockquote key={k}>{inline(b.text, k)}</blockquote>;
          case "rule":
            return <hr key={k} />;
          case "list": {
            const items = b.items.map((it, j) => <li key={`${k}-${j}`}>{inline(it, `${k}-${j}`)}</li>);
            return b.ordered ? <ol key={k}>{items}</ol> : <ul key={k}>{items}</ul>;
          }
          default:
            return <Fragment key={k} />;
        }
      })}
    </div>
  );
}
