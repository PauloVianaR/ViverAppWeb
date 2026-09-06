import "/vendor/signalr/signalr.min.js";
import { request } from "./patient-experience.js";
let connection, local, peer, heartbeat, qualityTimer, initiator, candidates = [], active = false;
function status(text) { const e = document.getElementById("call-status"); if (e) e.textContent = text; }
function closePeer() { if (peer) { peer.onconnectionstatechange = null; peer.close(); peer = null; } candidates = []; const remote = document.getElementById("remote-video"); if (remote) remote.srcObject = null; }
async function makePeer() {
    const pending = candidates; closePeer(); candidates = pending; peer = new RTCPeerConnection({ iceServers: [] });
    for (const track of local.getTracks()) peer.addTrack(track, local);
    peer.ontrack = event => { const video = document.getElementById("remote-video"); if (video) video.srcObject = event.streams[0]; };
    peer.onicecandidate = event => { if (event.candidate && connection?.state === "Connected") connection.invoke("Signal", "candidate", JSON.stringify(event.candidate.toJSON())).catch(() => status("Não foi possível negociar a conexão. Tente reconectar.")); };
    peer.onconnectionstatechange = () => {
        if (!peer) return;
        status(({ connected: "Conectado", connecting: "Conectando ao profissional…", disconnected: "Conexão interrompida. Tente reconectar.", failed: "A rede não permitiu a chamada. Tente outra rede ou reconecte.", closed: "Chamada encerrada" })[peer.connectionState] || "Aguardando profissional…");
    };
}
async function offer() { if (!active || !local || !initiator) return; await makePeer(); await peer.setLocalDescription(await peer.createOffer()); await connection.invoke("Signal", "offer", JSON.stringify(peer.localDescription)); }
async function signal(kind, payload) {
    if (!active || !local) return;
    try {
        const value = JSON.parse(payload);
        if (kind === "offer") { await makePeer(); await peer.setRemoteDescription(value); await peer.setLocalDescription(await peer.createAnswer()); await connection.invoke("Signal", "answer", JSON.stringify(peer.localDescription)); }
        else if (kind === "answer" && peer) await peer.setRemoteDescription(value);
        else if (kind === "candidate") { if (peer?.remoteDescription) await peer.addIceCandidate(value); else candidates.push(value); }
        if (peer?.remoteDescription) { for (const candidate of candidates) await peer.addIceCandidate(candidate); candidates = []; }
    } catch { status("Não foi possível estabelecer o vídeo. Tente reconectar."); }
}
export async function start(base, appointmentId) {
    await stop(false);
    try {
        const eligibility = await request(base, `api/v1/patient/experience/appointments/${appointmentId}`);
        if (!eligibility.ok || !eligibility.data.canJoinOnline) { status("A sala não está disponível para este atendimento."); return false; }
        if (!navigator.mediaDevices?.getUserMedia) { status("Use um navegador atualizado e uma conexão HTTPS para acessar câmera e microfone."); return false; }
        local = await navigator.mediaDevices.getUserMedia({ audio: true, video: { width: { ideal: 1280 }, height: { ideal: 720 } } });
        document.getElementById("local-video").srcObject = local;
        connection = new window.signalR.HubConnectionBuilder().withUrl(new URL("hubs/teleconsultation", base).toString(), { withCredentials: true, transport: window.signalR.HttpTransportType.WebSockets, skipNegotiation: true }).withAutomaticReconnect([0, 2000, 5000, 10000]).configureLogging(window.signalR.LogLevel.None).build();
        active = true;
        const join = async () => { const token = await request(base, "api/v1/auth/antiforgery"); if (!token.ok) throw new Error(); const state = await connection.invoke("Join", appointmentId, token.data.requestToken); initiator = state.initiator; status(state.peerPresent ? "Conectando…" : "Aguardando profissional…"); if (state.peerPresent) await offer(); };
        connection.on("Signal", signal);
        connection.on("PeerReady", () => offer().catch(() => status("Tente reconectar para iniciar o vídeo.")));
        connection.on("PeerLeft", () => { closePeer(); status("O outro participante saiu. Aguardando seu retorno…"); });
        connection.onreconnecting(() => { closePeer(); status("Reconectando…"); });
        connection.onreconnected(() => join().catch(async () => { await stop(false); status("Sua sala ou sessão expirou. Volte ao atendimento."); }));
        connection.onclose(async () => { if (active) { await stop(false); status("A chamada foi desconectada. Entre novamente para continuar."); } });
        await connection.start(); await join();
        heartbeat = setInterval(async () => { if (connection?.state === "Connected") try { await connection.invoke("Heartbeat"); } catch { await stop(false); status("A sala encerrou ou sua sessão expirou. Volte ao atendimento."); } }, 15000);
        qualityTimer = setInterval(async () => { if (!peer) return; try { const stats = await peer.getStats(); let loss = 0; for (const report of stats.values()) if (report.type === "inbound-rtp") loss = Math.max(loss, report.packetsLost / Math.max(1, report.packetsReceived + report.packetsLost)); const e = document.getElementById("call-quality"); if (e) e.textContent = peer.connectionState === "connected" ? (loss > .05 ? "Qualidade: rede instável. Experimente desligar a câmera." : "Qualidade: conexão estável.") : "Qualidade: aguardando conexão."; } catch { } }, 5000);
        window.addEventListener("pagehide", leavePage); return true;
    } catch (error) {
        await stop(false);
        status(error?.name === "NotAllowedError" ? "Permissão negada. Autorize câmera e microfone nas configurações do navegador." : error?.name === "NotFoundError" ? "Câmera ou microfone não encontrados. Conecte um dispositivo e tente novamente." : "Não foi possível entrar. Confira os dispositivos, a conexão e o horário do atendimento."); return false;
    }
}
function leavePage() { local?.getTracks().forEach(track => track.stop()); closePeer(); connection?.stop(); }
export function toggle(kind) { const tracks = local?.getTracks().filter(track => track.kind === kind) || []; const enabled = !tracks.some(track => track.enabled); for (const track of tracks) track.enabled = enabled; return enabled; }
export async function stop(showMessage = true) {
    active = false; clearInterval(heartbeat); clearInterval(qualityTimer); closePeer();
    local?.getTracks().forEach(track => track.stop()); local = null;
    const video = document.getElementById("local-video"); if (video) video.srcObject = null;
    const previous = connection; connection = null;
    if (previous) { try { if (previous.state === "Connected") await previous.invoke("Leave"); await previous.stop(); } catch { } }
    window.removeEventListener("pagehide", leavePage); if (showMessage) status("Você saiu da chamada. Câmera e microfone foram desligados.");
}
