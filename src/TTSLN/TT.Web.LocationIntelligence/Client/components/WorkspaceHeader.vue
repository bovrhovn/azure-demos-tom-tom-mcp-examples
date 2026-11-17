<script setup>
import AppIcon from "./AppIcon.vue";

const emit = defineEmits(["open-options"]);
</script>

<template>
  <header class="topbar">
    <a class="brand" href="/" aria-label="TomTom Location Intelligence home">
      <span class="brand-mark" aria-hidden="true"><AppIcon name="location-crosshairs" /></span>
      <span class="wordmark">tomtom<span class="brand-dot">.</span></span>
    </a>
    <span class="header-divider"></span>
    <span class="product-name">Location Intelligence <span class="preview-tag">PLAYGROUND</span></span>
    <a class="docs-link" href="https://docs.tomtom.com/" target="_blank" rel="noopener noreferrer">
      Documentation <AppIcon name="arrow-up-right-from-square" />
    </a>
    <button type="button" class="icon-button" aria-label="Open options" @click="emit('open-options')">
      <AppIcon name="sliders" />
    </button>
  </header>
</template>
