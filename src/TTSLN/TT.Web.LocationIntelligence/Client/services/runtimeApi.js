export function readEmbeddedRuntime(doc = globalThis.document) {
  const element = doc?.getElementById?.("tt-runtime");
  const raw = element?.textContent?.trim();
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw);
    if (!parsed || typeof parsed !== "object" || Object.keys(parsed).length === 0) {
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}

export function createRuntimeApi(http, getRuntimeUrl = () => "/api/runtime") {
  return {
    getRuntime({ signal } = {}) {
      return http(getRuntimeUrl(), { signal });
    }
  };
}
