function endpoint(baseUrl, path) {
    return new URL(path.replace(/^\//, ""), baseUrl).toString();
}

async function readResponse(response) {
    if (response.ok) {
        return response.status === 204 ? null : await response.json();
    }

    let message = response.status === 401
        ? "Credenciais inválidas ou sessão expirada."
        : response.status === 403
            ? "Sua conta não tem permissão para esta ação."
            : "Não foi possível concluir a operação.";
    try {
        const problem = await response.json();
        const firstValidation = problem.errors
            ? Object.values(problem.errors).flat().find(Boolean)
            : null;
        message = firstValidation || problem.title || message;
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

function fromBase64Url(value) {
    const base64 = value.replace(/-/g, "+").replace(/_/g, "/");
    const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, "=");
    return Uint8Array.from(atob(padded), character => character.charCodeAt(0));
}

function toBase64Url(value) {
    const bytes = new Uint8Array(value);
    let binary = "";
    bytes.forEach(byte => binary += String.fromCharCode(byte));
    return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

export async function passkeyLogin(baseUrl, identifier) {
    if (!window.PublicKeyCredential) {
        throw new Error("Este navegador não oferece chaves de acesso.");
    }
    const options = await send(baseUrl, "api/v1/auth/passkeys/options", { identifier });
    options.challenge = fromBase64Url(options.challenge);
    options.allowCredentials = (options.allowCredentials || []).map(item => ({
        ...item,
        id: fromBase64Url(item.id)
    }));
    const credential = await navigator.credentials.get({ publicKey: options });
    const payload = {
        id: credential.id,
        rawId: toBase64Url(credential.rawId),
        type: credential.type,
        authenticatorAttachment: credential.authenticatorAttachment,
        clientExtensionResults: credential.getClientExtensionResults(),
        response: {
            authenticatorData: toBase64Url(credential.response.authenticatorData),
            clientDataJSON: toBase64Url(credential.response.clientDataJSON),
            signature: toBase64Url(credential.response.signature),
            userHandle: credential.response.userHandle ? toBase64Url(credential.response.userHandle) : null
        }
    };
    return await send(baseUrl, "api/v1/auth/passkeys/login", {
        credentialJson: JSON.stringify(payload),
        rememberMe: false
    });
}

export async function createPasskey(baseUrl, displayName) {
    if (!window.PublicKeyCredential) throw new Error("Este navegador não oferece chaves de acesso.");
    const options = await send(baseUrl, "api/v1/auth/passkeys/create/options", {});
    options.challenge = fromBase64Url(options.challenge);
    options.user.id = fromBase64Url(options.user.id);
    options.excludeCredentials = (options.excludeCredentials || []).map(item => ({ ...item, id: fromBase64Url(item.id) }));
    const credential = await navigator.credentials.create({ publicKey: options });
    const payload = {
        id: credential.id,
        rawId: toBase64Url(credential.rawId),
        type: credential.type,
        authenticatorAttachment: credential.authenticatorAttachment,
        clientExtensionResults: credential.getClientExtensionResults(),
        response: {
            attestationObject: toBase64Url(credential.response.attestationObject),
            clientDataJSON: toBase64Url(credential.response.clientDataJSON),
            transports: credential.response.getTransports ? credential.response.getTransports() : []
        }
    };
    await send(baseUrl, "api/v1/auth/passkeys/create/complete", { credentialJson: JSON.stringify(payload), displayName });
}
