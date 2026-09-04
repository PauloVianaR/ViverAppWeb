async function readResponse(response) {
    if (response.ok) {
        return response.status === 204 ? null : await response.json();
    }

    let message = response.status === 401
        ? "Sua sessão expirou ou você ainda não entrou."
        : response.status === 403
            ? "Sua conta não tem permissão para esta operação."
            : "Não foi possível concluir a operação clínica.";
    try {
        const problem = await response.json();
        message = problem.title || message;
    } catch {
        // Não propaga conteúdo de proxies ou páginas HTML para a interface.
    }

    const error = new Error(message);
    error.status = response.status;
    throw error;
}

function endpoint(baseUrl, path) {
    return new URL(path.replace(/^\//, ""), baseUrl).toString();
}

export async function get(baseUrl, path) {
    const response = await fetch(endpoint(baseUrl, path), {
        method: "GET",
        credentials: "include",
        headers: { "Accept": "application/json" },
        cache: "no-store"
    });
    return await readResponse(response);
}

export async function send(baseUrl, path, method, body) {
    const tokenResponse = await fetch(endpoint(baseUrl, "api/v1/auth/antiforgery"), {
        method: "GET",
        credentials: "include",
        headers: { "Accept": "application/json" },
        cache: "no-store"
    });
    const token = await readResponse(tokenResponse);
    const response = await fetch(endpoint(baseUrl, path), {
        method,
        credentials: "include",
        headers: {
            "Accept": "application/json",
            "Content-Type": "application/json",
            "X-CSRF-TOKEN": token.requestToken
        },
        body: body === null ? null : JSON.stringify(body),
        cache: "no-store"
    });
    return await readResponse(response);
}
