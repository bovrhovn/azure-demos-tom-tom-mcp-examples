import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import TravelApp from "../TravelApp.vue";

const realtime = vi.hoisted(() => ({ observer: null, reconnecting: null, reconnected: null,
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
        onreconnecting: fn => { realtime.reconnecting = fn; },
        onreconnected: fn => { realtime.reconnected = fn; },
        onclose: () => {},
        stream: (...args) => {
          realtime.stream(...args);
          return { subscribe: observer => { realtime.observer = observer; return { dispose: realtime.dispose }; } };
        }
      };
    }
  }
}));
let fetcher;
beforeEach(() => {
  realtime.stream.mockClear();
  realtime.dispose.mockClear();
  realtime.stop.mockResolvedValue(undefined);
  fetcher = vi.fn(async url => {
    if (url === "/api/runtime") return Response.json({ csrfToken: "csrf" });
    if (url === "/api/travel/config") return Response.json({
      configured: true, minDate: "2026-11-18", maxDate: "2026-11-24",
      countrySource: "https://european-union.europa.eu/",
      countries: [{ code: "NL", name: "Netherlands", capital: "Amsterdam" }, { code: "DE", name: "Germany", capital: "Berlin" }],
      queriesPath: "/api/travel/queries", hubPath: "/hubs/travel"
    });
    if (url === "/api/travel/queries") return Response.json({ ticket: "protected-ticket" });
    throw new Error(`Unexpected request: ${url}`);
  });
  vi.stubGlobal("fetch", fetcher);
});
afterEach(() => vi.unstubAllGlobals());

async function planner(scenario = "handoff") {
  const wrapper = mount(TravelApp, { props: { scenario } });
  await flushPromises();
  await wrapper.get("#travel-start").setValue("NL");
  await wrapper.get("#travel-destination").setValue("DE");
  await wrapper.get("form").trigger("submit");
  await flushPromises();
  return wrapper;
}

it("offers two real scenario pages without initializing providers on the landing page", async () => {
  const wrapper = mount(TravelApp);
  await flushPromises();
  expect(wrapper.get('a[href="/Travel/Handoff"]').text()).toContain("Explore Handoff");
  expect(wrapper.get('a[href="/Travel/Magentic"]').text()).toContain("Explore Magentic");
  expect(fetcher).not.toHaveBeenCalled();
  wrapper.unmount();
});

it.each(["handoff", "magentic"])("submits %s through the CSRF API and displays weather, live findings and sanitized results", async scenario => {
  const wrapper = await planner(scenario);
  const request = fetcher.mock.calls.find(([url]) => url === "/api/travel/queries")[1];
  expect(JSON.parse(request.body)).toEqual({ scenario, startCountry: "NL", destinationCountry: "DE", departureDate: "2026-11-18" });
  expect(request.headers["X-CSRF-TOKEN"]).toBe("csrf");
  expect(realtime.stream).toHaveBeenCalledWith("Plan", "protected-ticket");
  realtime.observer.next({ type: "plan", agent: "TravelManager", data: "**Plan:** check weather then route" });
  realtime.observer.next({ type: "task", agent: "TravelManager", data: { nextAgent: "WeatherChecker", instruction: "Check both capitals" } });
  realtime.observer.next({ type: "weather", agent: "WeatherChecker", data: {
    location: "Amsterdam", condition: "bad", summary: "Heavy rain. Consider postponing.", sources: ["https://weather.example"]
  } });
  realtime.observer.next({ type: "result", agent: "RoutePlanner", data: { text: "**Route**<script>alert(1)</script>", outcome: "itinerary" } });
  realtime.observer.next({ type: "completed", agent: "system", data: null });
  await flushPromises();
  expect(wrapper.get(".travel-weather").text()).toContain("WEATHER ADVISORY");
  expect(wrapper.get(".travel-weather").text()).toContain("Heavy rain");
  expect(wrapper.get(".travel-timeline").text()).toContain("Check both capitals");
  expect(wrapper.get(".travel-plan strong").text()).toBe("Plan:");
  expect(wrapper.get(".travel-itinerary strong").text()).toBe("Route");
  expect(wrapper.find(".travel-itinerary script").exists()).toBe(false);
  expect(wrapper.text()).toContain("Your journey is ready to review");
  wrapper.unmount();
});

it("treats a non-sunny Magentic outcome as postponement, not a sunny itinerary", async () => {
  const wrapper = await planner("magentic");
  realtime.observer.next({ type: "result", agent: "TravelManager", data: { text: "Postpone: sunshine is unverified.", outcome: "postpone" } });
  realtime.observer.next({ type: "completed", agent: "system", data: null });
  await flushPromises();
  expect(wrapper.get(".travel-itinerary .travel-warning").text()).toContain("could not be verified");
  expect(wrapper.text()).toContain("Weather review complete: postpone travel");
  wrapper.unmount();
});

it("stops work, ignores late events and never replays automatically after reconnection", async () => {
  const wrapper = await planner();
  const old = realtime.observer;
  await wrapper.get(".travel-stop").trigger("click");
  old.next({ type: "weather", agent: "WeatherChecker", data: { location: "Stale city", condition: "sunny", sources: [] } });
  await flushPromises();
  expect(wrapper.text()).not.toContain("Stale city");
  expect(realtime.dispose).toHaveBeenCalledTimes(1);
  await wrapper.get("form").trigger("submit");
  await flushPromises();
  realtime.reconnecting();
  realtime.reconnected();
  await flushPromises();
  expect(realtime.stream).toHaveBeenCalledTimes(2);
  expect(wrapper.text()).toContain("Runs are not replayed");
  wrapper.unmount();
});

it("does not claim success after provider errors or premature stream completion", async () => {
  const wrapper = await planner();
  realtime.observer.next({ type: "error", agent: "system", data: "WebIQ weather search failed (HTTP 401)." });
  realtime.observer.complete();
  await flushPromises();
  expect(wrapper.get('[role="alert"]').text()).toContain("WebIQ");
  expect(wrapper.text()).not.toContain("Your journey is ready");
  await wrapper.get("form").trigger("submit");
  await flushPromises();
  realtime.observer.complete();
  await flushPromises();
  expect(wrapper.get('[role="alert"]').text()).toContain("before planning completed");
  wrapper.unmount();
});

it("requires distinct countries and a date within the forecast horizon", async () => {
  const wrapper = mount(TravelApp, { props: { scenario: "handoff" } });
  await flushPromises();
  expect(wrapper.get('button[type="submit"]').element.disabled).toBe(true);
  await wrapper.get("#travel-start").setValue("NL");
  await wrapper.get("#travel-destination").setValue("NL");
  expect(wrapper.get('button[type="submit"]').element.disabled).toBe(true);
  await wrapper.get("#travel-destination").setValue("DE");
  await wrapper.get("#travel-date").setValue("2026-12-01");
  expect(wrapper.get('button[type="submit"]').element.disabled).toBe(true);
  await wrapper.get("#travel-date").setValue("2026-11-19");
  expect(wrapper.get('button[type="submit"]').element.disabled).toBe(false);
  wrapper.unmount();
});
