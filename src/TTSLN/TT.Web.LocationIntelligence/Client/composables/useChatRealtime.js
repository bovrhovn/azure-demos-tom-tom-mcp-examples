import { onScopeDispose, ref } from "vue";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";

export function useChatRealtime({
  getHubUrl = () => "/hubs/chat",
  onConversationChanged,
  onConversationDeleted,
  onReconnected,
  onError
}) {
  const connected = ref(false);
  let connection = null;
  let transitionPromise = null;

  function createConnection() {
    const hub = new HubConnectionBuilder()
      .withUrl(getHubUrl())
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    hub.on("ConversationChanged", (id, updatedAt) => {
      Promise.resolve(onConversationChanged?.(id, updatedAt)).catch((reason) => {
        onError?.(reason);
      });
    });
    hub.on("ConversationDeleted", (id) => {
      onConversationDeleted?.(id);
    });
    hub.onreconnecting(() => {
      connected.value = false;
    });
    hub.onclose(() => {
      connected.value = false;
    });
    hub.onreconnected(async () => {
      connected.value = true;
      try {
        await onReconnected?.();
      } catch (reason) {
        onError?.(reason);
      }
    });

    return hub;
  }

  function start() {
    if (transitionPromise) return transitionPromise;
    if (connection && connected.value) return Promise.resolve();

    transitionPromise = (async () => {
      if (connection) {
        await connection.stop().catch(() => {});
      }
      const hub = createConnection();
      connection = hub;
      await hub.start();
      if (connection === hub) {
        connected.value = true;
      }
    })().finally(() => {
      transitionPromise = null;
    });

    return transitionPromise;
  }

  function reconnect() {
    if (transitionPromise) return transitionPromise;

    transitionPromise = (async () => {
      connected.value = false;
      if (connection) {
        await connection.stop().catch(() => {});
        connection = null;
      }
      const hub = createConnection();
      connection = hub;
      await hub.start();
      if (connection === hub) {
        connected.value = true;
      }
      await onReconnected?.();
    })().catch((reason) => {
      onError?.(reason);
    }).finally(() => {
      transitionPromise = null;
    });

    return transitionPromise;
  }

  async function stop() {
    const active = connection;
    connection = null;
    connected.value = false;
    if (active) {
      await active.stop().catch(() => {});
    }
  }

  onScopeDispose(() => {
    stop();
  });

  return {
    connected,
    start,
    reconnect,
    stop
  };
}
