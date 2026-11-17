<script setup>
import { nextTick, ref } from "vue";
import AppIcon from "./AppIcon.vue";

const props = defineProps({
  maxTitleLength: { type: Number, default: 120 },
  disabled: { type: Boolean, default: false },
  onRename: { type: Function, required: true },
  onDelete: { type: Function, required: true }
});

const emit = defineEmits(["renamed", "deleted"]);

const renameDialog = ref(null);
const deleteDialog = ref(null);
const titleInput = ref(null);
const cancelDeleteButton = ref(null);

const renameTarget = ref(null);
const deleteTarget = ref(null);
const title = ref("");
const savingRename = ref(false);
const savingDelete = ref(false);
const renameError = ref("");
const deleteError = ref("");

async function openRename(session) {
  renameTarget.value = session;
  title.value = session.title;
  renameError.value = "";
  renameDialog.value?.showModal();
  await nextTick();
  titleInput.value?.focus();
  titleInput.value?.select();
}

async function openDelete(session) {
  deleteTarget.value = session;
  deleteError.value = "";
  deleteDialog.value?.showModal();
  await nextTick();
  cancelDeleteButton.value?.focus();
}

async function submitRename() {
  const trimmed = title.value.trim();
  if (savingRename.value || props.disabled || !renameTarget.value || !trimmed) return;

  savingRename.value = true;
  renameError.value = "";
  try {
    const updated = await props.onRename(renameTarget.value.id, trimmed);
    renameDialog.value?.close();
    emit("renamed", updated);
  } catch (reason) {
    renameError.value = reason.message;
  } finally {
    savingRename.value = false;
  }
}

async function submitDelete() {
  if (savingDelete.value || props.disabled || !deleteTarget.value) return;

  savingDelete.value = true;
  deleteError.value = "";
  try {
    const id = deleteTarget.value.id;
    await props.onDelete(id);
    deleteDialog.value?.close();
    emit("deleted", id);
  } catch (reason) {
    deleteError.value = reason.message;
  } finally {
    savingDelete.value = false;
  }
}

defineExpose({
  openRename,
  openDelete
});
</script>

<template>
  <Teleport to="body">
    <dialog
      ref="renameDialog"
      class="options-dialog"
      aria-labelledby="session-rename-heading"
      @cancel="savingRename && $event.preventDefault()"
    >
      <form @submit.prevent="submitRename">
        <div class="dialog-heading">
          <h2 id="session-rename-heading">Edit conversation</h2>
          <button
            type="button"
            class="icon-button"
            aria-label="Close dialog"
            :disabled="savingRename"
            @click="renameDialog.close()"
          >
            <AppIcon name="xmark" />
          </button>
        </div>
        <label for="session-rename-input">Conversation name</label>
        <input
          id="session-rename-input"
          ref="titleInput"
          v-model="title"
          required
          :maxlength="maxTitleLength"
          :disabled="savingRename"
        >
        <p v-if="renameError" class="dialog-error" role="alert">{{ renameError }}</p>
        <div class="dialog-actions">
          <button
            type="button"
            class="secondary-button"
            :disabled="savingRename"
            @click="renameDialog.close()"
          >Cancel</button>
          <button
            type="submit"
            class="primary-button"
            :disabled="savingRename || disabled || !title.trim()"
          >
            {{ savingRename ? "Saving..." : "Save changes" }}
            <AppIcon :name="savingRename ? 'spinner' : 'check'" />
          </button>
        </div>
      </form>
    </dialog>

    <dialog
      ref="deleteDialog"
      class="options-dialog"
      aria-labelledby="session-delete-heading"
      aria-describedby="session-delete-warning"
      @cancel="savingDelete && $event.preventDefault()"
    >
      <form @submit.prevent="submitDelete">
        <div class="dialog-heading">
          <h2 id="session-delete-heading">Delete conversation?</h2>
          <button
            type="button"
            class="icon-button"
            aria-label="Close dialog"
            :disabled="savingDelete"
            @click="deleteDialog.close()"
          >
            <AppIcon name="xmark" />
          </button>
        </div>
        <p id="session-delete-warning">
          Permanently delete <strong>{{ deleteTarget?.title }}</strong> and all its messages? This cannot be undone.
        </p>
        <p v-if="deleteError" class="dialog-error" role="alert">{{ deleteError }}</p>
        <div class="dialog-actions">
          <button
            ref="cancelDeleteButton"
            type="button"
            class="secondary-button"
            :disabled="savingDelete"
            @click="deleteDialog.close()"
          >Cancel</button>
          <button
            type="submit"
            class="primary-button"
            :disabled="savingDelete || disabled"
          >
            {{ savingDelete ? "Saving..." : "Delete conversation" }}
            <AppIcon :name="savingDelete ? 'spinner' : 'trash-can'" />
          </button>
        </div>
      </form>
    </dialog>
  </Teleport>
</template>
