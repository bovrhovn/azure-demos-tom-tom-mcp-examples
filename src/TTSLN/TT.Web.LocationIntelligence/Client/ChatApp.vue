<script setup>
import { computed, nextTick, onMounted, onUnmounted, ref } from "vue";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { createApi } from "./api.js";

const runtime = ref(null);
const sessions = ref([]);
const selected = ref(null);
const continuation = ref(null);
const draft = ref("");
const error = ref("");
const sending = ref(false);
const loading = ref(true);
const connected = ref(false);
const databaseConnected = ref(false);
const compact = ref(localStorage.getItem("tt.compact") === "true");
const showUsage = ref(localStorage.getItem("tt.usage") !== "false");
const sessionTitle = ref("");
const optionsDialog = ref(null);
const newDialog = ref(null);
const messageList = ref(null);
const composer = ref(null);
const clock = ref(Date.now());
const api = createApi(() => runtime.value?.csrfToken ?? "");
let connection;
let selectionVersion = 0;
let clockTimer;
const refreshVersions = new Map();
const busy = computed(() => sending.value || !!selected.value?.messages.some((message) =>
  message.status === "pending") && new Date(selected.value.busyUntil).getTime() > clock.value);
const prompts = [
  { icon: "fa-location-dot", title: "Find a place", text: "Where is the Eiffel Tower? Give me its coordinates." },
  { icon: "fa-route", title: "Plan a route", text: "Help me plan a driving route from Amsterdam to Rotterdam." },
  { icon: "fa-charging-station", title: "Explore nearby", text: "Find EV charging stations near Amsterdam Centraal." }
];
const formatDate = (date) => new Date(date).toLocaleDateString(undefined, { month: "short", day: "numeric" });
const formatTime = (date) => new Date(date).toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit" });
const displayTokens = (tokens) => tokens == null ? "not reported" : tokens.toLocaleString();

async function scrollToLatest() {
  await nextTick();
  messageList.value?.scrollTo({ top: messageList.value.scrollHeight, behavior: "smooth" });
}
async function loadSessions(append = false) {
  const page = await api(`/api/sessions/${append && continuation.value
    ? `?continuationToken=${encodeURIComponent(continuation.value)}` : ""}`);
  sessions.value = append ? [...sessions.value, ...page.items] : page.items;
  continuation.value = page.continuationToken;
  databaseConnected.value = true;
}
async function selectSession(id) {
  const version = ++selectionVersion;
  error.value = "";
  try {
    const session = await api(`/api/sessions/${id}`);
    if (version !== selectionVersion) return;
    selected.value = session;
    await scrollToLatest();
  } catch (reason) { error.value = reason.message; }
}
async function refreshSession(id) {
  const version = (refreshVersions.get(id) ?? 0) + 1;
  refreshVersions.set(id, version);
  const session = await api(`/api/sessions/${id}`);
  if (refreshVersions.get(id) !== version) return;
  const index = sessions.value.findIndex((item) => item.id === id);
  const summary = { id: session.id, title: session.title, updatedAt: session.updatedAt };
  if (index >= 0) sessions.value.splice(index, 1);
  sessions.value.unshift(summary);
  if (selected.value?.id === id) {
    selected.value = session;
    await scrollToLatest();
  }
}
async function createSession() {
  error.value = "";
  try {
    const session = await api("/api/sessions/", { method: "POST", body: { title: sessionTitle.value } });
    ++selectionVersion;
    selected.value = session;
    sessions.value.unshift({ id: session.id, title: session.title, updatedAt: session.updatedAt });
    sessionTitle.value = "";
    newDialog.value.close();
    await nextTick();
    composer.value?.focus();
    return session;
  } catch (reason) { error.value = reason.message; return null; }
}
async function send() {
  if (!draft.value.trim() || busy.value || !selected.value) return;
  const id = selected.value.id;
  const content = draft.value;
  sending.value = true;
  error.value = "";
  draft.value = "";
  try {
    const session = await api(`/api/sessions/${id}/messages`, {
      method: "POST", body: { requestId: crypto.randomUUID(), content }
    });
    if (selected.value?.id === id) selected.value = session;
    await refreshSession(id);
  } catch (reason) {
    error.value = reason.message;
    // Reload persisted state instead of resending a potentially committed turn.
    try { await refreshSession(id); }
    catch (refreshError) { error.value += ` ${refreshError.message}`; databaseConnected.value = false; }
  } finally { sending.value = false; await scrollToLatest(); }
}
async function usePrompt(text) {
  if (!selected.value && !await createSession()) return;
  draft.value = text;
  composer.value?.focus();
}
function savePreferences() {
  localStorage.setItem("tt.compact", String(compact.value));
  localStorage.setItem("tt.usage", String(showUsage.value));
  optionsDialog.value.close();
}
async function initialize() {
  loading.value = true;
  error.value = "";
  try {
    runtime.value = await api("/api/runtime");
    await loadSessions();
    if (sessions.value.length) await selectSession(sessions.value[0].id);
    connection = new HubConnectionBuilder().withUrl("/hubs/chat")
      .withAutomaticReconnect().configureLogging(LogLevel.Warning).build();
    connection.on("ConversationChanged", (id) => {
      refreshSession(id).catch((reason) => { error.value = reason.message; });
    });
    connection.onreconnecting(() => { connected.value = false; });
    connection.onclose(() => { connected.value = false; });
    connection.onreconnected(async () => {
      connected.value = true;
      try {
        await loadSessions();
        if (selected.value) await refreshSession(selected.value.id);
      } catch (reason) { error.value = reason.message; }
    });
    await connection.start();
    connected.value = true;
    if (selected.value) await refreshSession(selected.value.id);
  } catch (reason) { error.value = reason.message; }
  finally { loading.value = false; }
}
async function reconnect() {
  try {
    if (connection) await connection.stop();
    await initialize();
  } catch (reason) { error.value = reason.message; }
}
onMounted(() => {
  clockTimer = setInterval(() => { clock.value = Date.now(); }, 10000);
  initialize();
});
onUnmounted(() => { clearInterval(clockTimer); connection?.stop(); });
</script>

