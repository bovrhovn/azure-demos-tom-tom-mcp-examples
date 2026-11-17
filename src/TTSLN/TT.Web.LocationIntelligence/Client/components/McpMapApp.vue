<script setup>
import { onBeforeUnmount, onMounted, ref } from "vue";
import { connectMcpApp } from "../services/mcpAppHost.js";

const props = defineProps({
  app: { type: Object, required: true },
  callTool: { type: Function, required: true },
  timeoutMilliseconds: { type: Number, required: true }
});
const frame = ref(null);
const ready = ref(false);
const error = ref("");
let dispose;
onMounted(() => {
  dispose = connectMcpApp({
    iframe: frame.value, app: props.app, callTool: props.callTool,
    timeoutMilliseconds: props.timeoutMilliseconds,
    onReady: () => { ready.value = true; error.value = ""; },
    onError: (message) => { error.value = message; }
  });
});
onBeforeUnmount(() => dispose?.());
</script>

<template>
  <article class="live-map-card">
    <header>
      <span><span class="status-dot" :class="{ muted: !ready || error }"></span> {{ app.name.replace("tomtom-", "").replaceAll("-", " ") }}</span>
      <span class="map-source">TOMTOM / MCP APP</span>
    </header>
    <p v-if="error" class="map-app-error" role="alert">{{ error }}</p>
    <p v-else-if="!ready" class="map-app-loading" role="status">Opening interactive map...</p>
    <iframe ref="frame" :title="`Interactive TomTom map: ${app.name}`" sandbox="allow-scripts"
            referrerpolicy="no-referrer" allow="camera 'none'; microphone 'none'; geolocation 'none'; clipboard-write 'none'"></iframe>
  </article>
</template>
