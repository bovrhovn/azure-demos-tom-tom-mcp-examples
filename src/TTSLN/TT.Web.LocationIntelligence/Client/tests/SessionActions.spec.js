import { describe, expect, it, vi } from "vitest";
import { DOMWrapper, flushPromises, mount } from "@vue/test-utils";
import SessionSidebar from "../components/SessionSidebar.vue";

const session = { id: "session-1", title: "Amsterdam", updatedAt: "2026-01-01T10:00:00Z" };
const modal = () => new DOMWrapper(document.querySelector("dialog[open]"));

function render({
  sessions = [session],
  selectedId = "session-1",
  busy = false,
  onRename = vi.fn(),
  onDelete = vi.fn()
} = {}) {
  return mount(SessionSidebar, {
    attachTo: document.body,
    props: {
      sessions,
      selectedId,
      continuationToken: null,
      loading: false,
      runtimeReady: true,
      maxTitleLength: 120,
      busy,
      onRename,
      onDelete
    }
  });
}

describe("session actions and shared sidebar dialogs", () => {
  it("mounts a single shared rename and delete dialog pair regardless of session count", () => {
    render({
      sessions: [
        session,
        { id: "session-2", title: "Rotterdam", updatedAt: "2026-01-01T11:00:00Z" },
        { id: "session-3", title: "Utrecht", updatedAt: "2026-01-01T12:00:00Z" }
      ]
    });
    expect(document.querySelectorAll("dialog")).toHaveLength(2);
  });

  it("edits via a prefilled modal and passes the trimmed title", async () => {
    const updated = { ...session, title: "New route", messages: [] };
    const onRename = vi.fn().mockResolvedValue(updated);
    const wrapper = render({ onRename });
    await wrapper.get('[aria-label="Edit Amsterdam"]').trigger("click");
    expect(modal().get("input").element.value).toBe("Amsterdam");
    expect(document.activeElement).toBe(modal().get("input").element);
    await modal().get("input").setValue("  New route  ");
    await modal().get("form").trigger("submit");
    await flushPromises();
    expect(onRename).toHaveBeenCalledExactlyOnceWith("session-1", "New route");
    expect(document.querySelector("dialog[open]")).toBeNull();
  });

  it("requires explicit deletion confirmation and focuses Cancel", async () => {
    const onDelete = vi.fn().mockResolvedValue(undefined);
    const wrapper = render({ onDelete });
    await wrapper.get('[aria-label="Delete Amsterdam"]').trigger("click");
    expect(modal().text()).toContain("This cannot be undone.");
    expect(modal().text()).toContain("Amsterdam");
    expect(document.activeElement.textContent).toBe("Cancel");
    expect(onDelete).not.toHaveBeenCalled();
    await modal().get(".secondary-button").trigger("click");
    expect(onDelete).not.toHaveBeenCalled();
    await wrapper.get('[aria-label="Delete Amsterdam"]').trigger("click");
    await modal().get("form").trigger("submit");
    await flushPromises();
    expect(onDelete).toHaveBeenCalledExactlyOnceWith("session-1");
    expect(document.querySelector("dialog[open]")).toBeNull();
  });

  it("keeps the dialog open with an explicit error when deletion fails", async () => {
    const onDelete = vi.fn().mockRejectedValue(new Error("Conversation changed."));
    const wrapper = render({ onDelete });
    await wrapper.get('[aria-label="Delete Amsterdam"]').trigger("click");
    await modal().get("form").trigger("submit");
    await flushPromises();
    expect(modal().get('[role="alert"]').text()).toBe("Conversation changed.");
  });

  it("prevents duplicate submissions while waiting for persistence", async () => {
    let resolve;
    const onDelete = vi.fn().mockReturnValue(new Promise((done) => { resolve = done; }));
    const wrapper = render({ onDelete });
    await wrapper.get('[aria-label="Delete Amsterdam"]').trigger("click");
    await modal().get("form").trigger("submit");
    await modal().get("form").trigger("submit");
    expect(onDelete).toHaveBeenCalledTimes(1);
    expect(modal().get(".secondary-button").element.disabled).toBe(true);
    resolve();
    await flushPromises();
  });

  it("disables actions while a reply is in progress and rejects whitespace titles", async () => {
    const onRename = vi.fn();
    const wrapper = render({ onRename, busy: true });
    expect(wrapper.findAll(".session-actions button").every((button) => button.element.disabled)).toBe(true);
    await wrapper.setProps({ busy: false });
    await wrapper.get('[aria-label="Edit Amsterdam"]').trigger("click");
    await modal().get("input").setValue("   ");
    expect(modal().get('[type="submit"]').element.disabled).toBe(true);
    expect(onRename).not.toHaveBeenCalled();
  });
});
