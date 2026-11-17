<script setup>
import AppIcon from "./AppIcon.vue";

defineProps({
  databaseConnected: { type: Boolean, default: false },
  runtime: { type: Object, default: null }
});
</script>

<template>
  <footer class="statusbar">
    <span>
      <AppIcon name="database" />
      {{ databaseConnected ? "Connected" : "Not connected" }} · {{ runtime?.database || "Cosmos DB" }}
      <span v-if="runtime" class="database-detail">/ {{ runtime.container }} · {{ runtime.databaseHost }}</span>
    </span>
    <span>
      <span class="status-dot" :class="{ muted: !runtime }"></span>{{ runtime?.environment || "Connecting" }}
      <span v-if="runtime?.emulator" class="emulator-tag">LOCAL EMULATOR</span>
    </span>
  </footer>
</template>
