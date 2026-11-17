const APP_TOOLS = new Set([
  "tomtom-get-api-key", "tomtom-get-app-config", "tomtom-get-app-data", "tomtom-report-app-status"
]);

function sources(values = [], schemes = []) {
  return values.filter((value) => typeof value === "string" &&
    (schemes.includes(value) || /^https:\/\/(\*\.)?[a-z0-9.-]+(?::\d+)?$/i.test(value))).join(" ");
}

export function sandboxDocument(html, meta) {
  const csp = meta?.ui?.csp ?? {};
  const resources = sources(csp.resourceDomains, ["blob:", "data:"]);
  const connections = sources(csp.connectDomains, ["blob:"]);
  const policy = [
    "default-src 'none'", `script-src 'unsafe-inline' ${resources}`,
    `style-src 'unsafe-inline' ${resources}`, `img-src data: blob: ${resources}`,
    `font-src data: ${resources}`, `media-src data: blob: ${resources}`,
    `connect-src ${connections || "'none'"}`, `worker-src ${resources.includes("blob:") ? "blob:" : "'none'"}`,
    "frame-src 'none'", "object-src 'none'", "base-uri 'none'", "form-action 'none'"
  ].join("; ");
  const doc = new DOMParser().parseFromString(html, "text/html");
  const tag = doc.createElement("meta");
  tag.httpEquiv = "Content-Security-Policy";
  tag.content = policy;
  doc.head.prepend(tag);
  return `<!doctype html>\n${doc.documentElement.outerHTML}`;
}

export function connectMcpApp({ iframe, app, callTool, onReady, onError, timeoutMilliseconds,
  hostWindow = window }) {
  let disposed = false;
  let initialized = false;
  const controller = new AbortController();
  const post = (message) => {
    if (!disposed) iframe.contentWindow?.postMessage({ jsonrpc: "2.0", ...message }, "*");
  };
  const notify = (method, params) => post({ method, params });
  const timer = setTimeout(() => {
    onError("The map app did not initialize. Check access to cdn.jsdelivr.net and api.tomtom.com.");
  }, timeoutMilliseconds);

  async function receive(event) {
    if (disposed || event.source !== iframe.contentWindow || event.data?.jsonrpc !== "2.0") return;
    const { id, method, params } = event.data;
    if (method === "ui/notifications/initialized") {
      if (initialized) return;
      initialized = true;
      clearTimeout(timer);
      notify("ui/notifications/tool-input", { arguments: app.arguments });
      notify("ui/notifications/tool-result", app.result);
      onReady();
      return;
    }
    if (method === "ui/notifications/size-changed") {
      if (Number.isFinite(params?.height)) iframe.style.height = `${Math.max(360, Math.min(900, params.height))}px`;
      return;
    }
    if (id === undefined) return;
    try {
      let result;
      if (method === "ui/initialize") {
        result = {
          protocolVersion: "2026-01-26",
          hostInfo: { name: "TomTom Location Intelligence", version: "1.0.0" },
          hostCapabilities: { serverTools: {} },
          hostContext: {
            theme: "light", platform: "web", displayMode: "inline", availableDisplayModes: ["inline"],
            containerDimensions: { maxHeight: 900 }
          }
        };
      } else if (method === "tools/call" && initialized && APP_TOOLS.has(params?.name)) {
        result = await callTool(params.name, params.arguments ?? {}, controller.signal);
        if (params.name === "tomtom-report-app-status" && params.arguments?.status === "failed")
          onError("TomTom could not draw this map. Check the API key permissions and browser network access.");
      } else if (method === "ping") {
        result = {};
      } else {
        post({ id, error: { code: -32601, message: "This operation is not supported by this map host." } });
        return;
      }
      post({ id, result });
    } catch {
      onError("The map app could not reach TomTom. Check the server configuration and try again.");
      post({ id, error: { code: -32603, message: "The map app request failed." } });
    }
  }
  hostWindow.addEventListener("message", receive);
  iframe.srcdoc = sandboxDocument(app.html, app.meta);
  return () => {
    disposed = true;
    clearTimeout(timer);
    controller.abort();
    hostWindow.removeEventListener("message", receive);
  };
}
