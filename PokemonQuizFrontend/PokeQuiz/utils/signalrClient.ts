import * as SignalR from "@microsoft/signalr";

let _connection: SignalR.HubConnection | null = null;

export function getConnection(): SignalR.HubConnection | null {
    return _connection;
}

export async function ensureConnection(hubUrl: string): Promise<SignalR.HubConnection | null> {
    if (_connection && _connection.state === SignalR.HubConnectionState.Connected) return _connection;

    _connection = new SignalR.HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect()
        .build();

    try {
        await _connection.start();
        return _connection;
    } catch (err) {
        console.warn('Failed to start SignalR connection', err);
        return null;
    }
}
