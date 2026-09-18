function url(base, path) {
    const target = new URL(path.replace(/^\//, ""), base);
    if (target.origin !== new URL(base).origin) throw new Error("Destino inválido");
    return target;
}
const unexpectedError = "Ocorreu um erro interno não classificado. Contate o administrador do sistema.";
const fieldNames = {
    fullName: "nome completo", preferredName: "nome preferido", taxId: "CPF", birthDate: "data de nascimento",
    email: "e-mail", phoneE164: "telefone", postalCode: "CEP", street: "logradouro", number: "número",
    complement: "complemento", district: "bairro", city: "cidade", stateCode: "UF", amount: "valor",
    description: "descrição", reason: "motivo", typeCode: "tipo", directionCode: "direção", methodCode: "forma de pagamento"
};
function isGenericEnglish(value) {
    return /one or more validation errors occurred|internal server error|^an error occurred|^the .+ field is required/i.test(value || "");
}
function validationError(data) {
    const entries = Object.entries(data?.errors || {});
    if (!entries.length) return null;
    const custom = entries.flatMap(([, messages]) => Array.isArray(messages) ? messages : [])
        .find(message => message && !isGenericEnglish(message) && /[áàâãéêíóôõúç]/i.test(message));
    if (custom) return custom;
    const names = [...new Set(entries.map(([key]) => {
        const leaf = key.split(".").pop()?.replace(/^\$\.?/, "") || key;
        return fieldNames[leaf.charAt(0).toLowerCase() + leaf.slice(1)] || leaf;
    }))].filter(Boolean);
    return names.length
        ? `Revise ${names.length === 1 ? "o campo" : "os campos"} ${names.join(", ")}. Há informações ausentes ou inválidas.`
        : "Revise os dados informados. Há informações ausentes ou inválidas.";
}
async function result(response) {
    if (response.ok) return { ok: true, data: response.status === 204 ? null : await response.json(), status: response.status };
    if (response.status >= 500) return { ok: false, status: response.status, error: unexpectedError };
    let error = response.status === 401 ? "Sua sessão expirou. Entre novamente." : response.status === 403 ? "Sua conta não possui permissão para esta operação." : response.status === 404 ? "O registro solicitado não foi encontrado." : response.status === 409 ? "Os dados foram alterados por outra operação. Atualize a página e tente novamente." : response.status === 429 ? "Aguarde um pouco antes de tentar novamente." : "Não foi possível concluir a operação. Revise os dados e tente novamente.";
    try {
        const data = await response.json();
        const validation = validationError(data);
        if (validation) error = validation;
        else if (data.title && !isGenericEnglish(data.title)) error = data.title;
    } catch { }
    return { ok: false, status: response.status, error };
}
export async function request(base, path, method = "GET", body = null, key = null) {
    try {
        const headers = { Accept: "application/json" };
        if (method !== "GET") {
            const token = await fetch(url(base, "api/v1/auth/antiforgery"), { credentials: "include", cache: "no-store" });
            if (!token.ok) return await result(token);
            headers["X-CSRF-TOKEN"] = (await token.json()).requestToken;
            headers["Content-Type"] = "application/json";
            if (key) headers["Idempotency-Key"] = key;
        }
        return await result(await fetch(url(base, path), { method, headers, credentials: "include", cache: "no-store", body: method === "GET" ? undefined : JSON.stringify(body) }));
    } catch { return { ok: false, status: 0, error: "Conexão indisponível. Tente novamente." }; }
}
export async function upload(base, inputId, planId, progressId) {
    const file = document.getElementById(inputId)?.files?.[0];
    if (!file || file.size > 5242880) return { ok: false, error: "Escolha um PDF, PNG ou JPEG de até 5 MB." };
    const token = await request(base, "api/v1/auth/antiforgery");
    if (!token.ok) return token;
    const form = new FormData(); form.append("file", file); form.append("planId", planId);
    return await new Promise(resolve => {
        const xhr = new XMLHttpRequest(); xhr.open("POST", url(base, "api/v1/patient/experience/premium")); xhr.withCredentials = true;
        xhr.setRequestHeader("X-CSRF-TOKEN", token.data.requestToken); xhr.timeout = 30000;
        xhr.upload.onprogress = e => { const progress = document.getElementById(progressId); if (progress && e.lengthComputable) progress.value = Math.round(e.loaded * 100 / e.total); };
        xhr.onload = () => { let data; try { data = JSON.parse(xhr.responseText); } catch { } resolve({ ok: xhr.status >= 200 && xhr.status < 300, status: xhr.status, data, error: xhr.status < 500 ? data?.title : "Não foi possível enviar o documento." }); };
        xhr.onerror = xhr.ontimeout = () => resolve({ ok: false, error: "Não foi possível enviar. Confira a conexão e tente novamente." }); xhr.send(form);
    });
}
export async function uploadTo(base, path, inputId, progressId) {
    const file = document.getElementById(inputId)?.files?.[0];
    if (!file || file.size > 5242880) return { ok: false, error: "Escolha um PDF, PNG ou JPEG de até 5 MB." };
    const token = await request(base, "api/v1/auth/antiforgery");
    if (!token.ok) return token;
    const form = new FormData(); form.append("file", file);
    return await new Promise(resolve => {
        const xhr = new XMLHttpRequest(); xhr.open("POST", url(base, path)); xhr.withCredentials = true;
        xhr.setRequestHeader("X-CSRF-TOKEN", token.data.requestToken); xhr.timeout = 30000;
        xhr.upload.onprogress = e => { const progress = document.getElementById(progressId); if (progress && e.lengthComputable) progress.value = Math.round(e.loaded * 100 / e.total); };
        xhr.onload = () => { let data; try { data = JSON.parse(xhr.responseText); } catch { } resolve({ ok: xhr.status >= 200 && xhr.status < 300, status: xhr.status, data, error: xhr.status < 500 ? data?.title : "Não foi possível enviar o documento." }); };
        xhr.onerror = xhr.ontimeout = () => resolve({ ok: false, error: "Não foi possível enviar. Confira a conexão e tente novamente." });
        xhr.send(form);
    });
}
export async function download(base, path) {
    try {
        const response = await fetch(url(base, path), { credentials: "include", cache: "no-store" });
        if (!response.ok) return false;
        const blob = await response.blob(); const objectUrl = URL.createObjectURL(blob);
        const a = document.createElement("a"); a.href = objectUrl;
        a.download = "documento." + ({ "application/pdf": "pdf", "image/png": "png", "image/jpeg": "jpg" }[blob.type] || "bin");
        a.click(); setTimeout(() => URL.revokeObjectURL(objectUrl), 10000); return true;
    } catch { return false; }
}
let returnFocus;
export function showDialog(id) { const dialog = document.getElementById(id); returnFocus = document.activeElement; if (dialog && !dialog.open) dialog.showModal(); }
export function closeDialog(id) { document.getElementById(id)?.close(); returnFocus?.focus(); }
export function focus(id) { document.getElementById(id)?.focus(); }
