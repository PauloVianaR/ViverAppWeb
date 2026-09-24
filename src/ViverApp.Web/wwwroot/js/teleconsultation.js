import "/vendor/signalr/signalr.min.js";
import { request } from "./patient-experience.js";

let connection, local, heartbeat, qualityTimer, active = false;
let iceServers = [];
const peers = new Map();
const pendingCandidates = new Map();
function status(value) { const element = document.getElementById("call-status"); if (element) element.textContent = value; }
function quality(value) { const element = document.getElementById("call-quality"); if (element) element.textContent = value; }
function remoteContainer() { return document.getElementById("remote-videos"); }
function removePeer(id) {
    const peer = peers.get(id);
    if (peer) { peer.onconnectionstatechange = null; peer.close(); peers.delete(id); }
    pendingCandidates.delete(id);
    document.getElementById(`remote-${id}`)?.remove();
    if (!peers.size && active) status("Aguardando outros participantes…");
}
function createPeer(id) {
    const waiting = pendingCandidates.get(id);
    removePeer(id);
    if (waiting) pendingCandidates.set(id, waiting);
    const peer = new RTCPeerConnection({ iceServers });
    peers.set(id, peer);
    for (const track of local.getTracks()) peer.addTrack(track, local);
    peer.ontrack = event => {
        let figure = document.getElementById(`remote-${id}`);
        if (!figure) {
            figure = document.createElement("figure"); figure.id = `remote-${id}`;
            const video = document.createElement("video"); video.autoplay = true; video.playsInline = true;
            video.setAttribute("aria-label", "Vídeo de outro participante");
            const caption = document.createElement("figcaption"); caption.textContent = "Participante";
            figure.append(video, caption); remoteContainer()?.append(figure);
        }
        figure.querySelector("video").srcObject = event.streams[0];
    };
    peer.onicecandidate = event => {
        if (event.candidate && connection?.state === "Connected")
            connection.invoke("Signal", id, "candidate", JSON.stringify(event.candidate.toJSON()))
                .catch(() => status("Não foi possível negociar a conexão. Tente reconectar."));
    };
    peer.onconnectionstatechange = () => {
        if (!peers.has(id)) return;
        if (peer.connectionState === "connected") status("Conectado");
        else if (peer.connectionState === "failed") status("A rede não permitiu esta conexão. Tente reconectar.");
        else if (peer.connectionState === "disconnected") status("Conexão interrompida. Tentando restabelecer…");
    };
    return peer;
}
async function offer(id) {
    if (!active || !local) return;
    const peer = createPeer(id);
    await peer.setLocalDescription(await peer.createOffer());
    await connection.invoke("Signal", id, "offer", JSON.stringify(peer.localDescription));
}
async function signal(id, kind, payload) {
    if (!active || !local) return;
    try {
        const value = JSON.parse(payload);
        let peer = peers.get(id);
        if (kind === "offer") {
            peer = createPeer(id);
            await peer.setRemoteDescription(value);
            await peer.setLocalDescription(await peer.createAnswer());
            await connection.invoke("Signal", id, "answer", JSON.stringify(peer.localDescription));
        } else if (kind === "answer" && peer) await peer.setRemoteDescription(value);
        else if (kind === "candidate") {
            if (peer?.remoteDescription) await peer.addIceCandidate(value);
            else pendingCandidates.set(id, [...(pendingCandidates.get(id) || []), value]);
        }
        if (peer?.remoteDescription && pendingCandidates.has(id)) {
            for (const candidate of pendingCandidates.get(id)) await peer.addIceCandidate(candidate);
            pendingCandidates.delete(id);
        }
    } catch { status("Não foi possível estabelecer o vídeo. Tente reconectar."); }
}
async function refreshDevices() {
    if (!navigator.mediaDevices?.enumerateDevices) return;
    const devices = await navigator.mediaDevices.enumerateDevices();
    for (const [kind, selectId] of [["audioinput", "call-audio-device"], ["videoinput", "call-video-device"]]) {
        const select = document.getElementById(selectId);
        if (!select) continue;
        const previous = select.value;
        select.replaceChildren();
        for (const device of devices.filter(item => item.kind === kind)) {
            const option = document.createElement("option"); option.value = device.deviceId;
            option.textContent = device.label || `${kind === "audioinput" ? "Microfone" : "Câmera"} ${select.options.length + 1}`;
            select.append(option);
        }
        if (previous && [...select.options].some(option => option.value === previous)) select.value = previous;
        else {
            const current = local?.getTracks().find(track => track.kind === (kind === "audioinput" ? "audio" : "video"));
            const id = current?.getSettings().deviceId;
            if (id && [...select.options].some(option => option.value === id)) select.value = id;
        }
        select.onchange = () => changeDevice(kind === "audioinput" ? "audio" : "video", select.value);
    }
}
async function changeDevice(kind, deviceId) {
    if (!active || !local || !deviceId) return;
    try {
        const stream = await navigator.mediaDevices.getUserMedia({ [kind]: { deviceId: { exact: deviceId } } });
        const next = stream.getTracks()[0];
        const old = local.getTracks().find(track => track.kind === kind);
        next.enabled = old?.enabled ?? true;
        for (const peer of peers.values()) {
            const sender = peer.getSenders().find(item => item.track?.kind === kind);
            if (sender) await sender.replaceTrack(next); else peer.addTrack(next, local);
        }
        if (old) { local.removeTrack(old); old.stop(); }
        local.addTrack(next);
        document.getElementById("local-video").srcObject = local;
        status("Dispositivo alterado.");
    } catch { status("Não foi possível usar este dispositivo. Confira as permissões."); await refreshDevices(); }
}

