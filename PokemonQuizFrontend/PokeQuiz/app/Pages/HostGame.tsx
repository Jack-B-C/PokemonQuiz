import * as React from "react";
import { View, Text, StyleSheet, TextInput, Alert, Platform, Button } from "react-native";
import Navbar from '@/components/Navbar';
import AppButton from '@/components/AppButton';
import { useRouter, useLocalSearchParams } from 'expo-router';
import * as SignalR from "@microsoft/signalr";
import { ensureConnection, getConnection } from '@/utils/signalrClient';

export default function HostGame() {
    const router = useRouter();
    const params = useLocalSearchParams();
    const paramRoomCode = params.roomCode as string | undefined;
    const paramPlayerName = params.playerName as string | undefined;

    const [hostName, setHostName] = React.useState(paramPlayerName ?? "");
    const [roomCode, setRoomCode] = React.useState(paramRoomCode ?? "");
    const [players, setPlayers] = React.useState<{ name: string; isHost: boolean }[]>([]);
    const [isConnected, setIsConnected] = React.useState(false);
    const [selectedGame, setSelectedGame] = React.useState<string | null>(null);
    const connectionRef = React.useRef<SignalR.HubConnection | null>(null);
    const autoStartRef = React.useRef(false);

    // ensure back goes to choose game and cleanup connection
    const handleBack = async () => {
        try {
            const conn = connectionRef.current ?? getConnection();
            if (conn) {
                try { if (roomCode) await conn.invoke('LeaveRoom', roomCode); } catch { /* ignore */ }
                try { await conn.stop(); } catch { /* ignore */ }
            }
        } catch (e) {
            console.warn('Error during back cleanup', e);
        }
        try { router.replace({ pathname: '/pages/ChooseGame' } as any); } catch { router.push('/pages/ChooseGame' as any); }
    };

    const initConnectionAndRehydrate = async (hubUrl: string, rc?: string, name?: string) => {
        try {
            const connection = await ensureConnection(hubUrl);
            if (!connection) {
                console.error('Failed to establish SignalR connection in HostGame');
                Alert.alert('Connection Error', 'Failed to connect to multiplayer server');
                return;
            }
            connectionRef.current = connection;

            // attach handlers (avoid duplicates)
            try { connection.off("RoomCreated"); } catch {}
            try { connection.off("roomcreated"); } catch {}
            try { connection.off("PlayerJoined"); } catch {}
            try { connection.off("playerjoined"); } catch {}
            try { connection.off("Error"); } catch {}
            try { connection.off("error"); } catch {}
            try { connection.off("GameSelected"); } catch {}
            try { connection.off("gameselected"); } catch {}
            try { connection.off("GameStarted"); } catch {}
            try { connection.off("gamestarted"); } catch {}
            try { connection.off("PokemonDataSeeded"); } catch {}

            connection.on("RoomCreated", async (data: any) => {
                const rc2 = data?.roomCode ?? data?.roomcode ?? "";
                setRoomCode(rc2);
                setPlayers(data.players ?? []);
                setIsConnected(true);

                try {
                    const info = await connection.invoke('GetRoomInfo', rc2);
                    if (info && info.selectedGame) setSelectedGame(info.selectedGame as string);
                } catch (e) {
                    console.warn('GetRoomInfo failed', e);
                }
            });
            connection.on("roomcreated", async (data: any) => {
                const rc2 = data?.roomCode ?? data?.roomcode ?? "";
                setRoomCode(rc2);
                setPlayers(data.players ?? []);
                setIsConnected(true);

                try {
                    const info = await connection.invoke('GetRoomInfo', rc2);
                    if (info && info.selectedGame) setSelectedGame(info.selectedGame as string);
                } catch (e) {
                    console.warn('GetRoomInfo failed', e);
                }
            });

            connection.on("PlayerJoined", (data: any) => setPlayers(data.players ?? []));
            connection.on("playerjoined", (data: any) => setPlayers(data.players ?? []));

            connection.on("Error", (message: string) => {
                Alert.alert("Error", message);
            });
            connection.on("error", (message: string) => {
                Alert.alert("Error", message);
            });

            connection.on("GameSelected", (gameId: string) => setSelectedGame(gameId));
            connection.on("gameselected", (gameId: string) => setSelectedGame(gameId));

            // When server starts the game, host should navigate to the game page
            connection.on("GameStarted", (gameId: string) => {
                console.log('GameStarted (host) received', gameId);

                const navigateWhenReady = async () => {
                    const conn = connectionRef.current ?? connection;
                    const roomParam = rc ?? roomCode; // don't shadow outer variable
                    const maxAttempts = 5;
                    let attempt = 0;
                    let info: any = null;
                    try {
                        while (attempt < maxAttempts) {
                            attempt++;
                            try {
                                info = await conn.invoke('GetRoomInfo', roomParam);
                                console.debug('Host GameStarted rehydrate attempt', attempt, 'info=', info);
                                if (info && info.currentQuestion) {
                                    // we have question available — navigate now
                                    break;
                                }
                            } catch (e) {
                                console.warn('Host rehydrate GetRoomInfo failed on attempt', attempt, e);
                            }
                            // small backoff
                            await new Promise(res => setTimeout(res, 200));
                        }
                    } catch { }

                    // Prepare navigation params, include initialQuestion if present
                    let navParams: any = { roomCode: roomParam, playerName: name ?? hostName, isHost: 'true' };
                    try {
                        if (!info) info = await conn.invoke('GetRoomInfo', roomParam);
                        if (info && info.currentQuestion) {
                            navParams.initialQuestion = JSON.stringify(info.currentQuestion);
                            if (info.questionStartedAt) navParams.questionStartedAt = info.questionStartedAt;
                        }
                    } catch (e) { console.warn('Final GetRoomInfo before navigate failed', e); }

                    // Navigate after attempts even if no question found (avoid blocking indefinitely)
                    if (gameId === 'guess-stats') {
                        router.replace({ pathname: '/pages/MultiplayerGuessStat', params: navParams } as any);
                    } else if (gameId === 'higher-or-lower' || gameId === 'compare-stat') {
                        router.replace({ pathname: '/pages/MultiplayerHigherOrLower', params: navParams } as any);
                    } else {
                        router.replace({ pathname: '/pages/MultiplayerGuessStat', params: navParams } as any);
                    }
                };

                // fire-and-forget
                void navigateWhenReady();
            });
            connection.on("gamestarted", (gameId: string) => {
                console.log('gamestarted (host) received', gameId);
                if (gameId === 'guess-stats') {
                    router.replace({ pathname: '/pages/MultiplayerGuessStat', params: { roomCode: rc ?? roomCode, playerName: hostName, isHost: 'true' } } as any);
                } else if (gameId === 'higher-or-lower' || gameId === 'compare-stat') {
                    router.replace({ pathname: '/pages/MultiplayerHigherOrLower', params: { roomCode: rc ?? roomCode, playerName: hostName, isHost: 'true' } } as any);
                } else {
                    router.replace({ pathname: '/pages/MultiplayerGuessStat', params: { roomCode: rc ?? roomCode, playerName: hostName, isHost: 'true' } } as any);
                }
            });

            // When Pokemon data is seeded, if host has selected a game and hasn't started, auto-start
            connection.on("PokemonDataSeeded", async (payload: any) => {
                console.log('PokemonDataSeeded received', payload);
                if (!autoStartRef.current && (rc ?? roomCode) && selectedGame) {
                    autoStartRef.current = true;
                    try {
                        await connection.invoke('StartGame', rc ?? roomCode);
                    } catch (e) {
                        console.warn('Auto StartGame failed', e);
                    }
                }
            });

            // If params provided, rehydrate room info instead of creating a new one
            if (rc) {
                try {
                    const info = await connection.invoke('GetRoomInfo', rc);
                    if (info) {
                        if (info.players) setPlayers(info.players);
                        if (info.selectedGame) setSelectedGame(info.selectedGame);
                        setRoomCode(rc);
                        setIsConnected(true);
                    }
                } catch (e) {
                    console.warn('GetRoomInfo during rehydrate failed', e);
                }
            }

        } catch (err: any) {
            console.error('initConnection failed', err);
            Alert.alert('Error', 'Failed to connect to game server: ' + (err?.message ?? String(err)));
        }
    };

    const createRoom = async () => {
        if (!hostName.trim()) {
            Alert.alert("Error", "Please enter your name");
            return;
        }

        const serverIp = Platform.OS === "android" ? "10.0.2.2" : "localhost";
        const hubUrl = `http://${serverIp}:5168/hubs/game`;

        try {
            await initConnectionAndRehydrate(hubUrl);
            const connection = connectionRef.current;
            if (!connection) throw new Error('No connection');

            await connection.invoke("CreateRoom", hostName);
        } catch (err: any) {
            console.error(err);
            Alert.alert("Error", "Failed to create room: " + (err?.message ?? String(err)));
        }
    };

    React.useEffect(() => {
        // If navigated back with params (after selecting game), rehydrate
        if (paramRoomCode || paramPlayerName) {
            const serverIp = Platform.OS === "android" ? "10.0.2.2" : "localhost";
            const hubUrl = `http://${serverIp}:5168/hubs/game`;
            initConnectionAndRehydrate(hubUrl, paramRoomCode, paramPlayerName);
        }

        return () => {
            // keep shared connection alive
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    const handleSelectGame = () => {
        if (!roomCode) return;
        router.push({
            pathname: "/pages/ChooseGame",
            params: { mode: "multiplayer", roomCode, isHost: "true", playerName: hostName },
        } as any);
    };

    const startGame = async () => {
        const connection = getConnection();
        if (!connection) {
            Alert.alert('Error', 'Not connected');
            return;
        }

        try {
            await connection.invoke('StartGame', roomCode);
        } catch (err: any) {
            Alert.alert('Error', 'Failed to start game: ' + (err?.message ?? String(err)));
        }
    };

    return (
        <View style={styles.container}>
            <Navbar title="Host Game" onBack={handleBack} />
            <View style={styles.content}>
                {!isConnected ? (
                    <>
                        <Text style={styles.title}>Enter Your Name</Text>
                        <TextInput
                            style={styles.nameInput}
                            placeholder="Your Name"
                            placeholderTextColor="#999"
                            value={hostName}
                            onChangeText={setHostName}
                        />
                        <AppButton label="Create Room" onPress={createRoom} />
                    </>
                ) : (
                    <>
                        <Text style={styles.title}>Room Code</Text>
                        <Text style={styles.roomCode}>{roomCode}</Text>
                        <Text style={styles.subtitle}>Share this code with your friends!</Text>
                        <Text style={styles.playersLabel}>Players:</Text>
                        {players.map((p, i) => (
                            <Text key={i} style={styles.playerName}>
                                {p.name} {p.isHost ? "👑" : ""}
                            </Text>
                        ))}
                        <AppButton label="Select Game" onPress={handleSelectGame} />
                        {selectedGame && (
                            <>
                                <Text style={{ marginTop: 10 }}>Selected: {selectedGame}</Text>
                                <Button title="Start Game" onPress={startGame} />
                            </>
                        )}
                    </>
                )}
            </View>
        </View>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, backgroundColor: "#ffffff" },
    content: { flex: 1, justifyContent: "center", alignItems: "center", padding: 20 },
    title: { fontSize: 28, fontWeight: "700", color: "#333", marginBottom: 20, textAlign: "center" },
    subtitle: { fontSize: 16, color: "#666", marginBottom: 20 },
    nameInput: { width: "80%", padding: 15, borderRadius: 10, fontSize: 18, marginBottom: 20, textAlign: "center", borderWidth: 2, borderColor: "#FF5252" },
    roomCode: { fontSize: 48, fontWeight: "900", color: "#FF5252", letterSpacing: 8, marginBottom: 20 },
    playersLabel: { fontSize: 20, fontWeight: "700", color: "#444", marginTop: 10 },
    playerName: { fontSize: 18, color: "#FF5252", marginVertical: 4 },
});
