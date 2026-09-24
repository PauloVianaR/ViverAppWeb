export * from './patient-experience.js';

export function confirmTyped(label) {
    return window.prompt(`Para confirmar ${label}, digite exatamente o nome exibido:`);
}

export function confirmPhrase(phrase) {
    return window.prompt(`Esta alteração afeta o acesso à plataforma. Para confirmar, digite ${phrase}:`);
}

export async function downloadAnalyticsExport(base, id) {
    try {
        const target = new URL(`api/v1/administrator/analytics-exports/${id}/download`, base);
        if (target.origin !== new URL(base).origin) return { ok: false, error: "Destino inválido." };
        const response = await fetch(target, { credentials: "include", cache: "no-store" });
        if (!response.ok) {
            let message = "Não foi possível baixar a exportação. Atualize a lista e tente novamente.";
            try {
                const problem = await response.json();
                if (typeof problem.title === "string" && problem.title.length < 200) message = problem.title;
            } catch { }
            return { ok: false, error: message };
        }
        const blob = await response.blob();
        const objectUrl = URL.createObjectURL(blob);
        const anchor = document.createElement("a");
        anchor.href = objectUrl;
        anchor.download = `viverapp-analytics-${id}.csv`;
        anchor.click();
        setTimeout(() => URL.revokeObjectURL(objectUrl), 10000);
        return { ok: true };
    } catch { return { ok: false, error: "Conexão indisponível. Tente novamente." }; }
}
