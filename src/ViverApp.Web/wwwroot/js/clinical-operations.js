async function readResponse(response) {
    if (response.ok) {
        return response.status === 204 ? null : await response.json();
    }

    const unexpected = "Ocorreu um erro interno não classificado. Contate o administrador do sistema.";
    if (response.status >= 500) throw Object.assign(new Error(unexpected), { status: response.status });
    let message = response.status === 401
        ? "Sua sessão expirou ou você ainda não entrou."
        : response.status === 403
            ? "Sua conta não tem permissão para esta operação."
            : "Não foi possível concluir a operação clínica.";
    try {
        const problem = await response.json();
        const validationFields = problem.errors ? Object.keys(problem.errors) : [];
        const firstValidation = problem.errors ? Object.values(problem.errors).flat().find(value => value && !/one or more validation errors|the .+ field is required/i.test(value)) : null;
        const safeTitle = problem.title && !/one or more validation errors occurred|internal server error|^an error occurred/i.test(problem.title) ? problem.title : null;
        message = firstValidation || safeTitle || (validationFields.length ? "Revise os campos informados. Há informações ausentes ou inválidas." : message);
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
