const unexpected = "Ocorreu um erro interno não classificado. Contate o administrador do sistema.";

async function read(response) {
    if (response.ok) return response.status === 204 ? null : await response.json();
    if (response.status >= 500) throw new Error(unexpected);
    let message = response.status === 401 ? "Sua sessão expirou. Entre novamente."
        : response.status === 403 ? "Sua conta não tem permissão para acessar este conteúdo."
            : "Não foi possível concluir a operação no prontuário.";
    try {
        const problem = await response.json();
        const first = problem.errors ? Object.values(problem.errors).flat().find(x => x && !/one or more validation errors|field is required/i.test(x)) : null;
        const title = problem.title && !/one or more validation errors|internal server error|an error occurred/i.test(problem.title) ? problem.title : null;
        message = first || title || message;
    } catch { }
    throw new Error(message);
}

function url(baseUrl, path) { return new URL(path.replace(/^\//, ""), baseUrl).toString(); }
function headers(purpose) {
    const value = { "Accept": "application/json" };
    if (purpose) value["X-Clinical-Purpose"] = purpose;
    return value;
}

async function antiforgery(baseUrl) {
    return await read(await fetch(url(baseUrl, "api/v1/auth/antiforgery"), {
        method: "GET", credentials: "include", headers: { "Accept": "application/json" }, cache: "no-store"
    }));
}

export async function get(baseUrl, path, purpose) {
    return await read(await fetch(url(baseUrl, path), {
        method: "GET", credentials: "include", headers: headers(purpose), cache: "no-store"
    }));
}

export async function send(baseUrl, path, method, body, purpose) {
    const token = await antiforgery(baseUrl);
    const requestHeaders = headers(purpose);
    requestHeaders["Content-Type"] = "application/json";
    requestHeaders["X-CSRF-TOKEN"] = token.requestToken;
    return await read(await fetch(url(baseUrl, path), {
        method, credentials: "include", headers: requestHeaders,
        body: body === null ? null : JSON.stringify(body), cache: "no-store"
    }));
}

export async function upload(baseUrl, path, inputId) {
    const input = document.getElementById(inputId);
    if (!input?.files?.length) throw new Error("Selecione um arquivo antes de enviar.");
    const token = await antiforgery(baseUrl);
    const form = new FormData(); form.append("file", input.files[0]);
    return await read(await fetch(url(baseUrl, path), {
        method: "POST", credentials: "include", headers: { "Accept": "application/json", "X-CSRF-TOKEN": token.requestToken },
        body: form, cache: "no-store"
    }));
}

async function saveBlob(response, fallback) {
    if (!response.ok) return await read(response);
    const blob = await response.blob();
    const disposition = response.headers.get("Content-Disposition") || "";
    const match = disposition.match(/filename\*?=(?:UTF-8''|\")?([^\";]+)/i);
    const name = match ? decodeURIComponent(match[1].replace(/\"/g, "")) : fallback;
    const anchor = document.createElement("a"); anchor.href = URL.createObjectURL(blob); anchor.download = name;
    document.body.appendChild(anchor); anchor.click(); anchor.remove(); setTimeout(() => URL.revokeObjectURL(anchor.href), 1000);
}

export async function download(baseUrl, path, purpose, fallback) {
    await saveBlob(await fetch(url(baseUrl, path), {
        method: "GET", credentials: "include", headers: headers(purpose), cache: "no-store"
    }), fallback);
}

export async function downloadPdf(baseUrl, path, body) {
    const token = await antiforgery(baseUrl);
    await saveBlob(await fetch(url(baseUrl, path), {
        method: "POST", credentials: "include",
        headers: { "Accept": "application/pdf", "Content-Type": "application/json", "X-CSRF-TOKEN": token.requestToken },
        body: JSON.stringify(body), cache: "no-store"
    }), "prontuario.pdf");
}
