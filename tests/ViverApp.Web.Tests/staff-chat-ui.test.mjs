import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const chatSource = await readFile(new URL("../../src/ViverApp.Web/wwwroot/js/staff-chat.js", import.meta.url), "utf8");
const scrollToLatest = new Function(chatSource.replace(/^import .*;\r?\n/m, "").replace(/export /g, "")
    + "\nreturn scrollToLatest;")();

test("chat scrolls only its own rendered message panel to the latest message", () => {
    const panel = { scrollTop: 0, scrollHeight: 2400 };
    scrollToLatest(panel);
    assert.equal(panel.scrollTop, 2400);
    panel.scrollHeight = 2600;
    scrollToLatest(panel);
    assert.equal(panel.scrollTop, 2600);
    scrollToLatest(null);
});

const toastSource = await readFile(new URL("../../src/ViverApp.Web/wwwroot/js/ui-toasts.js", import.meta.url), "utf8");
function toasts() {
    let open = false;
    const calls = [];
    const host = { matches: () => open, showPopover: () => { open = true; calls.push("show"); },
        hidePopover: () => { open = false; calls.push("hide"); } };
    const document = { getElementById: id => id === "ui-toast-host" ? host : null };
    const sync = new Function("document", toastSource.replace(/export /g, "") + "\nreturn sync;")(document);
    return { sync, calls };
}

test("toast rerender keeps the popover open so exit animation can finish", () => {
    const { sync, calls } = toasts();
    sync(true, "one");
    sync(true, "one");
    assert.deepEqual(calls, ["show"]);
    sync(false, null);
    assert.deepEqual(calls, ["show", "hide"]);
});

test("a new toast is raised above an existing dialog without reordering on dismissal", () => {
    const { sync, calls } = toasts();
    sync(true, "one");
    sync(true, "two");
    sync(true, "two");
    assert.deepEqual(calls, ["show", "hide", "show"]);
});
