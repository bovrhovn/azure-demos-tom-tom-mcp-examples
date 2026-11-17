<script setup>
import { computed, ref, watch } from "vue";
import AppIcon from "./components/AppIcon.vue";
import { copyMessage, formatMessage } from "./messageFormatting.js";

const props = defineProps({ content: { type: String, required: true } });
const html = computed(() => formatMessage(props.content));
const copying = ref(false);
const copied = ref(false);
const error = ref("");
watch(() => props.content, () => { copied.value = false; error.value = ""; });

async function copy() {
  copying.value = true;
  copied.value = false;
  error.value = "";
  try { await copyMessage(props.content, html.value); copied.value = true; }
  catch (reason) { error.value = `Could not copy message: ${reason.message}`; }
  finally { copying.value = false; }
}
</script>

<template>
  <div class="formatted-message" v-html="html"></div>
  <div class="message-actions">
    <button class="icon-button" :disabled="copying" :aria-label="copied ? 'Copy again' : 'Copy agent message'"
            :title="copied ? 'Copied' : 'Copy formatted message'" @click="copy">
      <AppIcon :name="copied ? 'circle-check' : 'copy'" />
    </button>
    <span role="status">{{ copied ? "Copied" : "" }}</span>
    <span v-if="error" role="alert">{{ error }}</span>
  </div>
</template>