<template>
  <div class="workspace" :class="{ compact }">
    <header class="topbar">
      <a class="brand" href="/" aria-label="TomTom Location Intelligence home">
        <span class="brand-mark" aria-hidden="true"><i class="fa-solid fa-location-crosshairs"></i></span>
        <span class="wordmark">tomtom<span class="brand-dot">.</span></span>
      </a>
      <span class="header-divider"></span>
      <span class="product-name">Location Intelligence <span class="preview-tag">PLAYGROUND</span></span>
      <a class="docs-link" href="https://docs.tomtom.com/" target="_blank" rel="noopener noreferrer">
        Documentation <i class="fa-solid fa-arrow-up-right-from-square" aria-hidden="true"></i>
      </a>
      <button class="icon-button" aria-label="Open options" @click="optionsDialog.showModal()">
        <i class="fa-solid fa-sliders" aria-hidden="true"></i>
      </button>
    </header>

    <aside class="sidebar" aria-label="Chat sessions">
      <div class="sidebar-heading"><span>YOUR WORKSPACE</span><i class="fa-solid fa-compass" aria-hidden="true"></i></div>
      <button class="new-session" :disabled="loading || !runtime" @click="newDialog.showModal()">
        <i class="fa-solid fa-plus" aria-hidden="true"></i> New conversation
      </button>
      <div class="section-label">CONVERSATIONS <span>{{ sessions.length }}</span></div>
      <nav class="sessions">
        <button v-for="session in sessions" :key="session.id" class="session"
                :class="{ active: selected?.id === session.id }"
                :aria-current="selected?.id === session.id ? 'page' : undefined"
                @click="selectSession(session.id)">
          <i class="fa-regular fa-comment" aria-hidden="true"></i>
          <span><strong>{{ session.title }}</strong><small>{{ formatDate(session.updatedAt) }}</small></span>
          <i v-if="selected?.id === session.id" class="fa-solid fa-arrow-right active-arrow" aria-hidden="true"></i>
        </button>
        <p v-if="!sessions.length && !loading" class="sidebar-empty">A new perspective starts with a question.</p>
        <button v-if="continuation" class="text-button" @click="loadSessions(true).catch(reason => error = reason.message)">Load more</button>
      </nav>
      <div class="sidebar-note">
        <span class="note-icon"><i class="fa-solid fa-layer-group" aria-hidden="true"></i></span>
        <strong>Real-world context.<br>Smarter conversations.</strong>
        <p>Powered by TomTom Maps MCP<br>and Microsoft Foundry.</p>
        <span class="note-line"></span>
      </div>
    </aside>

    <main class="chat-panel">
      <div class="conversation-header">
        <div><span class="eyebrow">EXPLORE WITH CONFIDENCE</span>
          <h1>{{ selected?.title || "Your world. Connected." }}</h1></div>
        <button class="connection-badge" :class="{ offline: !connected }" @click="reconnect"
                :aria-label="connected ? 'Realtime connected. Reconnect.' : 'Realtime disconnected. Retry connection.'">
          <span class="status-dot"></span>{{ connected ? "Live" : "Reconnect" }}
        </button>
      </div>

      <div v-if="error" class="error-banner" role="alert">
        <i class="fa-solid fa-circle-exclamation" aria-hidden="true"></i><span>{{ error }}</span>
        <button class="icon-button" aria-label="Dismiss error" @click="error = ''"><i class="fa-solid fa-xmark" aria-hidden="true"></i></button>
      </div>
      <div v-if="runtime && !runtime.agentConfigured" class="setup-banner">
        <i class="fa-solid fa-wrench" aria-hidden="true"></i>
        Configure Foundry and the TomTom key on the server to enable agent replies. Conversations are saved to Cosmos DB.
      </div>

      <div ref="messageList" class="message-list" role="log" aria-label="Conversation messages" aria-live="polite">
        <div v-if="loading" class="loading-state"><i class="fa-solid fa-circle-notch fa-spin" aria-hidden="true"></i> Connecting to your workspace...</div>
        <section v-else-if="!selected?.messages.length" class="welcome">
          <div class="map-art" aria-hidden="true">
            <div class="map-grid"></div><span class="map-route"></span>
            <span class="map-pin"><i class="fa-solid fa-location-dot"></i></span>
            <span class="map-coordinate">52.3676° N · 4.9041° E</span>
          </div>
          <span class="eyebrow">THE WORLD IS FULL OF POSSIBILITIES</span>
          <h2>Where will your<br>next question take you<span>?</span></h2>
          <p>Turn places, routes, and real-world data into a conversation.<br>Ask naturally. Explore intelligently.</p>
          <div class="prompt-grid">
            <button v-for="prompt in prompts" :key="prompt.title" class="prompt-card" @click="usePrompt(prompt.text)">
              <i class="fa-solid" :class="prompt.icon" aria-hidden="true"></i>
              <strong>{{ prompt.title }}</strong><span>{{ prompt.text }}</span>
              <i class="fa-solid fa-arrow-up-right-from-square prompt-arrow" aria-hidden="true"></i>
            </button>
          </div>
        </section>
        <template v-else>
          <div class="conversation-start"><span>EXPLORATION STARTED {{ formatDate(selected.createdAt) }}</span></div>
          <article v-for="message in selected.messages" :key="message.id" class="message-row" :class="message.role">
            <div class="avatar" aria-hidden="true"><i class="fa-solid" :class="message.role === 'user' ? 'fa-user' : 'fa-location-crosshairs'"></i></div>
            <div class="message-body">
              <div class="message-label">{{ message.role === "user" ? "You" : "Location agent" }}<time>{{ formatTime(message.createdAt) }}</time></div>
              <div class="message-bubble" :class="{ failed: message.status === 'failed' }">
                <span v-if="message.status === 'pending'" class="thinking">
                  <template v-if="new Date(selected.busyUntil).getTime() > clock"><i class="fa-solid fa-circle-notch fa-spin" aria-hidden="true"></i> Exploring your question...</template>
                  <template v-else>This reply was interrupted. You can send your question again.</template>
                </span>
                <span v-else>{{ message.content }}</span>
              </div>
              <div v-if="message.role !== 'user' && message.status === 'completed' && showUsage" class="usage">
                <span><i class="fa-solid fa-microchip" aria-hidden="true"></i> {{ message.model || "Model not reported" }}</span>
                <span>{{ displayTokens(message.inputTokens) }} input / {{ displayTokens(message.outputTokens) }} output tokens</span>
              </div>
              <span v-if="message.status === 'failed'" class="failed-label">Reply failed · your question was saved</span>
            </div>
          </article>
        </template>
      </div>

      <div class="composer-area">
        <form class="composer" @submit.prevent="send">
          <label class="sr-only" for="chat-message">Your message</label>
          <textarea id="chat-message" ref="composer" v-model="draft" rows="2"
                    :maxlength="runtime?.maxMessageLength || 8000"
                    :disabled="loading || !selected || busy"
                    :placeholder="selected ? 'Ask about places, routes, or what’s nearby...' : 'Start a new conversation to explore...'"
                    @keydown.enter.exact.prevent="send"></textarea>
          <div class="composer-bottom"><span><i class="fa-solid fa-wand-magic-sparkles" aria-hidden="true"></i> TomTom location intelligence</span>
            <button type="submit" class="send-button" aria-label="Send message" :disabled="!draft.trim() || busy || !selected">
              <i class="fa-solid" :class="busy ? 'fa-circle-notch fa-spin' : 'fa-arrow-up'" aria-hidden="true"></i>
            </button>
          </div>
        </form>
        <p class="composer-hint">Enter to send · Shift + Enter for a new line <span>AI can make mistakes. Verify important details.</span></p>
      </div>
    </main>

    <footer class="statusbar">
      <span><i class="fa-solid fa-database" aria-hidden="true"></i>
        {{ databaseConnected ? "Connected" : "Not connected" }} · {{ runtime?.database || "Cosmos DB" }}
        <span v-if="runtime" class="database-detail">/ {{ runtime.container }} · {{ runtime.databaseHost }}</span>
      </span>
      <span><span class="status-dot" :class="{ muted: !runtime }"></span>{{ runtime?.environment || "Connecting" }}
        <span v-if="runtime?.emulator" class="emulator-tag">LOCAL EMULATOR</span></span>
    </footer>

    <dialog ref="newDialog" class="options-dialog" aria-labelledby="new-title">
      <form @submit.prevent="createSession">
        <div class="dialog-heading"><h2 id="new-title">A new exploration</h2>
          <button type="button" class="icon-button" aria-label="Close dialog" @click="newDialog.close()"><i class="fa-solid fa-xmark" aria-hidden="true"></i></button></div>
        <label for="session-title">Conversation name</label>
        <input id="session-title" v-model="sessionTitle" autofocus maxlength="120" placeholder="e.g. A weekend in Amsterdam">
        <p>Messages and model usage are saved to your private Cosmos DB partition.</p>
        <button class="primary-button" type="submit">Start exploring <i class="fa-solid fa-arrow-right" aria-hidden="true"></i></button>
      </form>
    </dialog>
    <dialog ref="optionsDialog" class="options-dialog" aria-labelledby="options-title">
      <form @submit.prevent="savePreferences">
        <div class="dialog-heading"><h2 id="options-title">Workspace options</h2>
          <button type="button" class="icon-button" aria-label="Close options" @click="optionsDialog.close()"><i class="fa-solid fa-xmark" aria-hidden="true"></i></button></div>
        <label class="toggle"><span>Show model &amp; token usage</span><input v-model="showUsage" type="checkbox"></label>
        <label class="toggle"><span>Compact message spacing</span><input v-model="compact" type="checkbox"></label>
        <dl><dt>Environment</dt><dd>{{ runtime?.environment || "Unavailable" }}</dd>
          <dt>Database</dt><dd>{{ runtime?.database || "Unavailable" }}</dd>
          <dt>Model deployment</dt><dd>{{ runtime?.model || "Not configured" }}</dd>
          <dt>Authentication</dt><dd>{{ runtime?.environment === "Development" ? "Local Azure CLI identity" : "Container Apps Easy Auth" }}</dd></dl>
        <p>Connection settings and credentials are managed on the server, not in this browser.</p>
        <button class="primary-button" type="submit">Save preferences <i class="fa-solid fa-check" aria-hidden="true"></i></button>
      </form>
    </dialog>
  </div>
</template>
