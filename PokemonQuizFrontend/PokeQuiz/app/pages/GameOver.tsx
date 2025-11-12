import * as React from "react";
import { View, Text, StyleSheet, Alert, Platform } from "react-native";
import { colors } from "../../styles/colours";
import AppButton from "../../components/AppButton";
import { useRouter, useLocalSearchParams } from "expo-router";
import { ensureConnection, getConnection } from '../../utils/signalrClient';

export default function GameOver() {
    const router = useRouter();
    const params = useLocalSearchParams();

    // Multiplayer support: roomCode, isHost, playerName may be present
    const roomCode = params.roomCode as string | undefined;
    const isHost = String(params.isHost ?? '') === 'true';
    const playerName = params.playerName as string | undefined;

    // If leaderboard param provided (multiplayer), it will be a JSON string
    const leaderboardJson = params.leaderboard as string | undefined;

    // support optional returnTo param so pages can control where back goes
    const returnTo = params.returnTo as string | undefined;

    if (leaderboardJson) {
        let leaderboard: { Name?: string; name?: string; Score?: number; score?: number }[] = [];
        try {
            leaderboard = JSON.parse(leaderboardJson);
        } catch (e) {
            console.warn('Failed to parse leaderboard', e);
        }

        return (
            <View style={styles.container}>
                <Text style={styles.title}>🏆 Lobby Leaderboard</Text>
                <View style={{ width: '100%', paddingHorizontal: 20 }}>
                    {leaderboard.map((p, i) => (
                        <React.Fragment key={i}>
                            <Text style={styles.leaderText}>{i + 1}. {p.Name ?? p.name}: {p.Score ?? p.score}</Text>
                        </React.Fragment>
                    ))}
                </View>

                {isHost && roomCode ? (
                    <AppButton
                        label="Play Again"
                        onPress={async () => {
                            try {
                                const serverIp = Platform.OS === 'android' ? '10.0.2.2' : 'localhost';
                                const hubUrl = `http://${serverIp}:5168/hubs/game`;
                                const conn = await ensureConnection(hubUrl);
                                if (!conn) throw new Error('No connection');
                                await conn.invoke('StartGame', roomCode);
                            } catch (err: any) {
                                console.warn('Failed to replay as host', err);
                                Alert.alert('Error', 'Failed to start a new game: ' + (err?.message ?? String(err)));
                            }
                        }}
                        backgroundColor={colors.primary}
                        style={styles.button}
                    />
                ) : null}

                <AppButton
                    label="Back to Menu"
                    onPress={() => router.replace({ pathname: "/pages/ChooseGame" })}
                    backgroundColor={colors.primaryDark || "#666"}
                    style={styles.button}
                />
            </View>
        );
    }

    const score = Number(params.score ?? 0);
    const total = Number(params.total ?? 0);
    const points = Number(params.points ?? 0);
    const accuracy = total > 0 ? Math.round((score / total) * 100) : 0;

    // For singleplayer show a simpler screen
    return (
        <View style={styles.container}>
            <Text style={styles.title}>🎮 Game Over!</Text>
            <Text style={styles.stats}>
                You got {score}/{total} correct ({accuracy}%)
            </Text>
            <Text style={styles.points}>
                Points: {points}
            </Text>

            <AppButton
                label="Play Again"
                onPress={() => {
                    // navigate to returnTo (if provided) or default to HigherOrLowerSingle
                    const target = returnTo ?? '/pages/HigherOrLowerSingle';
                    try { router.replace({ pathname: target } as any); } catch { router.push(target as any); }
                }}
                backgroundColor={colors.primary}
                style={styles.button}
            />
            <AppButton
                label="Back to Menu"
                onPress={() => router.replace({ pathname: "/pages/ChooseGame" })}
                backgroundColor={colors.primaryDark || "#666"}
                style={styles.button}
            />
        </View>
    );
}

const styles = StyleSheet.create({
    container: {
        flex: 1,
        backgroundColor: colors.background || "#151515",
        justifyContent: "center",
        alignItems: "center",
        padding: 20,
    },
    title: {
        fontSize: 32,
        fontWeight: "700",
        marginBottom: 20,
        color: colors.text || "#fff",
    },
    stats: {
        fontSize: 20,
        marginBottom: 8,
        color: colors.text || "#fff",
        textAlign: "center",
    },
    points: {
        fontSize: 18,
        marginBottom: 24,
        color: colors.text || "#fff",
    },
    button: {
        width: "80%",
        marginVertical: 10,
        paddingVertical: 16,
        borderRadius: 12,
    },
    leaderText: {
        fontSize: 18,
        color: colors.text || '#fff',
        marginVertical: 6,
    }
});
