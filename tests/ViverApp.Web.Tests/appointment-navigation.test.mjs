import test from "node:test";
import assert from "node:assert/strict";
import { read, write, clear, bindOwner } from "../../src/ViverApp.Web/wwwroot/js/appointment-navigation.js";

class MemoryStorage {
    getItem(key) { return this[key] ?? null; }
    setItem(key, value) { this[key] = value; }
    removeItem(key) { delete this[key]; }
}
const path = "/gestao/agendar";
const state = JSON.stringify({ Root: "/gestao", View: "day", Date: "2026-10-19", ProfessionalId: 42, Time: "09:10" });
function reset() {
    globalThis.sessionStorage = new MemoryStorage();
    globalThis.history = { state: { userState: state } };
}
test("reload restores this entry, not a previous booking on a fresh link", () => {
    reset();
    assert.equal(write(path, state), true);
    assert.equal(read(path), state);
    history.state = null;
    assert.equal(read(path), null);
    history.state = { userState: "unrelated" };
    assert.equal(read(path), null);
    history.state = { userState: state };
    assert.equal(read("/medico/agendar"), null);
    assert.equal(read(path), state);
});
test("account changes and logout clear scheduling context", () => {
    reset();
    bindOwner("first");
    write(path, state);
    bindOwner("first");
    assert.equal(read(path), state);
    bindOwner("second");
    assert.equal(read(path), null);
    write(path, state);
    clear();
    assert.equal(read(path), null);
});
test("disabled storage requests the URL fallback without throwing", () => {
    globalThis.sessionStorage = {
        getItem() { throw new Error("blocked"); },
        setItem() { throw new Error("blocked"); },
    };
    assert.equal(write(path, state), false);
    assert.equal(read(path), null);
    assert.doesNotThrow(() => bindOwner("first"));
});
