import test from "node:test";
import assert from "node:assert/strict";
import { get } from "../../src/ViverApp.Web/wwwroot/js/booking-api-stream.js";

test("large patient responses return a stream handle instead of a SignalR JSON message", async () => {
    const data = { items: Array.from({ length: 100 }, (_, id) => ({ id, name: "PACIENTE SINTÉTICO ".repeat(30) })) };
    let blob;
    globalThis.DotNet = { createJSStreamReference(value) { blob = value; return { __jsObjectId: 1, __jsStreamReferenceLength: value.size }; } };
    globalThis.fetch = async (url, options) => {
        assert.equal(String(url), "https://api.example.test/api/v1/booking-workspace/patients?page=1&pageSize=100");
        assert.equal(options.credentials, "include");
        assert.equal(options.cache, "no-store");
        return new Response(JSON.stringify(data), { status: 200 });
    };
    const handle = await get("https://api.example.test/", "api/v1/booking-workspace/patients?page=1&pageSize=100");
    assert.ok(blob.size > 32768);
    assert.ok(JSON.stringify(handle).length < 100);
    assert.deepEqual(JSON.parse(await blob.text()), { ok: true, data, status: 200 });
});

test("authentication failure remains an error and never becomes an empty successful list", async () => {
    let blob;
    globalThis.DotNet = { createJSStreamReference(value) { blob = value; return { __jsObjectId: 1, __jsStreamReferenceLength: value.size }; } };
    globalThis.fetch = async () => new Response("{}", { status: 401 });
    await get("https://api.example.test/", "api/v1/booking-workspace/patients");
    const response = JSON.parse(await blob.text());
    assert.equal(response.ok, false);
    assert.equal(response.status, 401);
    assert.match(response.error, /sessão expirou/);
});
