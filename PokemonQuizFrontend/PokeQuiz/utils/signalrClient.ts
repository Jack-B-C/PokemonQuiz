import * as SignalR from "@microsoft/signalr";

let connection: SignalR.HubConnection | null = null;
let defaultsRegistered = false;

export async function ensureConnection(hubUrl: string) {
  if (connection && connection.state === SignalR.HubConnectionState.Connected) {
    return connection;
  }

  if (!connection) {
    connection = new SignalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .configureLogging(SignalR.LogLevel.Information)
      .build();
  }

  // Register default no-op handlers to prevent warnings when server invokes events
  if (!defaultsRegistered) {
    const events = [
      'GameStarted', 'gamestarted',
      'GameSelected', 'gameselected',
      'Question', 'question',
      'ScoreUpdated', 'scoreupdated',
      'PlayerJoined', 'playerjoined',
      'PlayerLeft', 'playerleft',
      'RoomJoined', 'roomjoined',
      'Error', 'error',
      'AllAnswered', 'allanswered'
    ];

    for (const ev of events) {
      // add harmless default handler
      try { connection.on(ev, (...args: any[]) => { console.debug('default handler', ev, args); }); } catch { }
    }

    defaultsRegistered = true;
  }

  try {
    if (connection.state !== SignalR.HubConnectionState.Connected) {
      await connection.start();
    }
    return connection;
  } catch (err) {
    // if start fails, clear connection so caller can retry
    connection = null;
    defaultsRegistered = false;
    throw err;
  }
}

export function getConnection() {
  return connection;
}

export async function stopConnection() {
  try {
    if (connection) {
      await connection.stop();
      connection = null;
      defaultsRegistered = false;
    }
  } catch {
    connection = null;
    defaultsRegistered = false;
  }
}
