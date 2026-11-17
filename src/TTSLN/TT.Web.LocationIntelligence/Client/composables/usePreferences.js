import { ref } from "vue";

function readStorage(key, fallback) {
  try {
    const value = globalThis.localStorage?.getItem(key);
    return value ?? fallback;
  } catch {
    return fallback;
  }
}

function writeStorage(key, value) {
  try {
    globalThis.localStorage?.setItem(key, value);
  } catch {
    // Ignore storage quota or private-browsing SecurityError restrictions.
  }
}

export function usePreferences() {
  const compact = ref(readStorage("tt.compact", "false") === "true");
  const showUsage = ref(readStorage("tt.usage", "true") !== "false");

  function savePreferences() {
    writeStorage("tt.compact", String(compact.value));
    writeStorage("tt.usage", String(showUsage.value));
  }

  return {
    compact,
    showUsage,
    savePreferences
  };
}
