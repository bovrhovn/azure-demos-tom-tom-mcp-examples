<script setup>
import { computed, ref } from "vue";
import AppIcon from "./AppIcon.vue";

const props = defineProps({
  modelValue: { type: String, default: "" },
  loading: { type: Boolean, default: false },
  hasSession: { type: Boolean, default: false },
  agentConfigured: { type: Boolean, default: true },
  busy: { type: Boolean, default: false },
  turnLimitReached: { type: Boolean, default: false },
  maxMessageLength: { type: Number, default: 4000 },
  maxTurns: { type: Number, default: 40 }
});

const emit = defineEmits(["update:modelValue", "send"]);
const textareaRef = ref(null);

const isDisabled = computed(() =>
  props.loading || !props.hasSession || props.busy || props.turnLimitReached || !props.agentConfigured
);

const canSubmit = computed(() =>
  !isDisabled.value && props.modelValue.trim().length > 0
);

const noticeText = computed(() => {
  if (props.hasSession && props.turnLimitReached) {
    return `This conversation reached the ${props.maxTurns}-turn limit. Start a new conversation to continue.`;
  }
  if (props.hasSession && !props.agentConfigured) {
    return "Agent replies are unavailable until Foundry and TomTom settings are configured.";
  }
  return "";
});

function onInput(event) {
  emit("update:modelValue", event.target.value);
}

function onEnter(event) {
  if (event.isComposing || event.keyCode === 229) return;
  event.preventDefault();
  if (canSubmit.value) {
    emit("send");
  }
}

function onSubmit() {
  if (canSubmit.value) {
    emit("send");
  }
}

defineExpose({
  focus() {
    textareaRef.value?.focus();
  }
});
</script>

<template>
  <div class="composer-area">
    <p v-if="noticeText" class="composer-notice" role="status">
      <AppIcon name="circle-info" /> <span>{{ noticeText }}</span>
    </p>
    <form class="composer" @submit.prevent="onSubmit">
      <label class="sr-only" for="chat-message">Your message</label>
      <textarea
        id="chat-message"
        ref="textareaRef"
        :value="modelValue"
        rows="2"
        :maxlength="maxMessageLength"
        :disabled="isDisabled"
        :placeholder="hasSession ? 'Ask about places, routes, or what’s nearby...' : 'Start a new conversation to explore...'"
        @input="onInput"
        @keydown.enter.exact="onEnter"
      ></textarea>
      <div class="composer-bottom">
        <span><AppIcon name="wand-magic-sparkles" /> TomTom location intelligence</span>
        <div class="composer-actions">
          <span class="char-count" aria-label="Character count">
            {{ modelValue.length.toLocaleString() }} / {{ maxMessageLength.toLocaleString() }}
          </span>
          <button type="submit" class="send-button" aria-label="Send message" :disabled="!canSubmit">
            <AppIcon :name="busy ? 'spinner' : 'arrow-up'" />
          </button>
        </div>
      </div>
    </form>
    <p class="composer-hint">Enter to send · Shift + Enter for a new line <span>AI can make mistakes. Verify important details.</span></p>
  </div>
</template>
