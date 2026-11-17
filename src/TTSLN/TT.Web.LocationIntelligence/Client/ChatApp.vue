<script setup>
import { nextTick, onMounted, ref } from "vue";
import { createApi } from "./api.js";
import AppIcon from "./components/AppIcon.vue";
import MessageComposer from "./components/MessageComposer.vue";
import MessageThread from "./components/MessageThread.vue";
import NewSessionDialog from "./components/NewSessionDialog.vue";
import SessionSidebar from "./components/SessionSidebar.vue";
import WorkspaceHeader from "./components/WorkspaceHeader.vue";
import WorkspaceOptionsDialog from "./components/WorkspaceOptionsDialog.vue";
import WorkspaceStatusBar from "./components/WorkspaceStatusBar.vue";
import { useChatRealtime } from "./composables/useChatRealtime.js";
import { useChatWorkspace } from "./composables/useChatWorkspace.js";
import { usePreferences } from "./composables/usePreferences.js";
import { useRuntimeConfig } from "./composables/useRuntimeConfig.js";
import { createRuntimeApi } from "./services/runtimeApi.js";
import { createSessionsApi } from "./services/sessionsApi.js";

const runtimeConfig = useRuntimeConfig();
const { runtime, agentConfigured, maxMessageLength, maxTitleLength, maxTurns, endpoints } = runtimeConfig;
const { compact, showUsage, savePreferences } = usePreferences();

let runtimeApi;
const http = createApi(() => runtimeConfig.csrfToken.value, {
  getHeaderName: () => runtimeConfig.antiforgeryHeaderName.value,
  onCsrfInvalid: () => runtimeConfig.refreshRuntime(runtimeApi)
});
runtimeApi = createRuntimeApi(http, () => endpoints.value.runtime);
const sessionsApi = createSessionsApi(http, () => endpoints.value.sessions);

const threadRef = ref(null);
const composerRef = ref(null);
const newDialogRef = ref(null);
const optionsDialogRef = ref(null);

const workspace = useChatWorkspace({
  runtimeConfig,
  runtimeApi,
  sessionsApi,
  onScrollRequest: (options) => threadRef.value?.scrollToBottom(options)
});

const {
  sessions,
  selected,
  continuationToken,
  draft,
  error,
  loading,
  databaseConnected,
  busy,
  turnLimitReached
} = workspace;

const realtime = useChatRealtime({
  getHubUrl: () => endpoints.value.chatHub,
  onConversationChanged: (id, updatedAt) => workspace.refreshSession(id, { knownUpdatedAt: updatedAt }),
  onConversationDeleted: (id) => workspace.removeSession(id),
  onReconnected: () => workspace.reconcileRealtime(),
  onError: (reason) => { error.value = reason.message; }
});
const { connected } = realtime;

async function handleCreateSession(title = "") {
  const created = await workspace.createSession(title);
  if (created) {
    await nextTick();
    composerRef.value?.focus();
  }
  return created;
}

async function usePrompt(text) {
  if (!selected.value && !await handleCreateSession()) return;
  draft.value = text;
  await nextTick();
  composerRef.value?.focus();
}

async function loadMoreSessions() {
  try {
    await workspace.loadSessions(true);
  } catch (reason) {
    error.value = reason.message;
  }
}

onMounted(() => {
  workspace.initialize(realtime);
});
</script>

<template>
  <div class="workspace" :class="{ compact }">
    <WorkspaceHeader @open-options="optionsDialogRef?.open()" />

    <SessionSidebar
      :sessions="sessions"
      :selected-id="selected?.id ?? null"
      :continuation-token="continuationToken"
      :loading="loading"
      :runtime-ready="Boolean(runtime)"
      :max-title-length="maxTitleLength"
      :busy="busy"
      :on-rename="workspace.renameSession"
      :on-delete="workspace.deleteSession"
      @new-session="newDialogRef?.open()"
      @select="workspace.selectSession"
      @load-more="loadMoreSessions"
    />

    <main class="chat-panel">
      <div class="conversation-header">
        <div>
          <span class="eyebrow">EXPLORE WITH CONFIDENCE</span>
          <h1>{{ selected?.title || "Your world. Connected." }}</h1>
        </div>
        <button
          type="button"
          class="connection-badge"
          :class="{ offline: !connected }"
          :aria-label="connected ? 'Realtime connected. Reconnect.' : 'Realtime disconnected. Retry connection.'"
          @click="realtime.reconnect()"
        >
          <span class="status-dot"></span>{{ connected ? "Live" : "Reconnect" }}
        </button>
      </div>

      <div v-if="error" class="error-banner" role="alert">
        <AppIcon name="circle-exclamation" /><span>{{ error }}</span>
        <button type="button" class="icon-button" aria-label="Dismiss error" @click="error = ''">
          <AppIcon name="xmark" />
        </button>
      </div>
      <div v-if="runtime && !agentConfigured" class="setup-banner">
        <AppIcon name="wrench" />
        Configure Foundry and the TomTom key on the server to enable agent replies. Conversations are saved to Cosmos DB.
      </div>

      <MessageThread
        ref="threadRef"
        :loading="loading"
        :selected="selected"
        :busy="busy"
        :show-usage="showUsage"
        @select-prompt="usePrompt"
      />

      <MessageComposer
        ref="composerRef"
        v-model="draft"
        :loading="loading"
        :has-session="Boolean(selected)"
        :agent-configured="!runtime || agentConfigured"
        :busy="busy"
        :turn-limit-reached="turnLimitReached"
        :max-message-length="maxMessageLength"
        :max-turns="maxTurns"
        @send="workspace.send()"
      />
    </main>

    <WorkspaceStatusBar :database-connected="databaseConnected" :runtime="runtime" />

    <NewSessionDialog
      ref="newDialogRef"
      :max-title-length="maxTitleLength"
      :on-create="handleCreateSession"
    />

    <WorkspaceOptionsDialog
      ref="optionsDialogRef"
      v-model:show-usage="showUsage"
      v-model:compact="compact"
      :runtime="runtime"
      :database-connected="databaseConnected"
      @save="savePreferences"
    />
  </div>
</template>
