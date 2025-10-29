import React, { useState } from "react";
import { View, Text, StyleSheet, TextInput, Alert } from "react-native";
import { colors } from "../../styles/colours";
import Navbar from "@/components/Navbar";
import AppButton from "@/components/AppButton";
import { useRouter } from "expo-router";

export default function MultiplayerSetup() {
    const router = useRouter();
    const [roomCode, setRoomCode] = useState("");

    const handleHostGame = () => {
        // Navigate to host game screen (where host enters their name)
        router.push("/pages/HostGame");
    };

    const handleJoinGame = () => {
        if (!roomCode.trim()) {
            Alert.alert("Error", "Please enter a room code to join a game.");
            return;
        }
        // Navigate to join game screen
        router.push({
            pathname: "/pages/JoinGame",
            params: { roomCode: roomCode.toUpperCase() }
        });
    };

    return (
        <View style={styles.container}>
            <Navbar title="Multiplayer Setup" />
            <View style={styles.content}>
                <Text style={styles.title}>Multiplayer Mode</Text>
                <Text style={styles.subtitle}>Host or Join a Game</Text>

                <AppButton
                    label="🎮 Host Game"
                    onPress={handleHostGame}
                    style={styles.button}
                />

                <View style={styles.divider} />

                <View style={styles.joinContainer}>
                    <Text style={styles.joinLabel}>Join an existing game:</Text>
                    <TextInput
                        style={styles.input}
                        placeholder="Enter Room Code"
                        placeholderTextColor="#999"
                        onChangeText={(text) => setRoomCode(text.toUpperCase())}
                        value={roomCode}
                        maxLength={6}
                        autoCapitalize="characters"
                    />

                    <AppButton
                        label="👥 Join Game"
                        onPress={handleJoinGame}
                        style={styles.button}
                    />
                </View>
            </View>
        </View>
    );
}

const styles = StyleSheet.create({
    container: {
        flex: 1,
        backgroundColor: colors.background || "#151515",
    },
    content: {
        flex: 1,
        justifyContent: "center",
        alignItems: "center",
        padding: 20,
    },
    title: {
        fontSize: 32,
        fontWeight: "800",

        marginBottom: 10,
    },
    subtitle: {
        fontSize: 18,

        marginBottom: 40,
        textAlign: "center",
        opacity: 0.8,
    },
    button: {
        width: "80%",
        marginVertical: 10,
    },
    divider: {
        width: "80%",
        height: 1,
        backgroundColor: "#444",
        marginVertical: 30,
    },
    joinContainer: {
        width: "100%",
        alignItems: "center",
    },
    joinLabel: {
        fontSize: 16,

        marginBottom: 15,
        opacity: 0.8,
    },
    input: {
        width: "80%",
        height: 50,
        borderColor: "#444",
        borderWidth: 2,
        borderRadius: 10,
        paddingHorizontal: 15,
        color: colors.white,
        marginBottom: 15,
        fontSize: 20,
        backgroundColor: "#252525",
        textAlign: "center",
        fontWeight: "700",
        letterSpacing: 2,
    },
});