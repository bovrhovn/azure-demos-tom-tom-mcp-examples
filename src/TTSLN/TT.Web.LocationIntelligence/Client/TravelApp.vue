<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { createApi } from "./api.js";
import AgentMessage from "./AgentMessage.vue";
import AppIcon from "./components/AppIcon.vue";
import WorkspaceHeader from "./components/WorkspaceHeader.vue";
import { useRuntimeConfig } from "./composables/useRuntimeConfig.js";
import { createRuntimeApi } from "./services/runtimeApi.js";
import "./travel.css";

const props = defineProps({ scenario: { type: String, default: "" } });
const magentic = computed(() => props.scenario === "magentic");
const runtime = useRuntimeConfig();
let runtimeApi;
const http = createApi(() => runtime.csrfToken.value, {
  getHeaderName: () => runtime.antiforgeryHeaderName.value,
  onCsrfInvalid: () => runtime.refreshRuntime(runtimeApi)
});
runtimeApi = createRuntimeApi(http, () => runtime.endpoints.value.runtime);
const config = ref(null);
const startCountry = ref("");
const destinationCountry = ref("");
const departureDate = ref("");
const connected = ref(false);
const busy = ref(false);
const error = ref("");
const status = ref("Ready for departure");
const events = ref([]);
const weather = ref([]);
const result = ref(null);
const completed = ref(false);
const started = ref(false);
const canSearch = computed(() => config.value?.configured && connected.value && !busy.value &&
  startCountry.value && destinationCountry.value && startCountry.value !== destinationCountry.value &&
  departureDate.value >= config.value.minDate && departureDate.value <= config.value.maxDate);
const activities = computed(() => events.value.filter(event => !["weather", "result", "completed"].includes(event.type)));
const names = { TravelAssistant: "Travel assistant", WeatherChecker: "Weather checker", RoutePlanner: "Route planner", TravelManager: "Magentic manager", system: "Workflow" };
const steps = computed(() => magentic.value
  ? ["Create a plan", "Delegate tasks", "Follow the sunshine", "Refine the route"]
  : ["Receive your trip", "Hand off for weather", "Route & recharge"]);
let connection;
let subscription;
let generation = 0;
let disposed = false;

function stop(message = "Stopped. Partial findings are not a complete itinerary.") {
  generation++;
  subscription?.dispose();
  subscription = null;
  busy.value = false;
  status.value = message;
}

function receive(event) {
  events.value.push({ ...event, receivedAt: new Date().toLocaleTimeString() });
  if (event.type === "status") status.value = event.data;
  if (event.type === "agent-start") status.value = `${names[event.agent] ?? event.agent} is working`;
  if (event.type === "task") status.value = `Next: ${names[event.data.nextAgent] ?? event.data.nextAgent}`;
  if (event.type === "weather") {
    const index = weather.value.findIndex(item => item.location === event.data.location);
    if (index >= 0) weather.value[index] = event.data;
    else weather.value.push(event.data);
  }
  if (event.type === "result") result.value = event.data;
  if (event.type === "error") { error.value = event.data; busy.value = false; status.value = "Planning could not finish"; }
  if (event.type === "completed") {
    completed.value = true;
    busy.value = false;
    status.value = result.value?.outcome === "postpone" ? "Weather review complete: postpone travel" : "Your journey is ready to review";
  }
}

async function submit() {
  if (!canSearch.value) return;
  const current = ++generation;
  busy.value = true;
  error.value = "";
  events.value = [];
  weather.value = [];
  result.value = null;
  completed.value = false;
  started.value = true;
  status.value = "Sending your trip to the team";
  try {
    const { ticket } = await http(config.value.queriesPath, { method: "POST", body: {
      scenario: props.scenario, startCountry: startCountry.value,
      destinationCountry: destinationCountry.value, departureDate: departureDate.value
    } });
    if (disposed || generation !== current) return;
    subscription = connection.stream("Plan", ticket).subscribe({
      next: event => { if (!disposed && generation === current) receive(event); },
      error: () => {
        if (disposed || generation !== current) return;
        error.value = "The live connection was interrupted. Partial findings are not a complete itinerary. Search again to start a new run.";
        busy.value = false;
        status.value = "Connection interrupted";
      },
      complete: () => {
        if (disposed || generation !== current) return;
        busy.value = false;
        if (!completed.value && !error.value) {
          error.value = "The stream ended before planning completed. Partial findings are not a complete itinerary.";
          status.value = "Planning interrupted";
        }
      }
    });
  } catch (reason) {
    if (disposed || generation !== current) return;
    error.value = reason.message;
    busy.value = false;
    status.value = "Could not start planning";
  }
}

