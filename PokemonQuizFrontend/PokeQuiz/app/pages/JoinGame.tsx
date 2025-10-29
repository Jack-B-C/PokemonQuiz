import React, { useState } from "react";
import { View, Text, StyleSheet, TextInput, Alert } from "react-native";
import { colors } from "../../styles/colours";
import Navbar from "@/components/Navbar";
import AppButton from "@/components/AppButton";
import { useRouter, useLocalSearchParams } from "expo-router";

export default function JoinGame() {
    const router = useRouter();
    const params = useLocalSearchParams();
    const roomCodeFromParams = params.roomCode as string || "";

    const [roomCode, setRoomCode] = useState(roomCodeFromParams);
    const [playerName, setPlayerName] = useState("");

    const handleJoin = () => {
        if (!roomCode.trim()) {
            Alert.alert("Error", "Please enter a room code");
            return;
        }
        if (!playerName.trim()) {
            Alert.alert("Error", "Please enter your name");
            return;
        }

        // Navigate to waiting room
        router.push({
            pathname: "/pages/WaitingRoom",
            params: {
                roomCode: roomCode.toUpperCase(),
                playerName: playerName.trim()
            }
        });
    };

    return (
        <View style={styles.container}>
            <Navbar title="Join Game" />
            <View style={styles.content}>
                <Text style={styles.title}>Join a Game</Text>

                <TextInput
                    style={styles.input}
                    placeholder="Room Code"
                    placeholderTextColor="#999"
                    value={roomCode}
                    onChangeText={(text) => setRoomCode(text.toUpperCase())}
                    maxLength={6}
                    autoCapitalize="characters"
                />

                <TextInput
                    style={styles.input}
                    placeholder="Your Name"
                    placeholderTextColor="#999"
                    value={playerName}
                    onChangeText={setPlayerName}
                    maxLength={20}
                />

                <AppButton label="Join Room" onPress={handleJoin} />
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
        fontSize: 30,
        fontWeight: "800",
        color: colors.white,
        marginBottom: 40,
    },
    input: {
        width: "80%",
        backgroundColor: colors.white,
        padding: 15,
        borderRadius: 10,
        fontSize: 18,
        marginBottom: 20,
        textAlign: "center",
    },
});