import React, { useState } from "react";
import { View, Text, StyleSheet, TextInput, Alert } from "react-native";
import { colors } from "../../styles/colours";
import Navbar from "@/components/Navbar";
import AppButton from "@/components/AppButton";
import { useRouter } from "expo-router";

export default function MultiplayerSetup() {
    const router = useRouter();
    const [roomCode, setRoomCode] = useState("");
    const [error, setError] = useState("");

    const handleHostGame = () => {
        router.push("/pages/HostGame");
    };

    const handleJoinGame = async () => {
        if (!roomCode.trim()) {
            setError("Please enter a room code");
            return;
        }

        // Validate room code via API
        try {
            const res = await fetch(`http://localhost:5168/api/lobby/validate?code=${roomCode.toUpperCase()}`);
            const data = await res.json();
            if (!data.valid) {
                setError("Invalid or inactive room code");
                return;
            }

            // Go to JoinGame page with validated code
            router.push({
                pathname: "/pages/JoinGame",
                params: { roomCode: roomCode.toUpperCase() },
            });
        } catch (err) {
            console.error(err);
            setError("Error validating room code");
        }
    };

    return (
        <View style={styles.container}>
            <Navbar title="Multiplayer Setup" />
            <View style={styles.content}>
                <Text style={styles.title}>Join a Room</Text>
                <TextInput
                    style={styles.codeInput}
                    placeholder="Enter Room Code"
                    placeholderTextColor="#999"
                    value={roomCode}
                    onChangeText={(text) => {
                        setRoomCode(text.toUpperCase());
                        setError("");
                    }}
                    maxLength={6}
                    autoCapitalize="characters"
                />
                {error ? <Text style={styles.errorText}>{error}</Text> : null}
                <AppButton label="Join Room" onPress={handleJoinGame} style={styles.mainButton} />

                <Text style={styles.orText}>OR</Text>
                <AppButton label="Host a Room" onPress={handleHostGame} style={styles.hostButton} />
            </View>
        </View>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, backgroundColor: "#ffffff" },
    content: { flex: 1, justifyContent: "center", alignItems: "center", padding: 20 },
    title: { fontSize: 32, fontWeight: "800", marginBottom: 20, color: "#333" },
    codeInput: {
        width: "80%",
        height: 60,
        borderColor: "#FF5252",
        borderWidth: 2,
        borderRadius: 12,
        fontSize: 28,
        textAlign: "center",
        fontWeight: "700",
        letterSpacing: 6,
        color: "#000",
        marginBottom: 10,
        backgroundColor: "#fff",
    },
    errorText: { color: "#ef4444", marginBottom: 10 },
    mainButton: { width: "80%", marginVertical: 10 },
    orText: { fontSize: 16, marginVertical: 15, color: "#666" },
    hostButton: { width: "60%", backgroundColor: "#FFCDD2", marginVertical: 5 },
});
