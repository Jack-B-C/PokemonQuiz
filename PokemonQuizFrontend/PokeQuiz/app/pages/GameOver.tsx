import React from "react";
import { View, Text, StyleSheet } from "react-native";
import { colors } from "../../styles/colours";
import Navbar from "@/components/Navbar";
import AppButton from "@/components/AppButton";
import { useRouter, useLocalSearchParams } from "expo-router";

export default function GameOver() {
    const router = useRouter();
    const params = useLocalSearchParams();

    // back handler to prevent going back into completed game
    const handleBack = () => {
        router.replace({ pathname: "/pages/ChooseGame" } as any);
    };

    // If leaderboard param provided (multiplayer), it will be a JSON string
    const leaderboardJson = params.leaderboard as string | undefined;

    if (leaderboardJson) {
        let leaderboard: { Name?: string; name?: string; Score?: number; score?: number }[] = [];
        try {
            leaderboard = JSON.parse(leaderboardJson);
        } catch (e) {
            console.warn('Failed to parse leaderboard', e);
        }

        return (
            <View style={styles.container}>
                <Navbar title="Game Over" onBack={handleBack} />
                <Text style={styles.title}>🏆 Lobby Leaderboard</Text>
                <View style={{ width: '100%', paddingHorizontal: 20 }}>
                    {leaderboard.map((p, i) => (
                        <Text key={i} style={styles.leaderText}>{i + 1}. {p.Name ?? p.name}: {p.Score ?? p.score}</Text>
                    ))}
                </View>

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
    const accuracy = total > 0 ? Math.round((score / total) * 100) : 0;

    return (
        <View style={styles.container}>
            <Navbar title="Game Over" onBack={handleBack} />
            <Text style={styles.title}>🎮 Game Over!</Text>
            <Text style={styles.stats}>
                You got {score}/{total} correct ({accuracy}%)
            </Text>

            <AppButton
                label="Play Again"
                onPress={() => router.replace({ pathname: "/pages/GuessStat" })}
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
        marginBottom: 40,
        color: colors.text || "#fff",
        textAlign: "center",
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
