export { request, download, showDialog, closeDialog, focus } from "./patient-experience.js";
function target(base, path) { const value = new URL(path.replace(/^\//, ""), base); if (value.origin !== new URL(base).origin) throw new Error("Destino inválido"); return value; }
export async function upload(base, path, inputId, progressId) {
    const file = document.getElementById(inputId)?.files?.[0];
    if (!file || file.size > 10485760) return { ok: false, error: "Escolha um PDF, PNG ou JPEG de até 10 MB." };
    const token = await fetch(target(base, "api/v1/auth/antiforgery"), { credentials: "include", cache: "no-store" });
    if (!token.ok) return { ok: false, status: token.status, error: "Atualize a página e tente novamente." };
    const form = new FormData(); form.append("file", file);
    return await new Promise(resolve => { const xhr = new XMLHttpRequest(); xhr.open("POST", target(base, path)); xhr.withCredentials = true; xhr.timeout = 45000;
        xhr.setRequestHeader("X-CSRF-TOKEN", (JSON.parse(token.responseText)).requestToken);
        xhr.upload.onprogress = e => { const progress = document.getElementById(progressId); if (progress && e.lengthComputable) progress.value = Math.round(e.loaded * 100 / e.total); };
        xhr.onload = () => { let data; try { data = JSON.parse(xhr.responseText); } catch { } resolve({ ok: xhr.status >= 200 && xhr.status < 300, status: xhr.status, data, error: xhr.status < 500 ? data?.title : "Não foi possível enviar o anexo." }); };
        xhr.onerror = xhr.ontimeout = () => resolve({ ok: false, status: 0, error: "Falha de conexão durante o envio." }); xhr.send(form); });
}
