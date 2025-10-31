import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity, Image, Platform, Alert } from 'react-native';
import { useRouter, useLocalSearchParams } from 'expo-router';
import { colors } from '../../styles/colours';
import Navbar from '@/components/Navbar';
import { getConnection, ensureConnection } from '../../utils/signalrClient';
import * as SignalR from '@microsoft/signalr';

export default function ChooseGame() {
    const router = useRouter();
    const params = useLocalSearchParams();
    const isMultiplayer = params.mode === 'multiplayer';
    const roomCode = params.roomCode as string;
    const playerName = typeof params.playerName === 'string' ? params.playerName : '';
    const isHost = String(params.isHost ?? '') === 'true';

    const games = [
        { id: 'guess-type', name: 'Guess the Type', image: require('../../assets/images/charizard.png') },
        { id: 'guess-stats', name: 'Guess the Stats', image: require('../../assets/images/charizard.png'), page:'/pages/GuessStat' },
        { id: 'who-that-pokemon', name: "Who's That Pokémon?", image: require('../../assets/images/charizard.png') },
    ];

    const handleGameSelect = async (gameId: string) => {
        const selectedGame = games.find(g => g.id === gameId);
        if (!selectedGame) return;

        if (isMultiplayer && roomCode) {
            const serverIp = Platform.OS === 'android' ? '10.0.2.2' : 'localhost';
            const hubUrl = `http://${serverIp}:5168/hubs/game`;

            // First try to use existing SignalR connection (host's live connection)
            try {
                let connection = getConnection();
                if (!connection || connection.state !== SignalR.HubConnectionState.Connected) {
                    // try to create/ensure connection
                    connection = await ensureConnection(hubUrl);
                }

                if (connection && connection.state === SignalR.HubConnectionState.Connected) {
                    await connection.invoke('SelectGame', roomCode, gameId, playerName);

                    // navigate back so the host returns to the previous screen and keeps the same connection state
                    if (isHost) {
                        // Replace to HostGame with params so HostGame can rehydrate without invoking CreateRoom
                        router.replace({ pathname: '/pages/HostGame', params: { roomCode, playerName, isHost: 'true' } } as any);
                    } else {
                        router.push({ pathname: '/pages/WaitingRoom', params: { roomCode, playerName, isHost: 'false' } } as any);
                    }
                    return;
                }
            } catch (err: any) {
                console.warn('SignalR SelectGame invoke failed, falling back to API', err?.message ?? err);
                // continue to fallback
            }

            // Fallback to server API to persist and broadcast selection
            try {
                const url = `http://${serverIp}:5168/api/lobby/select?code=${encodeURIComponent(roomCode)}&gameId=${encodeURIComponent(gameId)}&hostName=${encodeURIComponent(playerName)}`;
                const res = await fetch(url, { method: 'POST', headers: { 'Accept': 'application/json' }, mode: 'cors' });
                if (!res.ok) {
                    let msg = `Server returned ${res.status}`;
                    try {
                        const body = await res.json();
                        if (body?.message) msg = body.message;
                    } catch {
                        // ignore
                    }
                    Alert.alert('Failed to select game', msg);
                    return;
                }

                const json = await res.json();
                if (json?.success === false) {
                    Alert.alert('Failed to select game', json?.message ?? 'Unknown error');
                    return;
                }

                if (isHost) {
                    router.replace({ pathname: '/pages/HostGame', params: { roomCode, playerName, isHost: 'true' } } as any);
                } else {
                    router.push({ pathname: '/pages/WaitingRoom', params: { roomCode, playerName, isHost: 'false' } } as any);
                }
            } catch (err: any) {
                console.error(err);
                Alert.alert('Failed to notify server of selected game', err?.message ?? String(err));
            }

            return;
        }

        // Single player - navigate to the page defined for this game
        router.push({ pathname: selectedGame.page || '/pages/ChooseGame', params: { mode: 'single' } } as any);
    };

    return (
        <View style={styles.container}>
            <Navbar title={isMultiplayer ? "Choose Game (Room: " + roomCode + ")" : "Choose Game"} />

            <View style={styles.content}>
                <Text style={styles.subtitle}>
                    {isMultiplayer ? 'Select a game for your room' : 'Select a game to play'}
                </Text>

                <View style={styles.gamesContainer}>
                    {games.map((game) => (
                        <TouchableOpacity
                            key={game.id}
                            style={styles.gameCard}
                            onPress={() => handleGameSelect(game.id)}
                            activeOpacity={0.8}
                        >
                            <Image source={game.image} style={styles.gameImage} resizeMode="contain" />
                            <Text style={styles.gameName}>{game.name}</Text>
                        </TouchableOpacity>
                    ))}
                </View>
            </View>
        </View>
    );
}

const styles = StyleSheet.create({
    container: {
        flex: 1,
        backgroundColor: colors.background || '#151515',
    },
    content: {
        flex: 1,
        padding: 20,
        alignItems: 'center',
    },
    subtitle: {
        fontSize: 20,
        marginVertical: 30,
        textAlign: 'center',
        fontWeight: '600',
    },
    gamesContainer: {
        flexDirection: 'row',
        flexWrap: 'wrap',
        justifyContent: 'center',
        gap: 20,
        width: '100%',
    },
    gameCard: {
        width: 280,
        backgroundColor: colors.primary || '#FF5252',
        borderRadius: 20,
        padding: 20,
        alignItems: 'center',
        shadowColor: '#000',
        shadowOpacity: 0.25,
        shadowRadius: 8,
        shadowOffset: { width: 0, height: 4 },
        elevation: 5,
    },
    gameImage: {
        width: 150,
        height: 150,
        marginBottom: 15,
    },
    gameName: {
        fontSize: 18,
        fontWeight: '700',
        color: colors.white,
        textAlign: 'center',
    },
});