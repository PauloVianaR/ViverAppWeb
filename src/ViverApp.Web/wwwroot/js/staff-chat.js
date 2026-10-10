import { openRealtime, closeRealtime } from "./realtime-connection.js";

export function connect(baseUrl, dotnet) {
    return openRealtime(baseUrl, "hubs/staff-chat", dotnet, {
        StaffMessage: "OnStaffMessage", MessagesRead: "OnMessagesRead",
        PresenceChanged: "OnPresenceChanged", ManagerAlert: "OnManagerAlert"
    }, "RefreshStaffFeed");
}

export function disconnect(id) { return closeRealtime(id); }
