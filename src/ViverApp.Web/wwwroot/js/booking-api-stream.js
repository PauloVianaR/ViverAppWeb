import { request } from "./patient-experience.js";

// Return only the stream reference through SignalR. Blazor transfers the JSON
// in bounded chunks instead of closing the circuit on a large patient page.
export async function get(base, path) {
    const response = await request(base, path);
    return DotNet.createJSStreamReference(new Blob([JSON.stringify(response)], { type: "application/json" }));
}
