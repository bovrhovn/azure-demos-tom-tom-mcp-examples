<script setup>
import { computed, ref } from "vue";
import SessionActions from "../SessionActions.vue";
import { formatDate } from "../utils/dateFormatting.js";
import AppIcon from "./AppIcon.vue";
import SessionActionDialogs from "./SessionActionDialogs.vue";

const props = defineProps({
  sessions: { type: Array, required: true },
  selectedId: { type: String, default: null },
  continuationToken: { type: String, default: null },
  loading: { type: Boolean, default: false },
  runtimeReady: { type: Boolean, default: false },
  maxTitleLength: { type: Number, default: 120 },
  busy: { type: Boolean, default: false },
  onRename: { type: Function, required: true },
  onDelete: { type: Function, required: true }
});

const emit = defineEmits(["new-session", "select", "load-more"]);
const actionDialogs = ref(null);
const activeSessionId = ref(null);
const activeDialogBusy = computed(() => Boolean(activeSessionId.value && props.selectedId === activeSessionId.value && props.busy));

function handleOpenRename(session) {
  activeSessionId.value = session.id;
  actionDialogs.value?.openRename(session);
}

function handleOpenDelete(session) {
  activeSessionId.value = session.id;
  actionDialogs.value?.openDelete(session);
}
</script>

<template>
  <aside class="sidebar" aria-label="Chat sessions">
    <div class="sidebar-heading">
      <span>YOUR WORKSPACE</span>
      <AppIcon name="compass" />
    </div>
    <button class="new-session" :disabled="loading || !runtimeReady" @click="emit('new-session')">
      <AppIcon name="plus" /> New conversation
    </button>
    <div class="section-label">
      CONVERSATIONS <span>{{ sessions.length }}</span>
    </div>
    <nav class="sessions">
      <div
        v-for="session in sessions"
        :key="session.id"
        class="session-row"
        :class="{ active: selectedId === session.id }"
      >
        <button
          class="session"
          :aria-current="selectedId === session.id ? 'page' : undefined"
          @click="emit('select', session.id)"
        >
          <AppIcon name="comment" />
          <span>
            <strong>{{ session.title }}</strong>
            <small>{{ formatDate(session.updatedAt) }}</small>
          </span>
        </button>
        <SessionActions
          v-if="runtimeReady"
          :session="session"
          :disabled="selectedId === session.id && busy"
          @rename="handleOpenRename"
          @delete="handleOpenDelete"
        />
      </div>
      <p v-if="!sessions.length && !loading" class="sidebar-empty">A new perspective starts with a question.</p>
      <button v-if="continuationToken" class="text-button" @click="emit('load-more')">Load more</button>
    </nav>
    <div class="sidebar-note">
      <span class="note-icon"><AppIcon name="layer-group" /></span>
      <strong>Real-world context.<br>Smarter conversations.</strong>
      <p>Powered by TomTom Maps MCP<br>and Microsoft Foundry.</p>
      <span class="note-line"></span>
    </div>

    <SessionActionDialogs
      v-if="runtimeReady"
      ref="actionDialogs"
      :max-title-length="maxTitleLength"
      :disabled="activeDialogBusy"
      :on-rename="onRename"
      :on-delete="onDelete"
    />
  </aside>
</template>
