import { test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { randomUUID } from "node:crypto";
import { setImmediate } from "node:timers/promises";

const source = (await readFile(new URL("../../src/ViverApp.Web/wwwroot/js/realtime-connection.js", import.meta.url), "utf8"))
    .replace('await import("/vendor/signalr/signalr.min.js");', "await Promise.resolve();")
    .replaceAll("export async function", "async function");

function harness(failFirstStart = false) {
    const sockets = [], timers = [], callbacks = [];
    class Socket {
        state = "Disconnected"; events = {}; starts = 0; stops = 0;
        on(name, callback) { this.events[name] = callback; }
        onclose(callback) { this.closed = callback; }
        onreconnected(callback) { this.reconnected = callback; }
        async start() {
            this.starts++;
            if (failFirstStart && this.starts === 1) throw new Error("simulated outage");
            this.state = "Connected";
        }
        async stop() { this.stops++; this.state = "Disconnected"; this.closed(); }
    }
    class Builder {
        withUrl() { return this; } withAutomaticReconnect() { return this; } configureLogging() { return this; }
        build() { const socket = new Socket(); sockets.push(socket); return socket; }
    }
    const signalR = { HubConnectionBuilder: Builder, HubConnectionState: { Disconnected: "Disconnected" }, LogLevel: { Warning: 3 } };
    const api = new Function("globalThis", "crypto", "setTimeout", "clearTimeout", "console",
        source + "\nreturn {openRealtime,closeRealtime};")(
        { signalR }, { randomUUID }, callback => { timers.push(callback); return timers.length; }, () => {}, { warn() {} });
    const dotnet = { invokeMethodAsync: async (method, ...args) => { callbacks.push([method, ...args]); } };
    const open = () => api.openRealtime("https://api.example.test/", "hubs/staff-chat", dotnet, { StaffMessage: "OnStaffMessage" }, "RefreshStaffFeed");
    return { ...api, open, sockets, timers, callbacks };
}

test("initial connection failure retries and refreshes the durable feed", async () => {
    const h = harness(true);
    const id = await h.open(); await setImmediate();
    assert.equal(h.sockets[0].starts, 1);
    assert.equal(h.timers.length, 1);
    h.timers[0](); await setImmediate();
    assert.equal(h.sockets[0].starts, 2);
    assert.deepEqual(h.callbacks, [["RefreshStaffFeed"]]);
    await h.closeRealtime(id);
    assert.equal(h.timers.length, 1, "disposing must not schedule another connection");
});

test("disposing an old widget cannot close the newly mounted widget", async () => {
    const h = harness();
    const oldId = await h.open(), currentId = await h.open(); await setImmediate();
    await h.closeRealtime(oldId);
    assert.equal(h.sockets[0].stops, 1);
    assert.equal(h.sockets[1].stops, 0);
    assert.equal(h.sockets[1].state, "Connected");
    await h.closeRealtime(currentId);
});

test("push callbacks return no SignalR result and refresh after reconnect", async () => {
    const h = harness();
    const id = await h.open(); await setImmediate();
    const payload = { id: 1, senderId: 2, recipientId: 3 };
    assert.equal(h.sockets[0].events.StaffMessage(payload), undefined);
    await setImmediate();
    assert.deepEqual(h.callbacks[1], ["OnStaffMessage", payload]);
    h.sockets[0].reconnected(); await setImmediate();
    assert.deepEqual(h.callbacks[2], ["RefreshStaffFeed"]);
    await h.closeRealtime(id);
});
