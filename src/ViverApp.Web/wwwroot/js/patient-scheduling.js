async function readResponse(response) {
    if (response.ok) {
        if (response.status === 204) {
            return null;
        }

        return await response.json();
    }

    let message = "Não foi possível concluir a operação.";
    if (response.status === 401) {
        message = "Sua sessão expirou ou você ainda não entrou.";
    } else if (response.status === 403) {
        message = "Sua conta não tem permissão para esta operação.";
    }
    try {
        const problem = await response.json();
        message = problem.title || message;
    } catch {
        // A mensagem genérica evita exibir conteúdo não confiável retornado por proxies.
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

export async function send(baseUrl, path, method, body, idempotencyKey) {
    const tokenResponse = await fetch(endpoint(baseUrl, "api/v1/auth/antiforgery"), {
        method: "GET",
        credentials: "include",
        headers: { "Accept": "application/json" },
        cache: "no-store"
    });
    const token = await readResponse(tokenResponse);
    const headers = {
        "Accept": "application/json",
        "Content-Type": "application/json",
        "X-CSRF-TOKEN": token.requestToken
    };
    if (idempotencyKey) {
        headers["Idempotency-Key"] = idempotencyKey;
    }

    const response = await fetch(endpoint(baseUrl, path), {
        method,
        credentials: "include",
        headers,
        body: JSON.stringify(body),
        cache: "no-store"
    });
    return await readResponse(response);
}
