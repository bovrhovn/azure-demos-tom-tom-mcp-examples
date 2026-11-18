<script setup>
import AppIcon from "./AppIcon.vue";

const emit = defineEmits(["open-options"]);
defineProps({ activePage: { type: String, default: "chat" }, showOptions: { type: Boolean, default: true } });
</script>

<template>
  <header class="topbar">
    <a class="brand" href="/" aria-label="TomTom Location Intelligence home">
      <span class="brand-mark" aria-hidden="true"><AppIcon name="location-crosshairs" /></span>
      <span class="wordmark">tomtom<span class="brand-dot">.</span></span>
    </a>
    <span class="header-divider"></span>
    <span class="product-name">Location Intelligence <span class="preview-tag">PLAYGROUND</span></span>
    <nav class="workspace-nav" aria-label="Workspaces">
      <a href="/" :aria-current="activePage === 'chat' ? 'page' : undefined">Chat</a>
      <a href="/Maps" :aria-current="activePage === 'maps' ? 'page' : undefined">Live Maps</a>
      <a href="/Travel" :aria-current="activePage === 'travel' ? 'page' : undefined">Travel Lab</a>
    </nav>
    <a class="docs-link" href="https://docs.tomtom.com/" target="_blank" rel="noopener noreferrer">
      Documentation <AppIcon name="arrow-up-right-from-square" />
    </a>
    <button v-if="showOptions" type="button" class="icon-button" aria-label="Open options" @click="emit('open-options')">
      <AppIcon name="sliders" />
    </button>
  </header>
</template>
