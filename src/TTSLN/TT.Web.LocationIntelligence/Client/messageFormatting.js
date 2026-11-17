import { marked } from "marked";
import DOMPurify from "dompurify";

const MAX_CACHE_ENTRIES = 200;
const SANITIZE_OPTIONS = {
  ALLOWED_TAGS: ["p", "br", "strong", "em", "del", "blockquote", "ul", "ol", "li",
    "h1", "h2", "h3", "h4", "h5", "h6", "pre", "code", "a", "hr",
    "table", "thead", "tbody", "tr", "th", "td"],
  ALLOWED_ATTR: ["href", "title", "start"]
};

const formatCache = new Map();

export function formatMessage(content) {
  const key = String(content ?? "");
  if (formatCache.has(key)) {
    const cached = formatCache.get(key);
    formatCache.delete(key);
    formatCache.set(key, cached);
    return cached;
  }

  const sanitized = DOMPurify.sanitize(marked.parse(key, { async: false, gfm: true }), SANITIZE_OPTIONS);
  if (formatCache.size >= MAX_CACHE_ENTRIES) {
    formatCache.delete(formatCache.keys().next().value);
  }
  formatCache.set(key, sanitized);
  return sanitized;
}

export function clearFormatMessageCache() {
  formatCache.clear();
}

export async function copyMessage(content, preformattedHtml) {
  if (!navigator.clipboard) throw new Error("Clipboard access requires HTTPS and browser permission.");
  if (navigator.clipboard.write && typeof ClipboardItem !== "undefined" &&
      ClipboardItem.supports?.("text/html") !== false) {
    const html = preformattedHtml ?? formatMessage(content);
    await navigator.clipboard.write([new ClipboardItem({
      "text/html": new Blob([html], { type: "text/html" }),
      "text/plain": new Blob([content], { type: "text/plain" })
    })]);
  } else {
    await navigator.clipboard.writeText(content);
  }
}
