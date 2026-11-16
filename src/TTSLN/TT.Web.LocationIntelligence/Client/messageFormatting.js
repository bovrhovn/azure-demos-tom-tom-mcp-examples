import { marked } from "marked";
import DOMPurify from "dompurify";

export function formatMessage(content) {
  return DOMPurify.sanitize(marked.parse(content, { async: false, gfm: true }), {
    ALLOWED_TAGS: ["p", "br", "strong", "em", "del", "blockquote", "ul", "ol", "li",
      "h1", "h2", "h3", "h4", "h5", "h6", "pre", "code", "a", "hr",
      "table", "thead", "tbody", "tr", "th", "td"],
    ALLOWED_ATTR: ["href", "title", "start"]
  });
}

export async function copyMessage(content) {
  if (!navigator.clipboard) throw new Error("Clipboard access requires HTTPS and browser permission.");
  if (navigator.clipboard.write && typeof ClipboardItem !== "undefined" &&
      ClipboardItem.supports?.("text/html") !== false) {
    await navigator.clipboard.write([new ClipboardItem({
      "text/html": new Blob([formatMessage(content)], { type: "text/html" }),
      "text/plain": new Blob([content], { type: "text/plain" })
    })]);
  } else {
    await navigator.clipboard.writeText(content);
  }
}
