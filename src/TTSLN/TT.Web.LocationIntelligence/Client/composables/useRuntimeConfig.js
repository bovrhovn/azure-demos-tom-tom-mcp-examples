import { computed, shallowRef } from "vue";
import { readEmbeddedRuntime } from "../services/runtimeApi.js";

const DEFAULT_ENDPOINTS = Object.freeze({
  runtime: "/api/runtime",
  sessions: "/api/sessions",
  chatHub: "/hubs/chat"
});

export function useRuntimeConfig() {
  const runtime = shallowRef(null);
  let refreshPromise = null;

  async function refreshRuntime(runtimeApi) {
    if (!refreshPromise) {
      refreshPromise = runtimeApi.getRuntime()
        .then((data) => {
          runtime.value = data;
          return data;
        })
        .finally(() => {
          refreshPromise = null;
        });
    }
    return refreshPromise;
  }

  async function bootstrap(runtimeApi, doc = globalThis.document) {
    const embedded = readEmbeddedRuntime(doc);
    if (embedded) {
      runtime.value = embedded;
      return embedded;
    }
    return refreshRuntime(runtimeApi);
  }

  const agentConfigured = computed(() => Boolean(runtime.value?.agentConfigured));
  const maxMessageLength = computed(() => runtime.value?.maxMessageLength || 8000);
  const maxTitleLength = computed(() => runtime.value?.maxTitleLength || 120);
  const maxTurns = computed(() => runtime.value?.maxTurns || 40);
  const antiforgeryHeaderName = computed(() => runtime.value?.antiforgeryHeaderName || "X-CSRF-TOKEN");
  const csrfToken = computed(() => runtime.value?.csrfToken ?? "");
  const endpoints = computed(() => ({
    runtime: runtime.value?.endpoints?.runtime || DEFAULT_ENDPOINTS.runtime,
    sessions: runtime.value?.endpoints?.sessions || DEFAULT_ENDPOINTS.sessions,
    chatHub: runtime.value?.endpoints?.chatHub || DEFAULT_ENDPOINTS.chatHub
  }));

  return {
    runtime,
    bootstrap,
    refreshRuntime,
    agentConfigured,
    maxMessageLength,
    maxTitleLength,
    maxTurns,
    antiforgeryHeaderName,
    csrfToken,
    endpoints
  };
}
