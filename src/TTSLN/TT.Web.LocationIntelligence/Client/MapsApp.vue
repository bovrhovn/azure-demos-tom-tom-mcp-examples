<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { createApi } from "./api.js";
import AgentMessage from "./AgentMessage.vue";
import AppIcon from "./components/AppIcon.vue";
import McpMapApp from "./components/McpMapApp.vue";
import WorkspaceHeader from "./components/WorkspaceHeader.vue";
import { useRuntimeConfig } from "./composables/useRuntimeConfig.js";
import { createRuntimeApi } from "./services/runtimeApi.js";
import "./maps.css";

const runtimeConfig = useRuntimeConfig();
let runtimeApi;
const http = createApi(() => runtimeConfig.csrfToken.value, {
  getHeaderName: () => runtimeConfig.antiforgeryHeaderName.value,
  onCsrfInvalid: () => runtimeConfig.refreshRuntime(runtimeApi)
});
runtimeApi = createRuntimeApi(http, () => runtimeConfig.endpoints.value.runtime);
const config = ref(null);
const prompt = ref("");
const connected = ref(false);
const busy = ref(false);
const error = ref("");
const answer = ref("");
const status = ref("Ready to explore");
const tools = ref([]);
const apps = ref([]);
const events = ref([]);
const showEvents = ref(false);
const started = ref(false);
const completed = ref(false);
const canSend = computed(() => connected.value && runtimeConfig.agentConfigured.value &&
  !busy.value && prompt.value.trim() && prompt.value.length <= (config.value?.maxMessageLength ?? 0));
let connection;
let subscription;
let disposed = false;
let generation = 0;

function stop(message = "Stopped. Partial results are shown below.") {
  generation++;
  subscription?.dispose();
  subscription = null;
  busy.value = false;
  status.value = message;
}

function receive(event) {
  events.value.push(event);
  if (event.type === "status") status.value = event.data;
  if (event.type === "text") answer.value += event.data;
  if (event.type === "tool-start") tools.value.push({ ...event.data, state: "running" });
  if (event.type === "tool-result") {
    const tool = tools.value.find((item) => item.callId === event.data.callId);
    if (tool) Object.assign(tool, { result: event.data.result, state: event.data.result.isError ? "failed" : "completed" });
  }
  if (event.type === "app") apps.value.push(event.data);
  if (event.type === "error") { error.value = event.data; busy.value = false; status.value = "Query failed"; }
  if (event.type === "completed") { completed.value = true; busy.value = false; status.value = "Exploration complete"; }
}

async function submit() {
  if (!canSend.value) return;
  const current = ++generation;
  busy.value = true;
  error.value = "";
  answer.value = "";
  tools.value = [];
  apps.value = [];
  events.value = [];
  started.value = true;
  completed.value = false;
  status.value = "Starting your exploration";
  try {
    const { ticket } = await http(config.value.queriesPath, { method: "POST", body: { prompt: prompt.value } });
    if (disposed || current !== generation) return;
    subscription = connection.stream("Query", ticket).subscribe({
      next: (event) => { if (current === generation) receive(event); },
      error: () => {
        if (current !== generation || disposed) return;
        error.value = "The live stream was interrupted. Submit the question again to start a new query.";
        busy.value = false;
        status.value = "Stream interrupted";
      },
      complete: () => {
        if (current !== generation || disposed) return;
        busy.value = false;
        if (!completed.value && !error.value) {
          error.value = "The stream ended before the query completed. Partial results are shown below.";
          status.value = "Stream interrupted";
        }
      }
    });
  } catch (reason) {
    if (current !== generation || disposed) return;
    error.value = reason.message;
    busy.value = false;
    status.value = "Could not start query";
  }
}

function callAppTool(name, arguments_, signal) {
  return http(config.value.appToolsPath, { method: "POST", body: { name, arguments: arguments_ }, signal });
}

onMounted(async () => {
  try {
    await runtimeConfig.bootstrap(runtimeApi);
    const settings = await http("/api/maps/config");
    if (disposed) return;
    config.value = settings;
    prompt.value = settings.defaultPrompt;
    connection = new HubConnectionBuilder().withUrl(settings.hubPath)
      .withAutomaticReconnect().configureLogging(LogLevel.Warning).build();
    connection.onreconnecting(() => {
      connected.value = false;
      if (busy.value) stop("Connection lost. Submit again after reconnection; queries are not replayed.");
    });
    connection.onreconnected(() => { connected.value = true; });
    connection.onclose(() => {
      connected.value = false;
      if (busy.value) stop("Connection closed. Reload this page to reconnect.");
    });
    await connection.start();
    if (disposed) { await connection.stop(); return; }
    connected.value = true;
  } catch (reason) {
    if (!disposed) error.value = `Could not connect to Live Maps: ${reason.message}`;
  }
});
onBeforeUnmount(() => {
  disposed = true;
  stop();
  connection?.stop().catch(() => console.warn("Could not close the Live Maps connection."));
});
</script>

