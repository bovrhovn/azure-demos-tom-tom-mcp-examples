import { afterEach, describe, expect, it, vi } from "vitest";
import { effectScope, nextTick, ref, shallowRef } from "vue";
import { useChatRealtime } from "../composables/useChatRealtime.js";
import { useLeaseClock } from "../composables/useLeaseClock.js";
import { usePreferences } from "../composables/usePreferences.js";

const hubState = vi.hoisted(() => ({
  instances: [],
  nextStartGate: null
}));

vi.mock("@microsoft/signalr", () => ({
  LogLevel: { Warning: 3 },
  HubConnectionBuilder: class {
    withUrl(url) { this.url = url; return this; }
    withAutomaticReconnect() { return this; }
    configureLogging() { return this; }
    build() {
      const instance = {
        url: this.url,
        startCalls: 0,
        stopCalls: 0,
        on() {},
        onreconnecting() {},
        onclose() {},
        onreconnected() {},
        start: vi.fn(async () => {
          instance.startCalls++;
          if (hubState.nextStartGate) {
            await hubState.nextStartGate;
          }
        }),
        stop: vi.fn(async () => {
          instance.stopCalls++;
        })
      };
      hubState.instances.push(instance);
      return instance;
    }
  }
}));

afterEach(() => {
  hubState.instances = [];
  hubState.nextStartGate = null;
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("useLeaseClock", () => {
  it("flips busy to false via targeted setTimeout when busyUntil expires with no recurring interval", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-17T10:00:00.000Z"));

    const selected = shallowRef({
      id: "session-1",
      busyUntil: new Date(Date.now() + 5000).toISOString(),
      messages: [
        { id: "m1", role: "user", status: "completed", content: "Question" },
        { id: "m1-agent", role: "assistant", status: "pending", content: "" }
      ]
    });
    const sending = ref(false);
    const maxTurns = ref(40);

    const scope = effectScope();
    const lease = scope.run(() => useLeaseClock({ selected, sending, maxTurns }));

    expect(lease.leaseActive.value).toBe(true);
    expect(lease.busy.value).toBe(true);
    expect(vi.getTimerCount()).toBe(1);

    vi.advanceTimersByTime(4950);
    await nextTick();
    expect(lease.busy.value).toBe(true);

    vi.advanceTimersByTime(100);
    await nextTick();
    expect(lease.leaseActive.value).toBe(false);
    expect(lease.busy.value).toBe(false);
    expect(vi.getTimerCount()).toBe(0);

    scope.stop();
  });

  it("clears its scheduled timeout when the effect scope disposes before expiry", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-17T10:00:00.000Z"));

    const selected = shallowRef({
      id: "session-1",
      busyUntil: new Date(Date.now() + 10_000).toISOString(),
      messages: [{ id: "m1-agent", role: "assistant", status: "pending", content: "" }]
    });

    const scope = effectScope();
    scope.run(() => useLeaseClock({ selected, sending: ref(false), maxTurns: ref(40) }));
    expect(vi.getTimerCount()).toBe(1);

    scope.stop();
    expect(vi.getTimerCount()).toBe(0);
  });
});

describe("useChatRealtime", () => {
  it("guards concurrent reconnect() calls so the active connection stops and restarts only once", async () => {
    const onReconnected = vi.fn();
    const scope = effectScope();
    const realtime = scope.run(() => useChatRealtime({
      getHubUrl: () => "/hubs/chat",
      onReconnected
    }));

    await realtime.start();
    expect(hubState.instances).toHaveLength(1);
    expect(realtime.connected.value).toBe(true);
    const initialHub = hubState.instances[0];

    let releaseStart;
    hubState.nextStartGate = new Promise((resolve) => {
      releaseStart = resolve;
    });

    const firstReconnect = realtime.reconnect();
    const secondReconnect = realtime.reconnect();
    expect(secondReconnect).toBe(firstReconnect);

    releaseStart();
    await Promise.all([firstReconnect, secondReconnect]);

    expect(initialHub.stopCalls).toBe(1);
    expect(hubState.instances).toHaveLength(2);
    expect(hubState.instances[1].startCalls).toBe(1);
    expect(onReconnected).toHaveBeenCalledTimes(1);
    expect(realtime.connected.value).toBe(true);

    scope.stop();
  });
});

describe("usePreferences", () => {
  it("falls back to defaults and does not throw when localStorage throws SecurityError", () => {
    const securityError = () => {
      throw new DOMException("Storage is disabled in this context.", "SecurityError");
    };
    vi.stubGlobal("localStorage", {
      getItem: vi.fn(securityError),
      setItem: vi.fn(securityError)
    });

    let prefs;
    expect(() => {
      prefs = usePreferences();
    }).not.toThrow();

    expect(prefs.compact.value).toBe(false);
    expect(prefs.showUsage.value).toBe(true);

    prefs.compact.value = true;
    prefs.showUsage.value = false;
    expect(() => prefs.savePreferences()).not.toThrow();
  });
});
