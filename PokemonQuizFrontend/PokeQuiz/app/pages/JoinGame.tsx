import React, { useState, useEffect } from "react";
import { View, Text, StyleSheet, TextInput, Alert } from "react-native";
import { colors } from "../../styles/colours";
import Navbar from "@/components/Navbar";
import AppButton from "@/components/AppButton";
import { useRouter, useLocalSearchParams } from "expo-router";

const MAX_NAME_LENGTH = 24;

export default function JoinGame() {
    const router = useRouter();
    const params = useLocalSearchParams();
    const roomCode = params.roomCode as string;

    const [playerName, setPlayerName] = useState("");
    const [locked, setLocked] = useState(false);

    useEffect(() => {
        // If user is logged in, use their account username and lock the field
        const uname = (global as any).username as string | undefined;
        if (uname) {
            setPlayerName(uname.slice(0, MAX_NAME_LENGTH));
            setLocked(true);
        }
    }, []);

    const handleJoin = () => {
        const trimmed = playerName.trim();
        if (!trimmed) {
            Alert.alert("Error", "Please enter your name");
            return;
        }
        if (trimmed.length > MAX_NAME_LENGTH) {
            Alert.alert("Error", `Name too long. Max ${MAX_NAME_LENGTH} characters.`);
            return;
        }

        // If not logged in, redirect to Login and then return here
        const uname = (global as any).username as string | undefined;
        if (!uname) {
            // send user to login; after login they should be returned to this page
            router.push({ pathname: '/pages/Login', params: { returnTo: `/pages/JoinGame?roomCode=${roomCode}` } } as any);
            return;
        }

        router.push({
            pathname: "/pages/WaitingRoom",
            params: { roomCode, playerName: trimmed },
        });
    };

    return (
        <View style={styles.container}>
            <Navbar title="Join Room" />
            <View style={styles.content}>
                <Text style={styles.roomLabel}>ROOM: {roomCode}</Text>
                <Text style={styles.subtitle}>Enter your name to join</Text>
                <TextInput
                    style={[styles.nameInput, locked && { backgroundColor: '#eee' }]}
                    placeholder="Your Name"
                    placeholderTextColor="#999"
                    value={playerName}
                    onChangeText={(text) => { if (!locked) setPlayerName(text.slice(0, MAX_NAME_LENGTH)); }}
                    editable={!locked}
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
