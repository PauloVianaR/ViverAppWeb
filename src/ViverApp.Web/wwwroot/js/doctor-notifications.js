let connection;
let audioContext;

function endpoint(baseUrl, path) {
    return new URL(path.replace(/^\//, ""), baseUrl).toString();
}

export async function connect(baseUrl, dotnet) {
    await import("/vendor/signalr/signalr.min.js");
    const response = await fetch(endpoint(baseUrl, "api/v1/doctor/notifications?page=1&pageSize=20"), {
        credentials: "include", headers: { "Accept": "application/json" }, cache: "no-store"
    });
    if (!response.ok) throw new Error("Não foi possível carregar as notificações.");
    const initial = await response.json();
    connection = new globalThis.signalR.HubConnectionBuilder()
        .withUrl(endpoint(baseUrl, "hubs/doctor-notifications"), { withCredentials: true })
        .withAutomaticReconnect([0, 2000, 5000, 15000])
        .configureLogging(globalThis.signalR.LogLevel.Warning)
        .build();
    connection.on("PatientArrived", message => dotnet.invokeMethodAsync("ReceiveArrival", message));
    try { await connection.start(); } catch { /* O feed durável continua disponível por HTTP. */ }
    const unlock = () => {
        audioContext ??= new (window.AudioContext || window.webkitAudioContext)();
        if (audioContext.state === "suspended") audioContext.resume();
        document.removeEventListener("pointerdown", unlock);
        document.removeEventListener("keydown", unlock);
    };
    document.addEventListener("pointerdown", unlock, { once: true });
    document.addEventListener("keydown", unlock, { once: true });
    return initial;
}

export async function play(volume) {
    if (!audioContext || audioContext.state !== "running") return false;
    const gain = audioContext.createGain();
    const oscillator = audioContext.createOscillator();
    gain.gain.setValueAtTime(Math.max(0, Math.min(1, volume / 100)) * 0.12, audioContext.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.001, audioContext.currentTime + 0.45);
    oscillator.frequency.setValueAtTime(660, audioContext.currentTime);
    oscillator.frequency.setValueAtTime(880, audioContext.currentTime + 0.16);
    oscillator.connect(gain); gain.connect(audioContext.destination);
    oscillator.start(); oscillator.stop(audioContext.currentTime + 0.46);
    return true;
}

export async function disconnect() {
    if (connection) { try { await connection.stop(); } catch { } connection = null; }
    if (audioContext) { try { await audioContext.close(); } catch { } audioContext = null; }
}
