<script setup>
import { nextTick, ref } from "vue";
import AgentMessage from "../AgentMessage.vue";
import { displayTokens, formatDate, formatTime } from "../utils/dateFormatting.js";
import AppIcon from "./AppIcon.vue";
import WelcomeEmptyState from "./WelcomeEmptyState.vue";

const NEAR_BOTTOM_THRESHOLD_PX = 140;

defineProps({
  loading: { type: Boolean, default: false },
  selected: { type: Object, default: null },
  busy: { type: Boolean, default: false },
  showUsage: { type: Boolean, default: true }
});

const emit = defineEmits(["select-prompt"]);
const listEl = ref(null);

function isNearBottom(element) {
  if (!element) return true;
  return element.scrollHeight - element.scrollTop - element.clientHeight <= NEAR_BOTTOM_THRESHOLD_PX;
}

async function scrollToBottom({ force = false } = {}) {
  const shouldScroll = force || isNearBottom(listEl.value);
  await nextTick();
  if (!listEl.value || !shouldScroll) return;
  if (typeof listEl.value.scrollTo === "function") {
    listEl.value.scrollTo({ top: listEl.value.scrollHeight, behavior: "smooth" });
  } else {
    listEl.value.scrollTop = listEl.value.scrollHeight;
  }
}

defineExpose({
  scrollToBottom
});
</script>

<template>
  <div ref="listEl" class="message-list" role="log" aria-label="Conversation messages" aria-live="polite">
    <div v-if="loading" class="loading-state">
      <AppIcon name="spinner" /> Connecting to your workspace...
    </div>
    <WelcomeEmptyState
      v-else-if="!selected?.messages.length"
      @select-prompt="emit('select-prompt', $event)"
    />
    <template v-else>
      <div class="conversation-start">
        <span>EXPLORATION STARTED {{ formatDate(selected.createdAt) }}</span>
      </div>
      <article
        v-for="message in selected.messages"
        :key="message.id"
        v-memo="[message.id, message.status, message.content, showUsage, message.status === 'pending' ? busy : false]"
        class="message-row"
        :class="message.role"
      >
        <div class="avatar" aria-hidden="true">
          <AppIcon :name="message.role === 'user' ? 'user' : 'location-crosshairs'" />
        </div>
        <div class="message-body">
          <div class="message-label">
            {{ message.role === "user" ? "You" : "Location agent" }}<time :datetime="message.createdAt">{{ formatTime(message.createdAt) }}</time>
          </div>
          <div class="message-bubble" :class="{ failed: message.status === 'failed' }">
            <span v-if="message.status === 'pending'" class="thinking">
              <template v-if="busy"><AppIcon name="spinner" /> Exploring your question...</template>
              <template v-else>This reply was interrupted. You can send your question again.</template>
            </span>
            <AgentMessage v-else-if="message.role === 'assistant' && message.status === 'completed'" :content="message.content" />
            <span v-else>{{ message.content }}</span>
          </div>
          <div v-if="message.role !== 'user' && message.status === 'completed' && showUsage" class="usage">
            <span><AppIcon name="microchip" /> {{ message.model || "Model not reported" }}</span>
            <span>{{ displayTokens(message.inputTokens) }} input / {{ displayTokens(message.outputTokens) }} output tokens</span>
          </div>
          <span v-if="message.status === 'failed'" class="failed-label">Reply failed · your question was saved</span>
        </div>
      </article>
    </template>
  </div>
</template>