export async function exchangeInvite(base) {
    const fragment = location.hash.slice(1);
    if (!fragment) return await request(base, "api/v1/teleconsultation/guest/current");
    const match = /^([0-9a-f]{32})\.([A-Za-z0-9_-]{43})$/.exec(fragment);
    if (!match) return { ok: false, error: "Este convite é inválido ou está incompleto." };
    const result = await request(base, "api/v1/teleconsultation/guest/exchange", "POST", { linkId: match[1], secret: match[2] });
    history.replaceState(null, "", location.pathname + location.search);
    return result;
}

export async function start(base, appointmentId, guest = false) {
    await stop(false);
    try {
        if (!navigator.mediaDevices?.getUserMedia) { status("Use um navegador atualizado e conexão segura para câmera e microfone."); return false; }
        const ice = await request(base, `api/v1/teleconsultation/appointments/${appointmentId}/ice`);
        if (!ice.ok) { status(ice.error || "Rede da videochamada indisponível."); return false; }
        iceServers = ice.data.iceServers;
        local = await navigator.mediaDevices.getUserMedia({ audio: true, video: { width: { ideal: 1280 }, height: { ideal: 720 } } });
        document.getElementById("local-video").srcObject = local;
        await refreshDevices();
        connection = new window.signalR.HubConnectionBuilder().withUrl(new URL("hubs/teleconsultation", base).toString(),
            { withCredentials: true, transport: window.signalR.HttpTransportType.WebSockets, skipNegotiation: true })
            .withAutomaticReconnect([0, 2000, 5000, 10000]).configureLogging(window.signalR.LogLevel.None).build();
        active = true;
        const join = async () => {
            const token = await request(base, "api/v1/auth/antiforgery");
            if (!token.ok) throw new Error();
            const state = await connection.invoke("Join", appointmentId, token.data.requestToken);
            status(state.peers.length ? "Conectando…" : "Aguardando outros participantes…");
        };
        connection.on("Signal", signal);
        connection.on("PeerJoined", id => offer(id).catch(() => status("Tente reconectar para iniciar o vídeo.")));
        connection.on("PeerLeft", id => removePeer(id));
        connection.on("RoomClosed", async () => { await stop(false); status("O profissional encerrou a reunião ou o convite foi revogado."); });
        connection.onreconnecting(() => { for (const id of [...peers.keys()]) removePeer(id); status("Reconectando…"); });
        connection.onreconnected(() => join().catch(async () => { await stop(false); status("Sua sala ou sessão expirou."); }));
        connection.onclose(async () => { if (active) { await stop(false); status("A chamada foi desconectada. Entre novamente."); } });
        await connection.start(); await join();
        heartbeat = setInterval(async () => { if (connection?.state === "Connected") try { await connection.invoke("Heartbeat"); } catch { await stop(false); status("A sala encerrou ou sua sessão expirou."); } }, 15000);
        qualityTimer = setInterval(async () => {
            if (!peers.size) return;
            try {
                let loss = 0;
                for (const peer of peers.values()) for (const report of (await peer.getStats()).values())
                    if (report.type === "inbound-rtp") loss = Math.max(loss, report.packetsLost / Math.max(1, report.packetsReceived + report.packetsLost));
                quality(loss > .05 ? "Qualidade: rede instável. Experimente desligar a câmera." : "Qualidade: conexão estável.");
            } catch { /* Estatísticas são apenas informativas. */ }
        }, 5000);
        window.addEventListener("pagehide", leavePage);
        return true;
    } catch (error) {
        await stop(false);
        status(error?.name === "NotAllowedError" ? "Permissão negada. Autorize câmera e microfone no navegador."
            : error?.name === "NotFoundError" ? "Câmera ou microfone não encontrados."
            : /limite de quatro/i.test(error?.message || "") ? "A sala está lotada (máximo de quatro pessoas)."
            : "Não foi possível entrar. Confira o convite, os dispositivos e o horário.");
        return false;
    }
}
function leavePage() { local?.getTracks().forEach(track => track.stop()); for (const id of [...peers.keys()]) removePeer(id); connection?.stop(); }
export function toggle(kind) {
    const tracks = local?.getTracks().filter(track => track.kind === kind) || [];
    const enabled = !tracks.some(track => track.enabled);
    for (const track of tracks) track.enabled = enabled;
    return enabled;
}
export async function stop(showMessage = true) {
    active = false; clearInterval(heartbeat); clearInterval(qualityTimer);
    for (const id of [...peers.keys()]) removePeer(id);
    local?.getTracks().forEach(track => track.stop()); local = null;
    const video = document.getElementById("local-video"); if (video) video.srcObject = null;
    const previous = connection; connection = null;
    if (previous) { try { if (previous.state === "Connected") await previous.invoke("Leave"); await previous.stop(); } catch { /* Desconexão já em andamento. */ } }
    window.removeEventListener("pagehide", leavePage);
    if (showMessage) status("Você saiu da chamada. Câmera e microfone foram desligados.");
}
