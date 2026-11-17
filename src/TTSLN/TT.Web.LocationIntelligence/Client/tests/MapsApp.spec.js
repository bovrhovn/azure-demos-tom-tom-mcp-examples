import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import MapsApp from "../MapsApp.vue";

const realtime = vi.hoisted(() => ({ observer: null, reconnecting: null, reconnected: null, close: null,
  dispose: vi.fn(), stop: vi.fn(), stream: vi.fn() }));
vi.mock("@microsoft/signalr", () => ({
  LogLevel: { Warning: 3 },
  HubConnectionBuilder: class {
    withUrl() { return this; }
    withAutomaticReconnect() { return this; }
    configureLogging() { return this; }
    build() {
      return {
        start: async () => {}, stop: realtime.stop,
        onreconnecting: (fn) => { realtime.reconnecting = fn; },
        onreconnected: (fn) => { realtime.reconnected = fn; },
        onclose: (fn) => { realtime.close = fn; },
        stream: (...args) => {
          realtime.stream(...args);
          return { subscribe: (observer) => { realtime.observer = observer; return { dispose: realtime.dispose }; } };
        }
      };
    }
  }
}));
const defaultPrompt = "Find the fastest route from Amsterdam to Berlin, avoiding toll roads,\nand show me gas stations along the way.";
let fetcher;
beforeEach(() => {
  realtime.stream.mockClear();
  realtime.dispose.mockClear();
  realtime.stop.mockClear();
  realtime.stop.mockResolvedValue(undefined);
  fetcher = vi.fn(async (url) => {
    if (url === "/api/runtime") return Response.json({ agentConfigured: true, csrfToken: "csrf" });
    if (url === "/api/maps/config") return Response.json({
      defaultPrompt, maxMessageLength: 8000, queriesPath: "/api/maps/queries", appToolsPath: "/api/maps/app-tools",
      hubPath: "/hubs/maps", appInitializationTimeoutMilliseconds: 30000
    });
    if (url === "/api/maps/queries") return Response.json({ ticket: "protected-ticket" });
    throw new Error(`Unexpected request: ${url}`);
  });
  vi.stubGlobal("fetch", fetcher);
});
afterEach(() => vi.unstubAllGlobals());

it("prefills the route question, links from the header and streams text, tools and sandboxed MCP apps", async () => {
  const wrapper = mount(MapsApp);
  await flushPromises();
  expect(wrapper.get("textarea").element.value).toBe(defaultPrompt);
  expect(wrapper.get('a[href="/Maps"]').attributes("aria-current")).toBe("page");
  await wrapper.get("form").trigger("submit");
  await flushPromises();
  const request = fetcher.mock.calls.find(([url]) => url === "/api/maps/queries")[1];
  expect(JSON.parse(request.body)).toEqual({ prompt: defaultPrompt });
  expect(request.headers["X-CSRF-TOKEN"]).toBe("csrf");
  expect(realtime.stream).toHaveBeenCalledWith("Query", "protected-ticket");
  const emit = realtime.observer.next;
  emit({ type: "tool-start", data: { callId: "route", name: "tomtom-routing", arguments: { avoid: "tollRoads" } } });
  emit({ type: "tool-result", data: { callId: "route", result: { content: [{ text: "650 km" }] } } });
  emit({ type: "text", data: "**Fastest** " });
  await flushPromises();
  expect(wrapper.get(".maps-answer strong").text()).toBe("Fastest");
  emit({ type: "text", data: "route<script>alert(1)</script>" });
  emit({ type: "app", data: { callId: "route", name: "tomtom-routing", html: "<h1>Map</h1>",
    arguments: {}, result: { content: [], _meta: { geometry: "complete" } } } });
  emit({ type: "completed", data: null });
  await flushPromises();
  expect(wrapper.get(".maps-activity-item.completed").text()).toContain("routing");
  expect(wrapper.get("iframe").attributes("sandbox")).toBe("allow-scripts");
  expect(wrapper.get("iframe").element.srcdoc).toContain("Content-Security-Policy");
  expect(wrapper.find(".maps-answer script").exists()).toBe(false);
  expect(wrapper.get(".maps-answer").text()).toContain("Fastest route");
  wrapper.get(".maps-event-log").element.open = true;
  await wrapper.get(".maps-event-log").trigger("toggle");
  expect(wrapper.get(".maps-event-log").text()).toContain('"geometry": "complete"');
  expect(wrapper.text()).toContain("Exploration complete");
  wrapper.unmount();
  expect(realtime.stop).toHaveBeenCalled();
});

it("stops a query, ignores late data and never automatically replays after reconnecting", async () => {
  const wrapper = mount(MapsApp);
  await flushPromises();
  await wrapper.get("form").trigger("submit");
  await flushPromises();
  const old = realtime.observer;
  old.next({ type: "text", data: "Partial answer" });
  await wrapper.get(".maps-stop").trigger("click");
  old.next({ type: "text", data: "stale result" });
  await flushPromises();
  expect(wrapper.text()).toContain("Partial answer");
  expect(wrapper.text()).not.toContain("stale result");
  expect(realtime.dispose).toHaveBeenCalledTimes(1);
  await wrapper.get("form").trigger("submit");
  await flushPromises();
  realtime.reconnecting();
  realtime.reconnected();
  await flushPromises();
  expect(realtime.stream).toHaveBeenCalledTimes(2);
  expect(wrapper.text()).toContain("queries are not replayed");
  wrapper.unmount();
});

it("shows explicit stream and provider failures without claiming completion", async () => {
  const wrapper = mount(MapsApp);
  await flushPromises();
  await wrapper.get("form").trigger("submit");
  await flushPromises();
  realtime.observer.next({ type: "text", data: "Partial" });
  realtime.observer.next({ type: "error", data: "Provider unavailable" });
  realtime.observer.complete();
  await flushPromises();
  expect(wrapper.get('[role="alert"]').text()).toBe("Provider unavailable");
  expect(wrapper.text()).not.toContain("Exploration complete");
  await wrapper.get("form").trigger("submit");
  await flushPromises();
  realtime.observer.complete();
  await flushPromises();
  expect(wrapper.get('[role="alert"]').text()).toContain("before the query completed");
  wrapper.unmount();
});
