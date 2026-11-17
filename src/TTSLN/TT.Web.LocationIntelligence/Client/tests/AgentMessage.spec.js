import { afterEach, expect, it, vi } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import DOMPurify from "dompurify";
import { marked } from "marked";
import AgentMessage from "../AgentMessage.vue";
import { clearFormatMessageCache, copyMessage, formatMessage } from "../messageFormatting.js";

afterEach(() => {
  clearFormatMessageCache();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});
const clipboard = (value) => vi.stubGlobal("navigator", { clipboard: value });

it("renders Markdown, code and tables but strips unsafe agent HTML and links", () => {
  const wrapper = mount(AgentMessage, { props: { content:
    "# Route\n\n**Amsterdam**\n\n- Walk\n- Cycle\n\n```js\nconst km = 5;\n```\n\n" +
    "| Place | km |\n| --- | --- |\n| Home | 5 |\n\n" +
    '<script>alert(1)</script><img src=x onerror=alert(1)><a href="javascript:alert(1)">unsafe</a>' } });
  expect(wrapper.get("h1").text()).toBe("Route");
  expect(wrapper.get("strong").text()).toBe("Amsterdam");
  expect(wrapper.findAll("li").map((item) => item.text())).toEqual(["Walk", "Cycle"]);
  expect(wrapper.get("pre code").text()).toContain("const km = 5;");
  expect(wrapper.get("tbody td").text()).toBe("Home");
  expect(wrapper.find("script").exists()).toBe(false);
  expect(wrapper.find("img").exists()).toBe(false);
  expect(wrapper.get("a").attributes("href")).toBeUndefined();
});

it("copies sanitized rich HTML and the complete original plain-text message", async () => {
  const write = vi.fn().mockResolvedValue(undefined);
  clipboard({ write });
  vi.stubGlobal("ClipboardItem", class {
    constructor(items) { this.items = items; }
    static supports() { return true; }
  });
  const content = "**Amsterdam**\n\n1. Walk\n2. Cycle\n<script>alert(1)</script>";
  const wrapper = mount(AgentMessage, { props: { content } });
  await wrapper.get("button").trigger("click");
  await flushPromises();
  const items = write.mock.calls[0][0][0].items;
  const readBlob = (blob) => new Promise((resolve) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result);
    reader.readAsText(blob);
  });
  expect(await readBlob(items["text/plain"])).toBe(content);
  expect(await readBlob(items["text/html"])).toContain("<strong>Amsterdam</strong>");
  expect(await readBlob(items["text/html"])).not.toContain("<script>");
  expect(wrapper.get('[role="status"]').text()).toBe("Copied");
  expect(wrapper.find('svg[data-icon="circle-check"]').exists()).toBe(true);
});

it("falls back to plain text when rich clipboard is unavailable", async () => {
  const writeText = vi.fn().mockResolvedValue(undefined);
  clipboard({ writeText });
  vi.stubGlobal("ClipboardItem", undefined);
  const wrapper = mount(AgentMessage, { props: { content: "**Routes**\n\nKeep line breaks." } });
  await wrapper.get("button").trigger("click");
  await flushPromises();
  expect(writeText).toHaveBeenCalledExactlyOnceWith("**Routes**\n\nKeep line breaks.");
  expect(wrapper.get('[role="status"]').text()).toBe("Copied");
});

it("shows a clipboard permission error instead of reporting success", async () => {
  clipboard({ writeText: vi.fn().mockRejectedValue(new Error("Permission denied")) });
  vi.stubGlobal("ClipboardItem", undefined);
  const wrapper = mount(AgentMessage, { props: { content: "Route" } });
  await wrapper.get("button").trigger("click");
  await flushPromises();
  expect(wrapper.get('[role="alert"]').text()).toContain("Permission denied");
  expect(wrapper.get('[role="status"]').text()).toBe("");
  expect(wrapper.get("button").element.disabled).toBe(false);
});

it("caches sanitized HTML in an LRU cache and evicts the least-recently-used entry past 200 items", () => {
  clearFormatMessageCache();
  const parseSpy = vi.spyOn(marked, "parse");
  const sanitizeSpy = vi.spyOn(DOMPurify, "sanitize");

  const first = formatMessage("**Delft** <script>alert(1)</script>");
  const second = formatMessage("**Delft** <script>alert(1)</script>");
  expect(second).toBe(first);
  expect(first).toContain("<strong>Delft</strong>");
  expect(first).not.toContain("<script>");
  expect(parseSpy).toHaveBeenCalledTimes(1);
  expect(sanitizeSpy).toHaveBeenCalledTimes(1);

  clearFormatMessageCache();
  parseSpy.mockClear();

  for (let index = 0; index < 200; index++) {
    formatMessage(`message-${index}`);
  }
  expect(parseSpy).toHaveBeenCalledTimes(200);

  // Touch message-0 so message-1 becomes the least-recently-used entry.
  formatMessage("message-0");
  expect(parseSpy).toHaveBeenCalledTimes(200);

  // Inserting a 201st entry evicts message-1 while retaining message-0.
  formatMessage("message-200");
  expect(parseSpy).toHaveBeenCalledTimes(201);

  formatMessage("message-0");
  expect(parseSpy).toHaveBeenCalledTimes(201);

  formatMessage("message-1");
  expect(parseSpy).toHaveBeenCalledTimes(202);
});

it("copies preformatted HTML without re-parsing Markdown or re-running DOMPurify", async () => {
  const write = vi.fn().mockResolvedValue(undefined);
  clipboard({ write });
  vi.stubGlobal("ClipboardItem", class {
    constructor(items) { this.items = items; }
    static supports() { return true; }
  });

  const parseSpy = vi.spyOn(marked, "parse");
  const sanitizeSpy = vi.spyOn(DOMPurify, "sanitize");

  await copyMessage("**Utrecht**", "<p><strong>Utrecht</strong></p>");
  expect(parseSpy).not.toHaveBeenCalled();
  expect(sanitizeSpy).not.toHaveBeenCalled();

  const items = write.mock.calls[0][0][0].items;
  const readBlob = (blob) => new Promise((resolve) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result);
    reader.readAsText(blob);
  });
  expect(await readBlob(items["text/html"])).toBe("<p><strong>Utrecht</strong></p>");
  expect(await readBlob(items["text/plain"])).toBe("**Utrecht**");

  const wrapper = mount(AgentMessage, { props: { content: "**Haarlem**" } });
  expect(parseSpy).toHaveBeenCalledTimes(1);
  await wrapper.get("button").trigger("click");
  await flushPromises();
  expect(parseSpy).toHaveBeenCalledTimes(1);
});
