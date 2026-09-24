function endpoint(baseUrl, path) {
    return new URL(path.replace(/^\//, ""), baseUrl).toString();
}

async function readResponse(response) {
    if (response.ok) {
        return response.status === 204 ? null : await response.json();
    }

    const unexpected = "Ocorreu um erro interno não classificado. Contate o administrador do sistema.";
    if (response.status >= 500) throw Object.assign(new Error(unexpected), { status: response.status });
    let message = response.status === 401
        ? "Credenciais inválidas ou sessão expirada."
        : response.status === 403
            ? "Sua conta não tem permissão para esta ação."
            : "Não foi possível concluir a operação.";
    try {
        const problem = await response.json();
        const validationFields = problem.errors ? Object.keys(problem.errors) : [];
        const firstValidation = problem.errors ? Object.values(problem.errors).flat().find(value => value && !/one or more validation errors|the .+ field is required/i.test(value)) : null;
        const safeTitle = problem.title && !/one or more validation errors occurred|internal server error|^an error occurred/i.test(problem.title) ? problem.title : null;
        message = firstValidation || safeTitle || (validationFields.length ? "Revise os campos informados. Há informações ausentes ou inválidas." : message);
    } catch {
        // Nunca apresenta HTML ou texto não confiável vindo de proxies.
    }

    const error = new Error(message);
    error.status = response.status;
    throw error;
}

export async function get(baseUrl, path) {
    const response = await fetch(endpoint(baseUrl, path), {
        credentials: "include",
        headers: { "Accept": "application/json" },
        cache: "no-store"
    });
    return await readResponse(response);
}
export async function tryGetCurrent(baseUrl) {
    const response = await fetch(endpoint(baseUrl, "api/v1/auth/me"), {
        credentials: "include",
        headers: { "Accept": "application/json" },
        cache: "no-store"
    });
    if (response.status === 401 || response.status === 403) {
        return null;
    }
    return await readResponse(response);
}

export async function send(baseUrl, path, body) {
    return await sendMethod(baseUrl, path, "POST", body);
}

export async function sendMethod(baseUrl, path, method, body) {
    const tokenResponse = await fetch(endpoint(baseUrl, "api/v1/auth/antiforgery"), {
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
