import { beforeEach, afterEach, expect, it, vi } from "vitest";
import { DOMWrapper, flushPromises, mount } from "@vue/test-utils";
import ChatApp from "../ChatApp.vue";

const realtime = vi.hoisted(() => ({ handlers: {}, reconnected: null }));
vi.mock("@microsoft/signalr", () => ({
  LogLevel: { Warning: 3 },
  HubConnectionBuilder: class {
    withUrl() { return this; }
    withAutomaticReconnect() { return this; }
    configureLogging() { return this; }
    build() {
      return {
        on: (event, handler) => { realtime.handlers[event] = handler; },
        onreconnecting() {},
        onclose() {},
        onreconnected: (handler) => { realtime.reconnected = handler; },
        start: async () => {},
        stop: async () => {}
      };
    }
  }
}));

let stored;
let fetcher;
const path = "/api/sessions/one";
const dialog = () => new DOMWrapper(document.querySelector("dialog[open]"));
beforeEach(() => {
  vi.stubGlobal("localStorage", { getItem: vi.fn(() => null), setItem: vi.fn() });
  realtime.handlers = {};
  realtime.reconnected = null;
  Element.prototype.scrollTo = vi.fn();
  stored = {
    id: "one", title: "Amsterdam", createdAt: "2026-01-01T10:00:00Z",
    updatedAt: "2026-01-01T10:00:00Z", busyUntil: null,
    messages: [
      { id: "user", role: "user", status: "completed", content: "**Question**", createdAt: "2026-01-01T10:00:00Z" },
      { id: "agent", role: "assistant", status: "completed", content: "**Answer**", createdAt: "2026-01-01T10:00:01Z" }
    ]
  };
  fetcher = vi.fn(async (url, request) => {
    if (url === "/api/runtime") return Response.json({
      csrfToken: "token", maxTitleLength: 120, agentConfigured: true, environment: "Test"
    });
    if (url === "/api/sessions/") return Response.json({ items: stored ? [stored] : [], continuationToken: null });
    if (url === path && request.method === "GET") return stored
      ? Response.json(stored) : Response.json({ title: "Not found" }, { status: 404 });
    if (url === path && request.method === "PUT") {
      stored = { ...stored, ...JSON.parse(request.body), updatedAt: "2026-01-02T10:00:00Z" };
      return Response.json(stored);
    }
    if (url === path && request.method === "DELETE") {
      stored = null;
      return new Response(null, { status: 204 });
    }
    throw new Error(`Unexpected request: ${request.method} ${url}`);
  });
  vi.stubGlobal("fetch", fetcher);
});
afterEach(() => { vi.unstubAllGlobals(); });

it("updates the selected header and sidebar after an edit and clears them after confirmed deletion", async () => {
  const wrapper = mount(ChatApp, { attachTo: document.body });
  await flushPromises();
  expect(wrapper.get("h1").text()).toBe("Amsterdam");
  expect(wrapper.get(".user .message-bubble").text()).toBe("**Question**");
  expect(wrapper.get(".formatted-message strong").text()).toBe("Answer");
  expect(wrapper.findAll('[aria-label="Copy agent message"]')).toHaveLength(1);
  await wrapper.get('[aria-label="Edit Amsterdam"]').trigger("click");
  await dialog().get("input").setValue("Rotterdam");
  await dialog().get("form").trigger("submit");
  await flushPromises();
  expect(wrapper.get("h1").text()).toBe("Rotterdam");
  expect(wrapper.get(".session strong").text()).toBe("Rotterdam");
  expect(wrapper.findAll(".message-row")).toHaveLength(2);
  await wrapper.get("textarea").setValue("Unsent draft");
  await wrapper.get('[aria-label="Delete Rotterdam"]').trigger("click");
  expect(wrapper.findAll(".session-row")).toHaveLength(1);
  await dialog().get("form").trigger("submit");
  await flushPromises();
  expect(wrapper.findAll(".session-row")).toHaveLength(0);
  expect(wrapper.get("h1").text()).toBe("Your world. Connected.");
  expect(wrapper.get("textarea").element.value).toBe("");
  expect(wrapper.get("textarea").element.disabled).toBe(true);
});

it("does not resurrect a deleted conversation when an older refresh finishes", async () => {
  const wrapper = mount(ChatApp, { attachTo: document.body });
  await flushPromises();
  let release;
  const staleResponse = Response.json(stored);
  fetcher.mockImplementationOnce(() => new Promise((resolve) => { release = resolve; }));
  realtime.handlers.ConversationChanged("one");
  realtime.handlers.ConversationDeleted("one");
  release(staleResponse);
  await flushPromises();
  expect(wrapper.findAll(".session-row")).toHaveLength(0);
  expect(wrapper.get("h1").text()).toBe("Your world. Connected.");
  expect(wrapper.find('[role="alert"]').exists()).toBe(false);
});

it("reconciles deletion missed while realtime was disconnected", async () => {
  const wrapper = mount(ChatApp, { attachTo: document.body });
  await flushPromises();
  stored = null;
  await realtime.reconnected();
  await flushPromises();
  expect(wrapper.findAll(".session-row")).toHaveLength(0);
  expect(wrapper.get("h1").text()).toBe("Your world. Connected.");
  expect(wrapper.find('[role="alert"]').exists()).toBe(false);
});
