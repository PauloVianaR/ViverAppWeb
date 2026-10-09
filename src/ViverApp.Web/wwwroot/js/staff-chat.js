let connection;
const endpoint = (baseUrl, path) => new URL(path.replace(/^\//, ""), baseUrl).toString();

export async function connect(baseUrl, dotnet) {
    await import("/vendor/signalr/signalr.min.js");
    connection = new globalThis.signalR.HubConnectionBuilder()
        .withUrl(endpoint(baseUrl, "hubs/staff-chat"), { withCredentials: true })
        .withAutomaticReconnect([0, 2000, 5000, 15000])
        .configureLogging(globalThis.signalR.LogLevel.Warning)
        .build();
    connection.on("StaffMessage", message => dotnet.invokeMethodAsync("OnStaffMessage", message));
    connection.on("MessagesRead", accountId => dotnet.invokeMethodAsync("OnMessagesRead", accountId));
    connection.on("PresenceChanged", (accountId, online) => dotnet.invokeMethodAsync("OnPresenceChanged", accountId, online));
    connection.on("ManagerAlert", alert => dotnet.invokeMethodAsync("OnManagerAlert", alert));
    connection.onreconnected(() => dotnet.invokeMethodAsync("RefreshStaffFeed"));
    try { await connection.start(); } catch { /* O histórico durável permanece acessível. */ }
}

export async function disconnect() {
    if (connection) { try { await connection.stop(); } catch { } connection = null; }
}
