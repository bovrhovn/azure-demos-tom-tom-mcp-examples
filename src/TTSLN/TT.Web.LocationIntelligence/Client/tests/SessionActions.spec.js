import { describe, expect, it, vi } from "vitest";
import { DOMWrapper, flushPromises, mount } from "@vue/test-utils";
import SessionActions from "../SessionActions.vue";

const session = { id: "session-1", title: "Amsterdam" };
const modal = () => new DOMWrapper(document.querySelector("dialog[open]"));
const render = (api = vi.fn(), disabled = false) => mount(SessionActions, {
  attachTo: document.body, props: { session, api, disabled, maxTitleLength: 120 }
});

describe("session actions", () => {
  it("edits via a prefilled modal and emits the persisted session", async () => {
    const updated = { ...session, title: "New route", messages: [] };
    const api = vi.fn().mockResolvedValue(updated);
    const wrapper = render(api);
    await wrapper.get('[aria-label="Edit Amsterdam"]').trigger("click");
    expect(modal().get("input").element.value).toBe("Amsterdam");
    expect(document.activeElement).toBe(modal().get("input").element);
    await modal().get("input").setValue("  New route  ");
    await modal().get("form").trigger("submit");
    await flushPromises();
    expect(api).toHaveBeenCalledWith("/api/sessions/session-1", { method: "PUT", body: { title: "New route" } });
    expect(wrapper.emitted("updated")).toEqual([[updated]]);
    expect(document.querySelector("dialog").open).toBe(false);
  });

  it("requires explicit deletion confirmation and focuses Cancel", async () => {
    const api = vi.fn().mockResolvedValue(undefined);
    const wrapper = render(api);
    await wrapper.get('[aria-label="Delete Amsterdam"]').trigger("click");
    expect(modal().text()).toContain("This cannot be undone.");
    expect(modal().text()).toContain("Amsterdam");
    expect(document.activeElement.textContent).toBe("Cancel");
    expect(api).not.toHaveBeenCalled();
    await modal().get(".secondary-button").trigger("click");
    expect(api).not.toHaveBeenCalled();
    await wrapper.get('[aria-label="Delete Amsterdam"]').trigger("click");
    await modal().get("form").trigger("submit");
    await flushPromises();
    expect(api).toHaveBeenCalledExactlyOnceWith("/api/sessions/session-1", { method: "DELETE" });
    expect(wrapper.emitted("deleted")).toEqual([["session-1"]]);
  });

  it("keeps the dialog open with an explicit error when deletion fails", async () => {
    const wrapper = render(vi.fn().mockRejectedValue(new Error("Conversation changed.")));
    await wrapper.get('[aria-label="Delete Amsterdam"]').trigger("click");
    await modal().get("form").trigger("submit");
    await flushPromises();
    expect(modal().get('[role="alert"]').text()).toBe("Conversation changed.");
    expect(wrapper.emitted("deleted")).toBeUndefined();
  });

  it("prevents duplicate submissions while waiting for persistence", async () => {
    let resolve;
    const api = vi.fn().mockReturnValue(new Promise((done) => { resolve = done; }));
    const wrapper = render(api);
    await wrapper.get('[aria-label="Delete Amsterdam"]').trigger("click");
    await modal().get("form").trigger("submit");
    await modal().get("form").trigger("submit");
    expect(api).toHaveBeenCalledTimes(1);
    expect(modal().get(".secondary-button").element.disabled).toBe(true);
    resolve();
    await flushPromises();
  });

  it("disables actions while a reply is in progress and rejects whitespace titles", async () => {
    const api = vi.fn();
    const wrapper = render(api, true);
    expect(wrapper.findAll("button").every((button) => button.element.disabled)).toBe(true);
    await wrapper.setProps({ disabled: false });
    await wrapper.get('[aria-label="Edit Amsterdam"]').trigger("click");
    await modal().get("input").setValue("   ");
    expect(modal().get('[type="submit"]').element.disabled).toBe(true);
    expect(api).not.toHaveBeenCalled();
  });
});
