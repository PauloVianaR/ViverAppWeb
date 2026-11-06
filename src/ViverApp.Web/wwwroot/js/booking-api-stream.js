import { request } from "./patient-experience.js";

// InvokeAsync<IJSStreamReference> wraps this Blob in a stream reference itself.
// Returning an already wrapped reference would fail the framework's Blob check.
export async function get(base, path) {
    const response = await request(base, path);
    return new Blob([JSON.stringify(response)], { type: "application/json" });
}
