import { computed, onScopeDispose, ref, watch } from "vue";

export function useLeaseClock({ selected, sending, maxTurns }) {
  const clock = ref(Date.now());
  let leaseTimer = null;

  function clearLeaseTimer() {
    if (leaseTimer !== null) {
      clearTimeout(leaseTimer);
      leaseTimer = null;
    }
  }

  function scheduleLeaseCheck() {
    clearLeaseTimer();
    const session = selected.value;
    const hasPending = Boolean(session?.messages?.some((message) => message.status === "pending"));
    const expiryMs = session?.busyUntil ? Date.parse(session.busyUntil) : Number.NaN;
    const now = Date.now();
    clock.value = now;

    if (hasPending && Number.isFinite(expiryMs) && expiryMs > now) {
      const delay = Math.min(Math.max(25, expiryMs - now + 20), 2_147_483_647);
      leaseTimer = setTimeout(() => {
        leaseTimer = null;
        clock.value = Date.now();
      }, delay);
    }
  }

  watch(
    () => [
      selected.value?.id ?? null,
      selected.value?.busyUntil ?? null,
      Boolean(selected.value?.messages?.some((message) => message.status === "pending"))
    ],
    scheduleLeaseCheck,
    { immediate: true }
  );

  onScopeDispose(clearLeaseTimer);

  const leaseActive = computed(() => {
    const session = selected.value;
    if (!session?.busyUntil || !session.messages?.some((message) => message.status === "pending")) {
      return false;
    }
    const expiryMs = Date.parse(session.busyUntil);
    return Number.isFinite(expiryMs) && expiryMs > clock.value;
  });

  const busy = computed(() => Boolean(sending?.value) || leaseActive.value);

  const turnLimitReached = computed(() => {
    const messages = selected.value?.messages;
    const limit = maxTurns?.value ?? 40;
    if (!messages?.length || !(limit > 0)) return false;
    return messages.length >= limit * 2;
  });

  return {
    clock,
    leaseActive,
    busy,
    turnLimitReached
  };
}
