<script setup>
import { nextTick, ref } from "vue";

const props = defineProps({
  session: { type: Object, required: true },
  api: { type: Function, required: true },
  maxTitleLength: { type: Number, required: true },
  disabled: Boolean
});
const emit = defineEmits(["updated", "deleted"]);
const dialog = ref(null);
const titleInput = ref(null);
const cancelButton = ref(null);
const action = ref("edit");
const title = ref("");
const saving = ref(false);
const error = ref("");

async function open(mode) {
  action.value = mode;
  title.value = props.session.title;
  error.value = "";
  dialog.value.showModal();
  await nextTick();
  if (mode === "delete") cancelButton.value.focus();
  else { titleInput.value.focus(); titleInput.value.select(); }
}
async function submit() {
  if (saving.value || props.disabled || (action.value === "edit" && !title.value.trim())) return;
  saving.value = true;
  error.value = "";
  try {
    const path = `/api/sessions/${props.session.id}`;
    if (action.value === "delete") {
      await props.api(path, { method: "DELETE" });
      dialog.value?.close();
      emit("deleted", props.session.id);
    } else {
      const session = await props.api(path, { method: "PUT", body: { title: title.value.trim() } });
      dialog.value?.close();
      emit("updated", session);
    }
  } catch (reason) { error.value = reason.message; }
  finally { saving.value = false; }
}
</script>

<template>
  <div class="session-actions">
    <button class="icon-button" :aria-label="`Edit ${session.title}`" title="Edit conversation"
            :disabled="disabled" @click="open('edit')"><i class="fa-solid fa-pen" aria-hidden="true"></i></button>
    <button class="icon-button delete-button" :aria-label="`Delete ${session.title}`" title="Delete conversation"
            :disabled="disabled" @click="open('delete')"><i class="fa-regular fa-trash-can" aria-hidden="true"></i></button>
  </div>
  <Teleport to="body">
    <dialog ref="dialog" class="options-dialog" :aria-labelledby="`session-action-${session.id}`"
            :aria-describedby="action === 'delete' ? `delete-warning-${session.id}` : undefined"
            @cancel="saving && $event.preventDefault()">
      <form @submit.prevent="submit">
        <div class="dialog-heading">
          <h2 :id="`session-action-${session.id}`">{{ action === "delete" ? "Delete conversation?" : "Edit conversation" }}</h2>
          <button type="button" class="icon-button" aria-label="Close dialog" :disabled="saving" @click="dialog.close()">
            <i class="fa-solid fa-xmark" aria-hidden="true"></i>
          </button>
        </div>
        <template v-if="action === 'edit'">
          <label :for="`edit-title-${session.id}`">Conversation name</label>
          <input :id="`edit-title-${session.id}`" ref="titleInput" v-model="title" required
                 :maxlength="maxTitleLength" :disabled="saving">
        </template>
        <p v-else :id="`delete-warning-${session.id}`">
          Permanently delete <strong>{{ session.title }}</strong> and all its messages? This cannot be undone.
        </p>
        <p v-if="error" class="dialog-error" role="alert">{{ error }}</p>
        <div class="dialog-actions">
          <button ref="cancelButton" type="button" class="secondary-button" :disabled="saving" @click="dialog.close()">Cancel</button>
          <button type="submit" class="primary-button"
                  :disabled="saving || disabled || (action === 'edit' && !title.trim())">
            {{ saving ? "Saving..." : action === "delete" ? "Delete conversation" : "Save changes" }}
            <i class="fa-solid" :class="saving ? 'fa-circle-notch fa-spin' : action === 'delete' ? 'fa-trash-can' : 'fa-check'" aria-hidden="true"></i>
          </button>
        </div>
      </form>
    </dialog>
  </Teleport>
</template>
