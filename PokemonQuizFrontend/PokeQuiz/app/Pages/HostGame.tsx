import React, { useState, useEffect, useRef } from "react";
import {
    View,
    Text,
    StyleSheet,
    FlatList,
    Alert,
    TextInput,
    Platform,
} from "react-native";
import { colors } from "../../styles/colours";
import Navbar from "@/components/Navbar";
import AppButton from "@/components/AppButton";
import { useRouter } from "expo-router";
import * as SignalR from "@microsoft/signalr";

type Player = {
    name: string;
    isHost: boolean;
};

export default function HostGame() {
    const router = useRouter();
    const [roomCode, setRoomCode] = useState("");
    const [players, setPlayers] = useState<Player[]>([]);
    const [isConnected, setIsConnected] = useState(false);
    const [hostName, setHostName] = useState("");
    const [hasEnteredName, setHasEnteredName] = useState(false);
    const connectionRef = useRef<SignalR.HubConnection | null>(null);

    const createRoom = () => {
        if (!hostName.trim()) {
            Alert.alert("Error", "Please enter your name");
            return;
        }

        setHasEnteredName(true);

        const serverIp = Platform.OS === "android" ? "10.0.2.2" : "192.168.1.1";
        const hubUrl = `http://${serverIp}:5168/hubs/game`;

        const connection = new SignalR.HubConnectionBuilder()
            .withUrl(hubUrl, { transport: SignalR.HttpTransportType.WebSockets })
            .withAutomaticReconnect()
            .configureLogging(SignalR.LogLevel.Information)
            .build();

        // Host receives room code
        connection.on("RoomCreated", (serverRoomCode: string) => {
            setRoomCode(serverRoomCode);
            setIsConnected(true);
            setPlayers([{ name: hostName, isHost: true }]);
        });

        // Player joins dynamically
        connection.on("PlayerJoined", (playerName: string) => {
            setPlayers((prev) => {
                if (prev.some((p) => p.name === playerName)) return prev;
                return [...prev, { name: playerName, isHost: false }];
            });
        });

        // Player leaves
        connection.on("PlayerLeft", (playerName: string) => {
            setPlayers((prev) => prev.filter((p) => p.name !== playerName));
        });

        // Error
        connection.on("Error", (message: string) => {
            Alert.alert("Error", message);
        });

        // Connect and create room
        connection
            .start()
            .then(() => connection.invoke("CreateRoom", hostName))
            .catch((err) => {
                console.error("Connection Error:", err);
                Alert.alert("Connection Error", "Cannot connect to server.");
                setHasEnteredName(false);
            });

        connectionRef.current = connection;
    };

    // Cleanup on unmount
    useEffect(() => {
        return () => {
            if (connectionRef.current?.state === SignalR.HubConnectionState.Connected) {
                connectionRef.current.invoke("LeaveRoom", roomCode, hostName).catch(console.error);
                connectionRef.current.stop();
            }
        };
    }, [roomCode, hostName]);

    const handleSelectGame = () => {
        if (players.length < 2) {
            Alert.alert("Not Enough Players", "Wait for at least 2 players to join!");
            return;
        }

        router.push({
            pathname: "/pages/ChooseGame",
            params: { mode: "multiplayer", roomCode, isHost: "true" },
        });
    };

    if (!hasEnteredName) {
        return (
            <View style={styles.container}>
                <Navbar title="Host Game" />
                <View style={styles.content}>
                    <Text style={styles.title}>Enter Your Name</Text>
                    <TextInput
                        style={styles.nameInput}
                        placeholder="Your Name"
                        placeholderTextColor="#999"
                        value={hostName}
                        onChangeText={setHostName}
                        maxLength={20}
                        autoFocus
                    />
                    <AppButton label="Create Room" onPress={createRoom} />
                </View>
            </View>
        );
    }

    // Lobby
    return (
        <View style={styles.container}>
            <Navbar title="Game Lobby" />
            <View style={styles.content}>
                <Text style={styles.title}>Waiting for Players...</Text>

                {isConnected ? (
                    <>
                        <Text style={styles.roomCodeLabel}>Room Code</Text>
                        <Text style={styles.roomCode}>{roomCode}</Text>
                        <Text style={styles.shareText}>Share this code with friends!</Text>
                    </>
                ) : (
                    <Text style={styles.connecting}>Connecting...</Text>
                )}

                <View style={styles.playersSection}>
                    <Text style={styles.subtitle}>Players in Lobby ({players.length})</Text>

                    <FlatList
                        data={players}
                        keyExtractor={(item) => item.name}
                        renderItem={({ item }) => (
                            <View style={styles.playerCard}>
                                <Text style={styles.playerName}>
                                    {item.name}
                                    {item.isHost && " 👑"}
                                </Text>
                            </View>
                        )}
                    />
                </View>

                <AppButton
                    label={`Select Game (${players.length} players)`}
                    onPress={handleSelectGame}
                    disabled={!isConnected || players.length < 2}
                />
                <Text style={styles.hostNote}>As host, you'll select the game</Text>
            </View>
        </View>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, backgroundColor: colors.background || "#151515" },
    content: { flex: 1, justifyContent: "center", alignItems: "center", padding: 20 },
    title: { fontSize: 30, fontWeight: "800", marginBottom: 20 },
    nameInput: { width: "80%", padding: 15, borderRadius: 10, fontSize: 18, marginBottom: 20, textAlign: "center" },
    connecting: { fontSize: 18, opacity: 0.7, marginVertical: 20 },
    roomCodeLabel: { fontSize: 18, color: colors.white, marginBottom: 5, opacity: 0.8 },
    roomCode: { fontSize: 48, fontWeight: "900", color: colors.primary, marginBottom: 10, letterSpacing: 8 },
    shareText: { fontSize: 14, opacity: 0.6, marginBottom: 30 },
    playersSection: { width: "100%", marginBottom: 30, maxHeight: 300 },
    subtitle: { fontSize: 20, marginBottom: 15, textAlign: "center", fontWeight: "700" },
    playerCard: { backgroundColor: colors.primary || "#FF5252", padding: 15, borderRadius: 10, marginVertical: 5, width: "100%" },
    playerName: { fontSize: 16, textAlign: "center", fontWeight: "600" },
    hostNote: { fontSize: 12, opacity: 0.5, marginTop: 10 },
});
