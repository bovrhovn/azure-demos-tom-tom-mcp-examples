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
  fetcher = vi.fn(async (url, request = {}) => {
    const method = request.method ?? "GET";
    if (url === "/api/runtime") return Response.json({
      csrfToken: "token", maxTitleLength: 120, maxMessageLength: 4000, maxTurns: 40,
      agentConfigured: true, environment: "Test"
    });
    if (url === "/api/sessions" && method === "GET") {
      return Response.json({ items: stored ? [stored] : [], continuationToken: null });
    }
    if (url === path && method === "GET") return stored
      ? Response.json(stored, { headers: { ETag: `"etag-${stored.updatedAt}"` } })
      : Response.json({ title: "Not found", code: "session_not_found" }, { status: 404 });
    if (url === path && method === "PUT") {
      stored = { ...stored, ...JSON.parse(request.body), updatedAt: "2026-01-02T10:00:00Z" };
      return Response.json(stored, { headers: { ETag: `"etag-${stored.updatedAt}"` } });
    }
    if (url === path && method === "DELETE") {
      stored = null;
      return new Response(null, { status: 204 });
    }
    if (url === `${path}/messages` && method === "POST") {
      const body = JSON.parse(request.body);
      stored = {
        ...stored,
        updatedAt: "2026-01-03T10:00:00Z",
        messages: [
          ...stored.messages,
          { id: `${body.requestId}:user`, role: "user", status: "completed", content: body.content, createdAt: "2026-01-03T10:00:00Z" },
          { id: `${body.requestId}:assistant`, role: "assistant", status: "completed", content: "Reply", createdAt: "2026-01-03T10:00:01Z" }
        ]
      };
      return Response.json(stored, { headers: { ETag: `"etag-${stored.updatedAt}"` } });
    }
    throw new Error(`Unexpected request: ${method} ${url}`);
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

it("hydrates from embedded #tt-runtime without fetching /api/runtime and skips duplicate GETs on send and matching SignalR timestamps", async () => {
  const script = document.createElement("script");
  script.id = "tt-runtime";
  script.type = "application/json";
  script.textContent = JSON.stringify({
    csrfToken: "embedded-token",
    maxTitleLength: 120,
    maxMessageLength: 4000,
    maxTurns: 40,
    agentConfigured: true,
    environment: "Development",
    endpoints: { runtime: "/api/runtime", sessions: "/api/sessions", chatHub: "/hubs/chat" }
  });
  document.body.appendChild(script);

  const wrapper = mount(ChatApp, { attachTo: document.body });
  await flushPromises();

  expect(fetcher.mock.calls.some(([url]) => url === "/api/runtime")).toBe(false);
  const getsAfterBoot = fetcher.mock.calls.filter(([url, req]) => url === path && (req?.method ?? "GET") === "GET").length;
  expect(getsAfterBoot).toBe(1);

  // Matching updatedAt from SignalR skips network GET completely.
  await realtime.handlers.ConversationChanged("one", "2026-01-01T10:00:00Z");
  await flushPromises();
  expect(fetcher.mock.calls.filter(([url, req]) => url === path && (req?.method ?? "GET") === "GET")).toHaveLength(1);

  // IME composition Enter must not submit.
  await wrapper.get("textarea").setValue("Traffic near Utrecht");
  await wrapper.get("textarea").trigger("keydown.enter", { isComposing: true });
  expect(fetcher.mock.calls.some(([url]) => url === `${path}/messages`)).toBe(false);

  // Non-IME Enter sends once and applies response directly without a trailing refresh GET.
  await wrapper.get("textarea").trigger("keydown.enter");
  await flushPromises();
  expect(fetcher.mock.calls.filter(([url]) => url === `${path}/messages`)).toHaveLength(1);
  expect(fetcher.mock.calls.filter(([url, req]) => url === path && (req?.method ?? "GET") === "GET")).toHaveLength(1);
  expect(wrapper.findAll(".message-row")).toHaveLength(4);
});

it("disables the composer with a clear notice when the conversation reaches maxTurns", async () => {
  const script = document.createElement("script");
  script.id = "tt-runtime";
  script.type = "application/json";
  script.textContent = JSON.stringify({
    csrfToken: "embedded-token",
    maxTitleLength: 120,
    maxMessageLength: 4000,
    maxTurns: 1,
    agentConfigured: true,
    environment: "Test"
  });
  document.body.appendChild(script);

  const wrapper = mount(ChatApp, { attachTo: document.body });
  await flushPromises();

  expect(wrapper.get("textarea").element.disabled).toBe(true);
  expect(wrapper.get(".composer-notice").text()).toContain("1-turn limit");
});
