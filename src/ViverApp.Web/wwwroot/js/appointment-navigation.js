const prefix = "viver.agenda-context:";
// Per-tab scheduling context only: no tokens, contacts or clinical content.
export function read(path) {
    try {
        const stored = sessionStorage.getItem(prefix + path);
        // Reloads retain the native history entry; a fresh "Agendar" link must
        // not inherit the last booking merely because its path is the same.
        return history.state?.userState === stored ? stored : null;
    } catch { return null; }
}
export function write(path, state) {
    try { sessionStorage.setItem(prefix + path, state); return true; } catch { return false; }
}
export function clear() {
    try {
        for (const key of Object.keys(sessionStorage)) if (key.startsWith(prefix)) sessionStorage.removeItem(key);
    } catch { /* Storage may be disabled by the browser. */ }
}
export function bindOwner(id) {
    if (id === null || id === undefined) return;
    try {
        const key = prefix + "owner";
        if (sessionStorage.getItem(key) !== String(id)) {
            clear();
            sessionStorage.setItem(key, String(id));
        }
    } catch { /* Storage is optional; the URL fallback preserves navigation. */ }
}
