import * as SignalR from '@microsoft/signalr';

let _connection = null;

export function getConnection() {
  return _connection;
}

export async function ensureConnection(hubUrl) {
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
