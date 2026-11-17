import { afterEach, expect, it, vi } from "vitest";
import { flushPromises } from "@vue/test-utils";
import { reactive } from "vue";
import { connectMcpApp, sandboxDocument } from "../services/mcpAppHost.js";

afterEach(() => vi.useRealTimers());

function fixture({ reactiveApp = false } = {}) {
  const postMessage = vi.fn((message) => structuredClone(message));
  const iframe = { contentWindow: { postMessage }, style: {} };
  const callTool = vi.fn(async () => ({ content: [{ type: "text", text: "map-config" }] }));
  const onError = vi.fn();
  const onReady = vi.fn();
  const app = {
    html: "<html><head><script>window.app = true</script></head><body>Map</body></html>",
    arguments: { route: "Amsterdam-Berlin" },
    result: { content: [{ type: "text", text: "route" }], _meta: { appData: "geometry" } },
    meta: { ui: { csp: { resourceDomains: ["https://cdn.jsdelivr.net", "blob:"], connectDomains: ["https://api.tomtom.com", "blob:"] } } }
  };
  const dispose = connectMcpApp({ iframe, app: reactiveApp ? reactive(app) : app,
    callTool, onError, onReady, timeoutMilliseconds: 30000 });
  const send = async (data, source = iframe.contentWindow) => {
    window.dispatchEvent(new MessageEvent("message", { source, data: { jsonrpc: "2.0", ...data } }));
    await flushPromises();
  };
  return { iframe, app, postMessage, callTool, onError, onReady, send, dispose };
}

it("isolates messages by frame, initializes the app and forwards full tool metadata only after readiness", async () => {
  const f = fixture();
  await f.send({ id: 1, method: "ui/initialize" }, window);
  expect(f.postMessage).not.toHaveBeenCalled();
  await f.send({ id: 1, method: "ui/initialize" });
  expect(f.postMessage.mock.calls[0][0].result).toMatchObject({
    protocolVersion: "2026-01-26", hostCapabilities: { serverTools: {} }
  });
  expect(f.postMessage).toHaveBeenCalledTimes(1);
  await f.send({ method: "ui/notifications/initialized" });
  expect(f.postMessage.mock.calls[1][0]).toMatchObject({ method: "ui/notifications/tool-input", params: { arguments: f.app.arguments } });
  expect(f.postMessage.mock.calls[2][0]).toMatchObject({ method: "ui/notifications/tool-result", params: f.app.result });
  await f.send({ method: "ui/notifications/initialized" });
  expect(f.onReady).toHaveBeenCalledTimes(1);
  await f.send({ method: "ui/notifications/size-changed", params: { height: 90000 } });
  expect(f.iframe.style.height).toBe("900px");
  f.dispose();
});

it("proxies only allowlisted app tools after initialization and rejects all other operations", async () => {
  const f = fixture();
  await f.send({ id: 1, method: "tools/call", params: { name: "tomtom-get-api-key" } });
  expect(f.callTool).not.toHaveBeenCalled();
  await f.send({ method: "ui/notifications/initialized" });
  await f.send({ id: 2, method: "tools/call", params: { name: "tomtom-get-app-config", arguments: {} } });
  expect(f.callTool).toHaveBeenCalledWith("tomtom-get-app-config", {}, expect.any(AbortSignal));
  expect(f.postMessage.mock.calls.at(-1)[0]).toMatchObject({ id: 2, result: { content: [{ text: "map-config" }] } });
  await f.send({ id: 3, method: "tools/call", params: { name: "tomtom-routing", arguments: {} } });
  expect(f.callTool).toHaveBeenCalledTimes(1);
  expect(f.postMessage.mock.calls.at(-1)[0]).toMatchObject({ id: 3, error: { code: -32601 } });
  f.dispose();
});

it("sends Vue-reactive arguments and complete tool results as cloneable JSON-RPC messages", async () => {
  const f = fixture({ reactiveApp: true });
  try {
    await f.send({ id: 1, method: "ui/initialize" });
    await f.send({ method: "ui/notifications/initialized" });
    expect(f.onReady).toHaveBeenCalledOnce();
    expect(f.onError).not.toHaveBeenCalled();
    const messages = f.postMessage.mock.calls.map(([message]) => structuredClone(message));
    expect(messages[1]).toEqual({
      jsonrpc: "2.0", method: "ui/notifications/tool-input", params: { arguments: f.app.arguments }
    });
    expect(messages[2]).toEqual({
      jsonrpc: "2.0", method: "ui/notifications/tool-result", params: f.app.result
    });
  } finally {
    f.dispose();
  }
});

it("constructs restrictive CSP before any untrusted script and preserves declared TomTom dependencies", () => {
  const html = sandboxDocument("<script>parent.document.body.innerHTML='bad'</script>", {
    ui: { csp: {
      resourceDomains: ["https://cdn.jsdelivr.net", "blob:", "https://evil.test; connect-src *"],
      connectDomains: ["https://*.api.tomtom.com", "blob:"]
    } }
  });
  const doc = new DOMParser().parseFromString(html, "text/html");
  const policy = doc.head.firstElementChild;
  expect(policy.httpEquiv).toBe("Content-Security-Policy");
  expect(policy.content).toContain("script-src 'unsafe-inline' https://cdn.jsdelivr.net blob:");
  expect(policy.content).toContain("connect-src https://*.api.tomtom.com blob:");
  expect(policy.content).toContain("worker-src blob:");
  expect(policy.content).toContain("frame-src 'none'");
  expect(policy.content).not.toContain("evil.test");
  expect(policy.content).not.toContain("'unsafe-eval'");
  expect(sandboxDocument("<p>no policy</p>")).toContain("connect-src 'none'");
});

it("reports initialization and tool failures explicitly and cleans up pending requests", async () => {
  vi.useFakeTimers();
  const f = fixture();
  vi.advanceTimersByTime(30000);
  expect(f.onError).toHaveBeenCalledWith(expect.stringContaining("did not initialize"));
  vi.useRealTimers();
  await f.send({ method: "ui/notifications/initialized" });
  f.callTool.mockRejectedValue(new Error("secret upstream url"));
  await f.send({ id: 7, method: "tools/call", params: { name: "tomtom-get-api-key" } });
  expect(f.onError).toHaveBeenCalledWith(expect.stringContaining("could not reach TomTom"));
  expect(JSON.stringify(f.postMessage.mock.calls)).not.toContain("secret upstream url");
  const signal = f.callTool.mock.calls[0][2];
  f.dispose();
  expect(signal.aborted).toBe(true);
  const count = f.postMessage.mock.calls.length;
  await f.send({ id: 8, method: "ui/initialize" });
  expect(f.postMessage).toHaveBeenCalledTimes(count);
});
