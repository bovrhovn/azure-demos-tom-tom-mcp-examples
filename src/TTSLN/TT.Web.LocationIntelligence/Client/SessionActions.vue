<script setup>
import AppIcon from "./components/AppIcon.vue";

defineProps({
  session: { type: Object, required: true },
  disabled: Boolean
});

const emit = defineEmits(["rename", "delete"]);
</script>

<template>
  <div class="session-actions">
    <button
      type="button"
      class="icon-button"
      :aria-label="`Edit ${session.title}`"
      title="Edit conversation"
      :disabled="disabled"
      @click="emit('rename', session)"
    >
      <AppIcon name="pen" />
    </button>
    <button
      type="button"
      class="icon-button delete-button"
      :aria-label="`Delete ${session.title}`"
      title="Delete conversation"
      :disabled="disabled"
      @click="emit('delete', session)"
    >
      <AppIcon name="trash-can" />
    </button>
  </div>
</template>
