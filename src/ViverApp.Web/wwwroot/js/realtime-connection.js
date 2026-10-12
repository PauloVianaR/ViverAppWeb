// Cada componente tem seu identificador; descartar o antigo não fecha o novo.
const connections = new Map();

export async function openRealtime(baseUrl, path, dotnet, events, refreshMethod) {
    await import("/vendor/signalr/signalr.min.js");
    const id = crypto.randomUUID();
    const connection = new globalThis.signalR.HubConnectionBuilder()
        .withUrl(new URL(path, baseUrl).toString(), { withCredentials: true })
        .withAutomaticReconnect([0, 2000, 5000, 15000])
        .configureLogging(globalThis.signalR.LogLevel.Warning).build();
    const state = { connection, stopped: false, timer: null, attempts: 0 };
    connections.set(id, state);
    const invoke = (method, ...args) => dotnet.invokeMethodAsync(method, ...args)
        .catch(() => console.warn("Não foi possível atualizar o componente em tempo real.", path));
    for (const [event, method] of Object.entries(events))
        connection.on(event, (...args) => { void invoke(method, ...args); });
    const schedule = () => {
        if (state.stopped || state.timer) return;
        state.timer = setTimeout(() => { state.timer = null; void start(); }, Math.min(30000, 3000 * ++state.attempts));
    };
    const start = async () => {
        if (state.stopped || connection.state !== globalThis.signalR.HubConnectionState.Disconnected) return;
        try {
            await connection.start();
            if (state.stopped) { await connection.stop(); return; }
            state.attempts = 0;
            await invoke(refreshMethod);
        } catch (error) {
            console.warn("Conexão em tempo real indisponível; será tentada novamente.", path, error);
            schedule();
        }
    };
    connection.onreconnected(() => invoke(refreshMethod));
    connection.onclose(schedule);
    void start();
    return id;
}

export async function closeRealtime(id) {
    const state = connections.get(id);
    if (!state) return;
    state.stopped = true;
    clearTimeout(state.timer);
    connections.delete(id);
    try { await state.connection.stop(); } catch { }
}
