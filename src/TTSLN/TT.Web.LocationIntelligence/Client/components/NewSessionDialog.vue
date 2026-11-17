<script setup>
import { nextTick, ref } from "vue";
import AppIcon from "./AppIcon.vue";

const props = defineProps({
  maxTitleLength: { type: Number, default: 120 },
  onCreate: { type: Function, required: true }
});

const dialogRef = ref(null);
const inputRef = ref(null);
const sessionTitle = ref("");
const creating = ref(false);

async function open() {
  sessionTitle.value = "";
  dialogRef.value?.showModal();
  await nextTick();
  inputRef.value?.focus();
}

function close() {
  dialogRef.value?.close();
}

async function submit() {
  if (creating.value) return;
  creating.value = true;
  try {
    const created = await props.onCreate(sessionTitle.value);
    if (created) {
      sessionTitle.value = "";
      dialogRef.value?.close();
    }
  } finally {
    creating.value = false;
  }
}

defineExpose({
  open,
  close
});
</script>

<template>
  <dialog ref="dialogRef" class="options-dialog" aria-labelledby="new-title">
    <form @submit.prevent="submit">
      <div class="dialog-heading">
        <h2 id="new-title">A new exploration</h2>
        <button type="button" class="icon-button" aria-label="Close dialog" @click="close">
          <AppIcon name="xmark" />
        </button>
      </div>
      <label for="session-title">Conversation name</label>
      <input
        id="session-title"
        ref="inputRef"
        v-model="sessionTitle"
        autofocus
        :maxlength="maxTitleLength"
        placeholder="e.g. A weekend in Amsterdam"
      >
      <p>Messages and model usage are saved to your private Cosmos DB partition.</p>
      <button class="primary-button" type="submit" :disabled="creating">
        Start exploring <AppIcon name="arrow-right" />
      </button>
    </form>
  </dialog>
</template>
