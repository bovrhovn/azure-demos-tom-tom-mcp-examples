<script setup>
import AppIcon from "./AppIcon.vue";

const emit = defineEmits(["select-prompt"]);

const prompts = [
  { icon: "location-dot", title: "Find a place", text: "Where is the Eiffel Tower? Give me its coordinates." },
  { icon: "route", title: "Plan a route", text: "Help me plan a driving route from Amsterdam to Rotterdam." },
  { icon: "charging-station", title: "Explore nearby", text: "Find EV charging stations near Amsterdam Centraal." }
];
</script>

<template>
  <section class="welcome">
    <div class="map-art" aria-hidden="true">
      <div class="map-grid"></div><span class="map-route"></span>
      <span class="map-pin"><AppIcon name="location-dot" /></span>
      <span class="map-coordinate">52.3676° N · 4.9041° E</span>
    </div>
    <span class="eyebrow">THE WORLD IS FULL OF POSSIBILITIES</span>
    <h2>Where will your<br>next question take you<span>?</span></h2>
    <p>Turn places, routes, and real-world data into a conversation.<br>Ask naturally. Explore intelligently.</p>
    <div class="prompt-grid">
      <button
        v-for="prompt in prompts"
        :key="prompt.title"
        type="button"
        class="prompt-card"
        @click="emit('select-prompt', prompt.text)"
      >
        <AppIcon :name="prompt.icon" />
        <strong>{{ prompt.title }}</strong><span>{{ prompt.text }}</span>
        <AppIcon name="arrow-up-right-from-square" class="prompt-arrow" />
      </button>
    </div>
  </section>
</template>
