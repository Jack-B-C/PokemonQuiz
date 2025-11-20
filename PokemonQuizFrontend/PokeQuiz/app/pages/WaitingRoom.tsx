// Pokémon Quiz — Page: WaitingRoom
// Standard page header added for consistency. No behavior changes.

import React, { useState, useEffect, useRef } from "react";
import { View, Text, StyleSheet, FlatList, Alert, Button, Platform } from "react-native";
import Navbar from "@/components/Navbar";
import { useRouter, useLocalSearchParams } from "expo-router";
import * as SignalR from "@microsoft/signalr";
import { ensureConnection, getConnection } from '@/utils/signalrClient';

type Player = { name: string; isHost: boolean; };

const GAME_NAMES: Record<string, string> = {
    'guess-stats': 'Guess the Stats',
    'guess-type': 'Guess the Type',
    'who-that-pokemon': "Who's That Pokémon?"
};

export default function WaitingRoom() {
    const router = useRouter();
    const params = useLocalSearchParams();
    const roomCode = params.roomCode as string;
    const playerName = params.playerName as string;
    const isHost = String(params.isHost ?? '') === 'true';

    const [players, setPlayers] = useState<Player[]>([]);
    const [isConnected, setIsConnected] = useState(false);
    const [selectedGame, setSelectedGame] = useState<string | null>(null);
    const connectionRef = useRef<SignalR.HubConnection | null>(null);

    useEffect(() => {
        const serverIp = Platform.OS === "android" ? "10.0.2.2" : "localhost";
        const hubUrl = `http://${serverIp}:5168/hubs/game`;

        const setup = async () => {
            try {
                const connection = await ensureConnection(hubUrl);
                // If connection failed, bail out and inform the user — prevents 'possibly null' TS errors later when using `connection`.
                if (!connection) {
                    console.error('Failed to establish SignalR connection in WaitingRoom');
                    Alert.alert('Connection Error', 'Failed to connect to multiplayer server');
                    try { router.back(); } catch { }
                    return;
                }
                connectionRef.current = connection;

                // detach previous handlers to avoid duplicates
                try { connection.off("RoomJoined"); } catch {}
                try { connection.off("roomjoined"); } catch {}
                try { connection.off("PlayerJoined"); } catch {}
                try { connection.off("playerjoined"); } catch {}
                try { connection.off("PlayerLeft"); } catch {}
                try { connection.off("playerleft"); } catch {}
                try { connection.off("NewHost"); } catch {}
                try { connection.off("newhost"); } catch {}
                try { connection.off("GameSelected"); } catch {}
                try { connection.off("gameselected"); } catch {}
                try { connection.off("GameStarted"); } catch {}
                try { connection.off("gamestarted"); } catch {}
                try { connection.off("PokemonDataSeeded"); } catch {}

                // Register handlers
                connection.on("RoomJoined", (data: any) => {
                    console.log('RoomJoined', data);
                    setPlayers(data.players || []);
                    setIsConnected(true);
                    if (data.selectedGame) setSelectedGame(data.selectedGame);
                });
                connection.on("roomjoined", (data: any) => {
                    console.log('roomjoined', data);
                    setPlayers(data.players || []);
                    setIsConnected(true);
                    if (data.selectedGame) setSelectedGame(data.selectedGame);
                });

                connection.on("PlayerJoined", (data: any) => {
                    console.log('PlayerJoined', data);
                    setPlayers(data.players || []);
                });
                connection.on("playerjoined", (data: any) => {
                    console.log('playerjoined', data);
                    setPlayers(data.players || []);
                });

                connection.on("PlayerLeft", (data: any) => {
                    console.log('PlayerLeft', data);
                    setPlayers(data.players || []);
                });
                connection.on("playerleft", (data: any) => {
                    console.log('playerleft', data);
                    setPlayers(data.players || []);
                });

                connection.on("NewHost", (newHostName: string) => {
                    console.log('NewHost', newHostName);
                    Alert.alert("New Host", `${newHostName} is now the host`);
                });
                connection.on("newhost", (newHostName: string) => {
                    console.log('newhost', newHostName);
                    Alert.alert("New Host", `${newHostName} is now the host`);
                });

                connection.on("GameSelected", (gameId: string) => {
                    console.log('GameSelected', gameId);
                    setSelectedGame(gameId);
                });
                connection.on("gameselected", (gameId: string) => {
                    console.log('gameselected', gameId);
                    setSelectedGame(gameId);
                });

                connection.on("GameStarted", (payload: any) => {
                    console.log('GameStarted', payload);
                    // payload may be a string (gameId) or object { gameId, currentQuestion, questionStartedAt }
                    const gameId = typeof payload === 'string' ? payload : payload?.gameId;
                    const currentQuestion = typeof payload === 'object' ? payload?.currentQuestion : undefined;
                    const questionStartedAt = typeof payload === 'object' ? payload?.questionStartedAt : undefined;

                    if (gameId === 'guess-stats') {
                        router.push({ pathname: '/pages/MultiplayerGuessStat', params: { roomCode, playerName, isHost: isHost ? 'true' : 'false', initialQuestion: currentQuestion ? JSON.stringify(currentQuestion) : undefined, questionStartedAt } } as const);
                    } else if (gameId === 'higher-or-lower' || gameId === 'compare-stat') {
                        router.push({ pathname: '/pages/MultiplayerHigherOrLower', params: { roomCode, playerName, isHost: isHost ? 'true' : 'false', initialQuestion: currentQuestion ? JSON.stringify(currentQuestion) : undefined, questionStartedAt } } as const);
                    } else {
                        Alert.alert('Game starting', `Game ${gameId} starting`);
                    }
                });
                connection.on("gamestarted", (payload: any) => {
                    console.log('gamestarted', payload);
                    const gameId = typeof payload === 'string' ? payload : payload?.gameId;
                    const currentQuestion = typeof payload === 'object' ? payload?.currentQuestion : undefined;
                    const questionStartedAt = typeof payload === 'object' ? payload?.questionStartedAt : undefined;

                    if (gameId === 'guess-stats') {
                        router.push({ pathname: '/pages/MultiplayerGuessStat', params: { roomCode, playerName, isHost: isHost ? 'true' : 'false', initialQuestion: currentQuestion ? JSON.stringify(currentQuestion) : undefined, questionStartedAt } } as const);
                    } else if (gameId === 'higher-or-lower' || gameId === 'compare-stat') {
                        router.push({ pathname: '/pages/MultiplayerHigherOrLower', params: { roomCode, playerName, isHost: isHost ? 'true' : 'false', initialQuestion: currentQuestion ? JSON.stringify(currentQuestion) : undefined, questionStartedAt } } as const);
                    } else {
                        Alert.alert('Game starting', `Game ${gameId} starting`);
                    }
                });

                connection.on("Error", (message: string) => {
                    console.log('Error event', message);
                    Alert.alert("Error", message);
                });
                connection.on("error", (message: string) => {
                    console.log('error event', message);
                    Alert.alert("Error", message);
                });

                // When Pokemon data is seeded on server, rehydrate room info so guests update
                connection.on("PokemonDataSeeded", async (payload: any) => {
                    console.log('PokemonDataSeeded (waiting room) received', payload);
                    try {
                        const info = await connection.invoke('GetRoomInfo', roomCode);
                        if (info) {
                            if (info.players) setPlayers(info.players);
                            if (info.selectedGame) setSelectedGame(info.selectedGame);
                        }
                    } catch (e) {
                        console.warn('GetRoomInfo after PokemonDataSeeded failed', e);
                    }
                });

                // Invoke JoinRoom. If name already taken, rehydrate via GetRoomInfo instead of leaving.
                try {
                    // connection is guaranteed to be non-null here due to the guard above
                    await connection.invoke("JoinRoom", roomCode, playerName);
                    console.log('JoinRoom invoked successfully');
                } catch (err: any) {
                    console.warn('JoinRoom failed', err?.message ?? err);
                    const msg = (err?.message ?? '').toString();
                    if (msg.includes('Name already taken')) {
                        // rehydrate room state instead of exiting
                        try {
                            const info = await connection.invoke('GetRoomInfo', roomCode);
                            if (info) {
                                if (info.players) setPlayers(info.players);
                                if (info.selectedGame) setSelectedGame(info.selectedGame);
                                setIsConnected(true);
                                console.log('Rehydrated room after name-taken');
                            }
                        } catch (e) {
                            console.warn('GetRoomInfo after name-taken failed', e);
                            // fallback: alert but stay on screen
                            Alert.alert('Warning', 'Room join conflict. If you are the host, try refreshing.');
                        }
                    } else {
                        Alert.alert("Connection Error", "Failed to join room");
                        try { router.back(); } catch { }
                    }
                }

            } catch (err) {
                console.error('Connection setup failed', err);
                Alert.alert("Connection Error", "Failed to connect to multiplayer server");
                router.back();
            }
        };

        setup();

        return () => {
            // Do not stop the shared connection here; other pages rely on it.
            connectionRef.current = null;
        };
    }, [roomCode, playerName]);

    const startGame = async () => {
        const connection = getConnection();
        if (!connection) return;
        try {
            await connection.invoke('StartGame', roomCode);
        } catch (err) {
            console.error(err);
            Alert.alert('Error', 'Failed to start game');
        }
    };

    return (
        <View style={styles.container}>
            <Navbar title="Waiting Room" backTo={'/pages/ChooseGame'} />
            <View style={styles.content}>
                <Text style={styles.title}>Room: {roomCode}</Text>
                <Text style={styles.selectedGame}>
                    Selected Game: {selectedGame ? GAME_NAMES[selectedGame] || selectedGame : 'No game selected'}
                </Text>
                {isConnected ? (
                    <>
                        <Text style={styles.subtitle}>Waiting for host to start...</Text>

                        <View style={styles.playersSection}>
                            <Text style={styles.playersTitle}>Players ({players.length})</Text>
                            <FlatList
                                data={players}
                                keyExtractor={(item, index) => index.toString()}
                                renderItem={({ item }) => (
                                    <View style={styles.playerCard}>
                                        <Text style={styles.playerName}>{item.name}{item.isHost && " 👑"}</Text>
                                    </View>
                                )}
                            />
                        </View>

                        {isHost && (
                            <View style={{ marginTop: 20 }}>
                                <Button title="Start Game" onPress={startGame} />
                            </View>
                        )}
                    </>
                ) : <Text style={styles.connecting}>Joining room...</Text>}
            </View>
        </View>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, backgroundColor: "#FFFFFF" },
    content: { flex: 1, justifyContent: "center", alignItems: "center", padding: 20 },
    title: { fontSize: 36, fontWeight: "900", color: "#FF5252", marginBottom: 20, letterSpacing: 4 },
    subtitle: { fontSize: 18, color: "#222", marginBottom: 40, textAlign: "center", opacity: 0.9 },
    connecting: { fontSize: 18, color: "#222", opacity: 0.7 },
    playersSection: { width: "100%", maxHeight: 400 },
    playersTitle: { fontSize: 20, color: "#222", marginBottom: 15, textAlign: "center", fontWeight: "700" },
    playerCard: { backgroundColor: "#FF5252", padding: 15, borderRadius: 10, marginVertical: 5 },
    playerName: { fontSize: 16, color: "#fff", textAlign: "center", fontWeight: "600" },
    selectedGame: { fontSize: 18, marginBottom: 10 }
});
