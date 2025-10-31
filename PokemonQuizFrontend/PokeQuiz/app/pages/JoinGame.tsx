import React, { useState } from "react";
import { View, Text, StyleSheet, TextInput, Alert } from "react-native";
import { colors } from "../../styles/colours";
import Navbar from "@/components/Navbar";
import AppButton from "@/components/AppButton";
import { useRouter, useLocalSearchParams } from "expo-router";

export default function JoinGame() {
    const router = useRouter();
    const params = useLocalSearchParams();
    const roomCode = params.roomCode as string;

    const [playerName, setPlayerName] = useState("");

    const handleJoin = () => {
        if (!playerName.trim()) {
            Alert.alert("Error", "Please enter your name");
            return;
        }

        router.push({
            pathname: "/pages/WaitingRoom",
            params: { roomCode, playerName },
        });
    };

    return (
        <View style={styles.container}>
            <Navbar title="Join Room" />
            <View style={styles.content}>
                <Text style={styles.roomLabel}>ROOM: {roomCode}</Text>
                <Text style={styles.subtitle}>Enter your name to join</Text>
                <TextInput
                    style={styles.nameInput}
                    placeholder="Your Name"
                    placeholderTextColor="#999"
                    value={playerName}
                    onChangeText={setPlayerName}
                />
                <AppButton label="Join Room" onPress={handleJoin} />
            </View>
        </View>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, backgroundColor: "#ffffff" },
    content: { flex: 1, justifyContent: "center", alignItems: "center", padding: 20 },
    roomLabel: { fontSize: 36, fontWeight: "900", color: "#FF5252", marginBottom: 15 },
    subtitle: { fontSize: 18, color: "#666", marginBottom: 20 },
    nameInput: { width: "80%", padding: 15, borderRadius: 12, fontSize: 18, marginBottom: 20, textAlign: "center", borderWidth: 2, borderColor: "#FF5252" },
});