onMounted(async () => {
  if (!props.scenario) return;
  try {
    await runtime.bootstrap(runtimeApi);
    const settings = await http("/api/travel/config");
    if (disposed) return;
    config.value = settings;
    departureDate.value = settings.minDate;
    connection = new HubConnectionBuilder().withUrl(settings.hubPath)
      .withAutomaticReconnect().configureLogging(LogLevel.Warning).build();
    connection.onreconnecting(() => {
      connected.value = false;
      if (busy.value) stop("Connection lost. Runs are not replayed; search again after reconnecting.");
    });
    connection.onreconnected(() => { connected.value = true; });
    connection.onclose(() => {
      connected.value = false;
      if (busy.value) stop("Connection closed. Reload to reconnect. Partial findings are not a complete itinerary.");
    });
    await connection.start();
    if (disposed) { await connection.stop(); return; }
    connected.value = true;
  } catch (reason) {
    if (!disposed) error.value = `Could not connect to the travel lab: ${reason.message}`;
  }
});
onBeforeUnmount(() => {
  disposed = true;
  stop();
  connection?.stop().catch(() => console.warn("Could not close the travel connection."));
});
</script>

<template>
  <div class="travel-workspace">
    <WorkspaceHeader active-page="travel" :show-options="false" />
    <main class="travel-main">
      <nav v-if="scenario" class="travel-breadcrumb" aria-label="Travel scenarios">
        <a href="/Travel">Travel lab</a><span>/</span>
        <a href="/Travel/Handoff" :aria-current="!magentic ? 'page' : undefined">Handoff</a>
        <a href="/Travel/Magentic" :aria-current="magentic ? 'page' : undefined">Magentic</a>
      </nav>
      <section class="travel-hero">
        <div>
          <p class="travel-eyebrow"><span></span> THE MULTI-AGENT TRAVEL LAB</p>
          <h1 v-if="!scenario">One journey.<br><em>Different minds.</em></h1>
          <h1 v-else-if="magentic">Chase the sun.<br><em>Rethink the route.</em></h1>
          <h1 v-else>A good trip starts<br><em>with a handoff.</em></h1>
          <p class="travel-deck">{{ !scenario
            ? "Two ways to bring a trip together. Watch specialist agents turn live weather and road intelligence into your next electric adventure."
            : magentic ? "A manager makes the plan. Specialists check the skies and the roads. When conditions change, the team changes course."
              : "Your travel assistant passes the baton to a weather specialist, then a route planner. Follow every step from forecast to charging stop." }}</p>
        </div>
        <div class="travel-atlas" aria-hidden="true">
          <span class="atlas-caption">EUROPE / ELECTRIC EXPLORATIONS</span>
          <svg viewBox="0 0 360 260" fill="none">
            <path class="atlas-grid" d="M0 65h360M0 130h360M0 195h360M60 0v260M120 0v260M180 0v260M240 0v260M300 0v260" />
            <circle class="atlas-orbit" cx="180" cy="130" r="104" />
            <path class="atlas-path" d="M65 199h54q19 0 19-19v-51q0-19 19-19h57q19 0 19-19V59h52" />
            <circle cx="65" cy="199" r="8" fill="#ecf1b1" /><circle cx="285" cy="59" r="8" fill="#ecf1b1" />
            <circle cx="180" cy="110" r="6" fill="#f3b45a" />
            <circle cx="279" cy="170" r="27" stroke="#f3b45a" stroke-width="2" />
            <path d="M279 131v-9m0 96v-9m39-39h9m-96 0h9m67-28 7-7m-70 70 7-7m56 0 7 7m-70-70 7 7" stroke="#f3b45a" stroke-width="2" />
          </svg>
          <span class="atlas-coordinate">LIVE WEATHER &nbsp; / &nbsp; REAL ROADS &nbsp; / &nbsp; OPEN POSSIBILITIES</span>
        </div>
      </section>

      <template v-if="!scenario">
        <div class="travel-section-title"><h2>Choose your orchestration.</h2><span>02 SCENARIOS / ONE DESTINATION</span></div>
        <section class="travel-scenarios" aria-label="Choose a travel scenario">
          <a class="travel-scenario-card" href="/Travel/Handoff">
            <div class="scenario-card-top"><span>01 / HANDOFF</span><AppIcon name="arrow-up-right-from-square" /></div>
            <div class="scenario-diagram"><span>Travel</span><b>&rarr;</b><span>Weather</span><b>&rarr;</b><span>Route</span></div>
            <h3>Pass the baton.</h3>
            <p>A focused chain of specialists. Receive the trip, check both forecasts, then build a route with electric charging stops.</p>
            <span class="travel-card-cta">Explore Handoff <AppIcon name="arrow-right" /></span>
          </a>
          <a class="travel-scenario-card magentic-card" href="/Travel/Magentic">
            <div class="scenario-card-top"><span>02 / MAGENTIC</span><AppIcon name="arrow-up-right-from-square" /></div>
            <div class="scenario-diagram"><span>Plan</span><b>&harr;</b><span>Specialists</span><b>&harr;</b><span>Adapt</span></div>
            <h3>Find a sunnier way.</h3>
            <p>A manager plans, delegates and replans. Check the weather, adapt the journey, and postpone if sunshine cannot be verified.</p>
            <span class="travel-card-cta">Explore Magentic <AppIcon name="arrow-right" /></span>
          </a>
        </section>
        <p class="travel-footnote">Real Agent Framework workflows. Microsoft Foundry + WebIQ + TomTom MCP. No simulated answers.</p>
      </template>

      <template v-else>
        <div class="travel-step-strip" aria-label="Orchestration stages">
          <div v-for="(step, index) in steps" :key="step"><span>0{{ index + 1 }}</span>{{ step }}</div>
        </div>
        <form class="travel-form" @submit.prevent="submit">
          <div class="travel-form-heading"><div><p class="travel-eyebrow">YOUR DEPARTURE BOARD</p><h2>Where are we going?</h2></div><span class="travel-mode">{{ magentic ? "SUNNY-ONLY / ADAPTIVE" : "WEATHER-AWARE / HANDOFF" }}</span></div>
          <div class="travel-form-fields">
            <label for="travel-start">Start country
              <select id="travel-start" v-model="startCountry" :disabled="busy || !config" required>
                <option disabled value="">Choose your departure</option>
                <option v-for="country in config?.countries ?? []" :key="country.code" :value="country.code">{{ country.name }} &middot; {{ country.capital }}</option>
              </select>
            </label>
            <span class="travel-form-arrow" aria-hidden="true">&rarr;</span>
            <label for="travel-destination">Destination country
              <select id="travel-destination" v-model="destinationCountry" :disabled="busy || !config" required>
                <option disabled value="">Choose your destination</option>
                <option v-for="country in config?.countries ?? []" :key="country.code" :value="country.code" :disabled="country.code === startCountry">{{ country.name }} &middot; {{ country.capital }}</option>
              </select>
            </label>
            <label for="travel-date">Departure date (UTC)
              <input id="travel-date" v-model="departureDate" type="date" :min="config?.minDate" :max="config?.maxDate" :disabled="busy || !config" required>
            </label>
          </div>
          <div class="travel-form-bottom">
            <p>Capital-to-capital electric-car trips. <a v-if="config" :href="config.countrySource" target="_blank" rel="noopener noreferrer">EU member countries</a>.<br>{{ magentic ? "The origin stays fixed. A sunnier destination may be proposed; otherwise the team recommends postponing." : "Weather warnings help you decide. Charger compatibility and availability still need your confirmation." }}</p>
            <button v-if="busy" class="travel-search travel-stop" type="button" @click="stop()">Stop planning <AppIcon name="xmark" /></button>
            <button v-else class="travel-search" type="submit" :disabled="!canSearch">Start searching <AppIcon name="arrow-right" /></button>
          </div>
        </form>
        <p v-if="config && !config.configured" class="travel-warning" role="status">Configure Foundry, TomTom and WebIQ:Key on the server to start live planning.</p>
        <p v-if="error" class="travel-error" role="alert">{{ error }}</p>
        <section class="travel-results" :aria-busy="busy" aria-label="Travel planning results">
          <div class="travel-section-title"><h2>Your journey, unfolding.</h2><span class="travel-status" role="status"><i :class="{ connected }"></i>{{ status }}</span></div>
          <div v-if="!started" class="travel-empty"><AppIcon name="compass" /><h3>Good things are on the horizon.</h3><p>Choose two countries and let the team get to work.<br>Forecasts, agent activity and your itinerary will appear here.</p></div>
          <template v-else>
            <div v-if="weather.length" class="travel-weather-grid" aria-label="Weather findings">
              <article v-for="item in weather" :key="item.location" class="travel-weather" :class="`weather-${item.condition}`">
                <div><span>{{ item.condition === "sunny" ? "SUNSHINE IN THE FORECAST" : item.condition === "unknown" ? "FORECAST NOT VERIFIED" : "WEATHER ADVISORY" }}</span><b aria-hidden="true">{{ item.condition === "sunny" ? "☀" : "☁" }}</b></div>
                <h3>{{ item.location }}</h3><p>{{ item.summary }}</p>
                <a v-for="(source, index) in item.sources" :key="source" :href="source" target="_blank" rel="noopener noreferrer">Forecast source {{ index + 1 }} <AppIcon name="arrow-up-right-from-square" /></a>
              </article>
            </div>
            <div class="travel-live-layout">
              <aside class="travel-timeline" aria-label="Live agent activity">
                <p class="travel-eyebrow">THE TEAM AT WORK</p>
                <p class="travel-ephemeral">Live findings stay on this page only. Stopping or disconnecting cancels the run; reconnecting never replays it.</p>
                <div v-for="(event, index) in activities" :key="index" class="travel-event" :class="`event-${event.type}`">
                  <div class="travel-event-heading"><strong>{{ names[event.agent] ?? event.agent }}</strong><time>{{ event.receivedAt }}</time></div>
                  <span class="travel-event-type">{{ event.type.replaceAll("-", " ") }}</span>
                  <p v-if="['status', 'warning', 'agent-start', 'agent-finished', 'route-reviewed', 'error'].includes(event.type)">{{ event.data }}</p>
                  <p v-else-if="event.type === 'task'">{{ event.data.instruction }}</p>
                  <div v-else-if="['plan', 'replan'].includes(event.type)" class="travel-plan"><AgentMessage :content="event.data" /></div>
                  <details v-else><summary>{{ event.type === "agent-result" ? "Read findings" : event.data?.name ?? "View details" }}</summary>
                    <AgentMessage v-if="event.type === 'agent-result'" :content="event.data" />
                    <pre v-else>{{ JSON.stringify(event.data, null, 2) }}</pre>
                  </details>
                </div>
                <div v-if="busy" class="travel-working"><AppIcon name="spinner" spin /> Live specialists are working</div>
              </aside>
              <article class="travel-itinerary">
                <template v-if="result">
                  <p class="travel-eyebrow">{{ result.outcome === "postpone" ? "A BETTER DAY AHEAD" : "YOUR ELECTRIC ESCAPE" }}</p>
                  <h2>{{ result.outcome === "postpone" ? "Let the weather lead." : "The road ahead." }}</h2>
                  <p v-if="result.outcome === 'postpone'" class="travel-warning">A complete sunny route could not be verified. Do not treat partial route findings as a recommendation to travel.</p>
                  <AgentMessage :content="result.text" />
                  <p class="travel-footnote">Forecasts change. Check road safety, ferry access, vehicle range, charger compatibility and live availability before departure.</p>
                </template>
                <div v-else class="travel-itinerary-pending"><AppIcon :name="busy ? 'route' : 'circle-exclamation'" /><h3>{{ busy ? "Connecting sky and road." : "No complete itinerary yet." }}</h3><p>{{ busy ? "Follow the live timeline while the team checks the evidence." : "Review the findings and any errors before starting a new search." }}</p></div>
              </article>
            </div>
          </template>
        </section>
      </template>
      <footer class="travel-footer"><span>TOMTOM / LOCATION INTELLIGENCE</span><span>Made for the journey, not just the destination.</span></footer>
    </main>
  </div>
</template>
