<script setup>
import { ref } from "vue";
import AppIcon from "./AppIcon.vue";

defineProps({
  showUsage: { type: Boolean, default: true },
  compact: { type: Boolean, default: false },
  runtime: { type: Object, default: null },
  databaseConnected: { type: Boolean, default: false }
});

const emit = defineEmits(["update:showUsage", "update:compact", "save"]);
const dialogRef = ref(null);

function open() {
  dialogRef.value?.showModal();
}

function close() {
  dialogRef.value?.close();
}

function submit() {
  emit("save");
  dialogRef.value?.close();
}

defineExpose({
  open,
  close
});
</script>

<template>
  <dialog ref="dialogRef" class="options-dialog" aria-labelledby="options-title">
    <form @submit.prevent="submit">
      <div class="dialog-heading">
        <h2 id="options-title">Workspace options</h2>
        <button type="button" class="icon-button" aria-label="Close options" @click="close">
          <AppIcon name="xmark" />
        </button>
      </div>
      <label class="toggle">
        <span>Show model &amp; token usage</span>
        <input
          :checked="showUsage"
          type="checkbox"
          @change="emit('update:showUsage', $event.target.checked)"
        >
      </label>
      <label class="toggle">
        <span>Compact message spacing</span>
        <input
          :checked="compact"
          type="checkbox"
          @change="emit('update:compact', $event.target.checked)"
        >
      </label>
      <dl>
        <dt>Environment</dt><dd>{{ runtime?.environment || "Unavailable" }}</dd>
        <dt>Database</dt><dd>{{ runtime?.database || "Unavailable" }}</dd>
        <dt>Model deployment</dt><dd>{{ runtime?.model || "Not configured" }}</dd>
        <dt>Authentication</dt><dd>{{ runtime?.environment === "Development" ? "Local Azure CLI identity" : "Container Apps Easy Auth" }}</dd>
      </dl>
      <p>Connection settings and credentials are managed on the server, not in this browser.</p>
      <button type="submit" class="primary-button">
        Save preferences <AppIcon name="check" />
      </button>
    </form>
  </dialog>
</template>
