import * as React from 'react';
import { View, Text, StyleSheet, TouchableOpacity, Image, Platform, Alert, Dimensions, StatusBar, ScrollView } from 'react-native';
import { useRouter, useLocalSearchParams } from 'expo-router';
import { colors } from '../../styles/colours.js';
import Navbar from '../../components/Navbar.js';
import { getConnection, ensureConnection } from '../../utils/signalrClient.js';
import * as SignalR from '@microsoft/signalr';

// -----------------------------------------------------------------------------
// ChooseGame
//
// Purpose:
// - Present available game modes to the user (single-player or multiplayer flows).
// - For multiplayer rooms, notify the server of the selected game and navigate
//   hosts/clients into the appropriate waiting/host screens.
// -----------------------------------------------------------------------------

const { width, height } = Dimensions.get('window');

export default function ChooseGame() {
    const router = useRouter();
    const params = useLocalSearchParams();
    const isMultiplayer = params.mode === 'multiplayer';
    const roomCode = params.roomCode as string;
    const playerName = typeof params.playerName === 'string' ? params.playerName : '';
    const isHost = String(params.isHost ?? '') === 'true';

    // back handler inside component so router is in scope
    const handleBack = () => {
        try {
            router.replace({ pathname: "/pages/Mode" } as any);
        } catch {
            try { router.back(); } catch { }
        }
    };

    const topOffset = Platform.OS === 'android' ? (StatusBar.currentHeight ?? 24) : 0;

    // Games list: define which modes each game should appear in
    const games = [
        // Single-player: GuessStat
        { id: 'guess-stats', name: 'Guess the Stats', image: require('../../assets/images/charizard.png'), page: '/pages/GuessStat', showInModes: ['single'] },
        // Single-player: Higher or Lower (single player variant)
        { id: 'higher-or-lower-single', name: 'Higher or Lower', image: require('../../assets/images/charizard.png'), page: '/pages/HigherOrLowerSingle', showInModes: ['single'] },
        // Multiplayer-only: Multiplayer Guess Stat
        { id: 'multiplayer-guess-stats', name: 'Guess the Stats (Multiplayer)', image: require('../../assets/images/charizard.png'), page: '/pages/MultiplayerGuessStat', showInModes: ['multi'] }
    ];

    // Filter games for current mode
    const visibleGames = games.filter(g => isMultiplayer ? (g.showInModes || []).includes('multi') : (g.showInModes || []).includes('single'));

    // Debug: ensure the list is as expected in the browser console
    try { console.debug('visibleGames', visibleGames, 'isMultiplayer', isMultiplayer); } catch (e) { /* ignore */ }

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
                    // Tell server which game was selected for the room
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
        <View style={[styles.container, { paddingTop: topOffset }]}>
            <Navbar title={isMultiplayer ? `Choose Game (Room: ${roomCode})` : 'Choose Game'} onBack={handleBack} />
            <ScrollView contentContainerStyle={styles.scrollContent} showsVerticalScrollIndicator={true}>
                <View style={[styles.content, { paddingTop: 16 }]}>
                    <Text style={styles.title}>
                        {isMultiplayer ? 'Select a Game Mode for Your Room' : 'Select a Game Mode'}
                    </Text>
                    <Text style={styles.subtitle}>
                        {isMultiplayer ? 'Pick a game for everyone to play together!' : 'Pick a game to play!'}
                    </Text>
                    <View style={styles.gamesContainer}>
                        {visibleGames.map((game) => (
                            <TouchableOpacity
                                key={game.id}
                                style={[styles.gameCard, { alignSelf: 'center' }]}
                                onPress={() => handleGameSelect(game.id)}
                                activeOpacity={0.85}
                            >
                                <Image source={game.image} style={styles.gameImage} resizeMode="contain" />
                                <Text style={styles.gameName}>{game.name}</Text>
                            </TouchableOpacity>
                        ))}
                    </View>
                </View>
            </ScrollView>
        </View>
    );
}

const styles = StyleSheet.create({
    container: {
        flex: 1,
        backgroundColor: colors.background || '#151515',
    },
    scrollContent: {
        flexGrow: 1,
        justifyContent: 'center',
    },
    content: {
        padding: 16,
        alignItems: 'center',
        justifyContent: 'flex-start',
    },
    title: {
        fontSize: width < 360 ? 20 : 28,
        fontWeight: '900',
        color: colors.primary || '#FF5252',
        marginTop: 18,
        marginBottom: 6,
        textAlign: 'center',
        letterSpacing: 0.5,
    },
    subtitle: {
        fontSize: 16,
        color: colors.text || '#fff',
        marginBottom: 18,
        textAlign: 'center',
        fontWeight: '500',
    },
    gamesContainer: {
        width: '100%',
        alignItems: 'center',
    },
    gameCard: {
        width: Math.min(320, width * 0.9),
        backgroundColor: colors.primary || '#FF5252',
        borderRadius: 20,
        padding: 20,
        alignItems: 'center',
        marginVertical: 12,
        shadowColor: '#000',
        shadowOpacity: 0.18,
        shadowRadius: 8,
        shadowOffset: { width: 0, height: 4 },
        elevation: 5,
    },
    gameImage: {
        width: 120,
        height: 120,
        marginBottom: 12,
    },
    gameName: {
        fontSize: 20,
        fontWeight: '700',
        color: colors.white,
        textAlign: 'center',
        marginTop: 4,
    },
});