<template>
  <div class="maps-workspace">
    <WorkspaceHeader active-page="maps" :show-options="false" />
    <main class="maps-main">
      <section class="maps-intro">
        <div>
          <p class="maps-kicker"><span></span> LIVE MAPS LAB</p>
          <h1>Less searching.<br>More <em>getting there.</em></h1>
          <p class="maps-description">Ask a question. Follow the progress. Explore the real world<br class="maps-wide-break"> through interactive TomTom maps.</p>
        </div>
        <div class="maps-route-art" aria-hidden="true">
          <svg viewBox="0 0 360 170" fill="none">
            <path class="art-contour" d="M-20 130Q60 30 130 90T380 40M-20 150Q60 50 130 110T380 60M-20 170Q60 70 130 130T380 80M-20 110Q60 10 130 70T380 20M-20 90Q60-10 130 50T380 0" />
            <path class="art-route" d="M60 122h64q16 0 16-16V76q0-16 16-16h82q18 0 18-18V32" />
            <circle cx="60" cy="122" r="7" fill="#faf8f4" stroke="#df1b32" stroke-width="3" />
            <circle cx="256" cy="32" r="8" fill="#df1b32" />
          </svg>
          <span>NATURAL LANGUAGE &rarr; REAL-WORLD CONTEXT</span>
        </div>
      </section>

      <form class="maps-query" @submit.prevent="submit">
        <label for="maps-prompt">Where will your next question take you?</label>
        <textarea id="maps-prompt" v-model="prompt" rows="3" :maxlength="config?.maxMessageLength"
                  :disabled="busy || !config" aria-describedby="maps-query-note"></textarea>
        <div class="maps-query-bottom">
          <span id="maps-query-note"><AppIcon name="layer-group" /> Live tools. Interactive maps. No saved history.</span>
          <button v-if="busy" class="maps-stop" type="button" @click="stop()">Stop exploration <AppIcon name="xmark" /></button>
          <button v-else class="maps-submit" type="submit" :disabled="!canSend">Explore with TomTom <AppIcon name="arrow-right" /></button>
        </div>
      </form>
      <p v-if="config && !runtimeConfig.agentConfigured.value" class="maps-notice">Configure the Foundry endpoint, model, and TomTom API key on the server to explore.</p>
      <p v-if="error" class="maps-error" role="alert">{{ error }}</p>

      <section class="maps-results" aria-label="Live exploration results" :aria-busy="busy">
        <div class="maps-section-heading">
          <h2>{{ started ? "Your exploration" : "A world of possibilities" }}</h2>
          <span class="maps-live-status" role="status"><span class="status-dot" :class="{ muted: !connected }"></span>{{ connected ? status : "Connecting to live stream" }}</span>
        </div>
        <div v-if="!started" class="maps-empty">
          <AppIcon name="compass" />
          <h3>A question is all it takes.</h3>
          <p>Routes, places, and the stops in between.<br>Your live maps and streamed answer will appear here.</p>
          <span>POWERED BY TOMTOM MCP APPS + MICROSOFT FOUNDRY</span>
        </div>
        <div v-else class="maps-result-layout">
          <aside class="maps-activity" aria-label="Tool activity">
            <p class="maps-kicker">THE JOURNEY</p>
            <div class="maps-activity-item"><AppIcon name="check" /><span>Question received</span></div>
            <div v-for="tool in tools" :key="tool.callId" class="maps-activity-item" :class="tool.state">
              <AppIcon :name="tool.state === 'running' ? (busy ? 'spinner' : 'xmark') : tool.state === 'failed' ? 'circle-exclamation' : 'check'"
                       :spin="tool.state === 'running' && busy" />
              <span>{{ tool.name.replace("tomtom-", "").replaceAll("-", " ") }}<small>{{ tool.state === 'running' && !busy ? 'interrupted' : tool.state }}</small></span>
            </div>
            <div v-if="busy && !tools.length" class="maps-activity-item"><AppIcon name="spinner" spin /><span>{{ status }}</span></div>
            <p class="maps-ephemeral"><AppIcon name="circle-exclamation" /> This exploration lives only on this page.</p>
          </aside>
          <div class="maps-output">
            <McpMapApp v-for="app in apps" :key="app.callId" :app="app" :call-tool="callAppTool"
                       :timeout-milliseconds="config.appInitializationTimeoutMilliseconds" />
            <div v-if="busy && !apps.length" class="maps-pending-map"><AppIcon name="route" /><span>Connecting the dots<span class="maps-pulse">...</span></span><small>Live tool results will open as interactive maps.</small></div>
            <article v-if="answer" class="maps-answer">
              <p class="maps-kicker"><AppIcon name="location-crosshairs" /> THE LOCAL PICTURE <span v-if="busy" class="maps-writing">STREAMING</span></p>
              <AgentMessage :content="answer" />
            </article>
            <p v-if="completed && !apps.length" class="maps-notice">TomTom did not return an interactive app for this question. The complete answer and tool data are available here.</p>
            <details v-if="events.length" class="maps-event-log" @toggle="showEvents = $event.target.open">
              <summary>Under the hood <span>{{ events.length }} live events</span></summary>
              <template v-if="showEvents">
                <div v-for="(event, index) in events" :key="index"><strong>{{ event.type }}</strong><pre>{{ JSON.stringify(event.data, null, 2) }}</pre></div>
              </template>
            </details>
          </div>
        </div>
      </section>
      <footer class="maps-footer"><span>TOMTOM LOCATION INTELLIGENCE</span><span>Real data. New perspectives.</span></footer>
    </main>
  </div>
</template>
