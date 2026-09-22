const key = "viverappweb.cookie-preferences";
const version = "2026-09-21";

export function read() {
    try {
        const value = JSON.parse(localStorage.getItem(key) ?? "null");
        if (!value || value.version !== version || value.necessary !== true || typeof value.preferences !== "boolean") return null;
        return { preferences: value.preferences };
    } catch {
        return null;
    }
}

export function save(preferences, source) {
    if (typeof preferences !== "boolean" || !["accept_all", "reject_optional", "customize"].includes(source)) throw new Error("Escolha de privacidade inválida.");
    const value = {
        version,
        necessary: true,
        preferences,
        analytics: false,
        marketing: false,
        savedAtUtc: new Date().toISOString(),
        source
    };
    localStorage.setItem(key, JSON.stringify(value));
    window.dispatchEvent(new CustomEvent("viver:cookie-preferences", { detail: { preferences } }));
}
