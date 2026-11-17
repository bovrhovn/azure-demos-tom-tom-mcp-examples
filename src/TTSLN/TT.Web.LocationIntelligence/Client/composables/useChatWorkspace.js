import { onScopeDispose, ref, shallowRef } from "vue";
import { useLeaseClock } from "./useLeaseClock.js";

const MAX_DELETED_SESSION_IDS = 500;

function isAbortError(error) {
  return error?.name === "AbortError";
}

export function useChatWorkspace({
  runtimeConfig,
  runtimeApi,
  sessionsApi,
  onScrollRequest
}) {
  const sessions = shallowRef([]);
  const selected = shallowRef(null);
  const continuationToken = ref(null);
  const draft = ref("");
  const error = ref("");
  const loading = ref(true);
  const sending = ref(false);
  const databaseConnected = ref(false);
  const { clock, leaseActive, busy, turnLimitReached } = useLeaseClock({
    selected,
    sending,
    maxTurns: runtimeConfig.maxTurns
  });

  const sessionEtags = new Map();
  const refreshVersions = new Map();
  const refreshAborts = new Map();
  const deletedSessions = new Set();
  let selectionVersion = 0;
  let selectionAbort = null;

  function rememberDeleted(id) {
    deletedSessions.delete(id);
    deletedSessions.add(id);
    if (deletedSessions.size > MAX_DELETED_SESSION_IDS) {
      deletedSessions.delete(deletedSessions.values().next().value);
    }
  }

  function upsertSummary(summary) {
    sessions.value = [
      summary,
      ...sessions.value.filter((item) => item.id !== summary.id)
    ];
  }

  function applyUpdatedSession(session, etag = null) {
    if (!session || deletedSessions.has(session.id)) return;
    if (etag) sessionEtags.set(session.id, etag);

    refreshAborts.get(session.id)?.abort();
    refreshAborts.delete(session.id);
    refreshVersions.set(session.id, (refreshVersions.get(session.id) ?? 0) + 1);

    upsertSummary({ id: session.id, title: session.title, updatedAt: session.updatedAt });
    if (selected.value?.id === session.id) {
      ++selectionVersion;
      selectionAbort?.abort();
      selectionAbort = null;
      selected.value = session;
    }
  }

  function removeSession(id) {
    rememberDeleted(id);
    refreshAborts.get(id)?.abort();
    refreshAborts.delete(id);
    refreshVersions.delete(id);
    sessionEtags.delete(id);
    sessions.value = sessions.value.filter((session) => session.id !== id);

    if (selected.value?.id === id) {
      ++selectionVersion;
      selectionAbort?.abort();
      selectionAbort = null;
      selected.value = null;
      draft.value = "";
    }
  }

  async function loadSessions(append = false) {
    const page = await sessionsApi.list({
      continuationToken: append && continuationToken.value ? continuationToken.value : undefined
    });
    const items = (page.items ?? []).filter((item) => !deletedSessions.has(item.id));
    if (append) {
      const existingIds = new Set(sessions.value.map((item) => item.id));
      sessions.value = [...sessions.value, ...items.filter((item) => !existingIds.has(item.id))];
    } else {
      sessions.value = items;
    }
    continuationToken.value = page.continuationToken ?? null;
    databaseConnected.value = true;
  }

  async function selectSession(id) {
    selectionAbort?.abort();
    const controller = new AbortController();
    selectionAbort = controller;
    const version = ++selectionVersion;
    error.value = "";

    try {
      const ifNoneMatch = selected.value?.id === id ? sessionEtags.get(id) : undefined;
      const result = await sessionsApi.get(id, {
        signal: controller.signal,
        ifNoneMatch
      });
      if (version !== selectionVersion || deletedSessions.has(id)) return;
      if (result.etag) sessionEtags.set(id, result.etag);
      if (!result.notModified) {
        selected.value = result.session;
      }
      await onScrollRequest?.({ force: true });
    } catch (reason) {
      if (isAbortError(reason)) return;
      if (reason.status === 404) {
        removeSession(id);
        return;
      }
      if (version === selectionVersion && !deletedSessions.has(id)) {
        error.value = reason.message;
      }
    } finally {
      if (selectionAbort === controller) {
        selectionAbort = null;
      }
    }
  }

  async function refreshSession(id, { knownUpdatedAt } = {}) {
    if (!id || deletedSessions.has(id)) return;

    if (knownUpdatedAt && selected.value?.id === id &&
        !selected.value.messages?.some((message) => message.status === "pending")) {
      const localMs = Date.parse(selected.value.updatedAt);
      const incomingMs = Date.parse(knownUpdatedAt);
      if (Number.isFinite(localMs) && Number.isFinite(incomingMs) && localMs >= incomingMs) {
        const top = sessions.value[0];
        if (top?.id !== id || top.title !== selected.value.title || top.updatedAt !== selected.value.updatedAt) {
          upsertSummary({
            id: selected.value.id,
            title: selected.value.title,
            updatedAt: selected.value.updatedAt
          });
        }
        return;
      }
    }

    refreshAborts.get(id)?.abort();
    const controller = new AbortController();
    refreshAborts.set(id, controller);
    const version = (refreshVersions.get(id) ?? 0) + 1;
    refreshVersions.set(id, version);

    let result;
    try {
      const hasSummary = sessions.value.some((item) => item.id === id);
      const canUseEtag = hasSummary && (selected.value?.id !== id || Boolean(selected.value));
      result = await sessionsApi.get(id, {
        signal: controller.signal,
        ifNoneMatch: canUseEtag ? sessionEtags.get(id) : undefined
      });
    } catch (reason) {
      if (isAbortError(reason)) return;
      if (reason.status === 404) {
        removeSession(id);
        return;
      }
      throw reason;
    } finally {
      if (refreshAborts.get(id) === controller) {
        refreshAborts.delete(id);
      }
    }

    if (refreshVersions.get(id) !== version || deletedSessions.has(id)) return;
    if (result.etag) sessionEtags.set(id, result.etag);
    if (result.notModified) return;

    const session = result.session;
    upsertSummary({ id: session.id, title: session.title, updatedAt: session.updatedAt });
    if (selected.value?.id === id) {
      selected.value = session;
      await onScrollRequest?.({ force: false });
    }
  }

  async function createSession(title = "") {
    error.value = "";
    try {
      const { session, etag } = await sessionsApi.create({ title });
      deletedSessions.delete(session.id);
      if (etag) sessionEtags.set(session.id, etag);
      ++selectionVersion;
      selectionAbort?.abort();
      selectionAbort = null;
      refreshVersions.set(session.id, (refreshVersions.get(session.id) ?? 0) + 1);
      selected.value = session;
      upsertSummary({ id: session.id, title: session.title, updatedAt: session.updatedAt });
      return session;
    } catch (reason) {
      error.value = reason.message;
      return null;
    }
  }

  async function renameSession(id, title) {
    const { session, etag } = await sessionsApi.rename(id, { title });
    applyUpdatedSession(session, etag);
    return session;
  }

  async function deleteSession(id) {
    await sessionsApi.remove(id);
    removeSession(id);
  }

  async function send() {
    const content = draft.value;
    if (!content.trim() || busy.value || turnLimitReached.value || !selected.value) return;

    const id = selected.value.id;
    sending.value = true;
    error.value = "";
    draft.value = "";
    try {
      const { session, etag } = await sessionsApi.sendMessage(id, {
        requestId: crypto.randomUUID(),
        content
      });
      applyUpdatedSession(session, etag);
    } catch (reason) {
      error.value = reason.message;
      // Reload persisted state instead of resending a potentially committed turn.
      try {
        await refreshSession(id);
      } catch (refreshError) {
        error.value += ` ${refreshError.message}`;
        databaseConnected.value = false;
      }
    } finally {
      sending.value = false;
      await onScrollRequest?.({ force: true });
    }
  }

  async function reconcileRealtime() {
    await loadSessions(false);
    if (selected.value) {
      await refreshSession(selected.value.id);
    }
  }

  async function initialize(realtime) {
    loading.value = true;
    error.value = "";
    try {
      await runtimeConfig.bootstrap(runtimeApi);
      const bootSessions = async () => {
        await loadSessions(false);
        if (sessions.value.length > 0) {
          await selectSession(sessions.value[0].id);
        }
      };
      const [sessionsResult, realtimeResult] = await Promise.allSettled([
        bootSessions(),
        realtime.start()
      ]);
      if (sessionsResult.status === "rejected") {
        error.value = sessionsResult.reason?.message ?? "Could not load conversations.";
      } else if (realtimeResult.status === "rejected" && !error.value) {
        error.value = realtimeResult.reason?.message ?? "Realtime connection unavailable.";
      }
    } catch (reason) {
      error.value = reason.message;
    } finally {
      loading.value = false;
    }
  }

  onScopeDispose(() => {
    selectionAbort?.abort();
    for (const controller of refreshAborts.values()) {
      controller.abort();
    }
    refreshAborts.clear();
  });

  return {
    sessions,
    selected,
    continuationToken,
    draft,
    error,
    loading,
    sending,
    databaseConnected,
    clock,
    leaseActive,
    busy,
    turnLimitReached,
    loadSessions,
    selectSession,
    refreshSession,
    applyUpdatedSession,
    removeSession,
    createSession,
    renameSession,
    deleteSession,
    send,
    reconcileRealtime,
    initialize
  };
}
