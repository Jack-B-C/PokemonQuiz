import React, { useState, useEffect, useRef } from "react";
import { View, Text, StyleSheet, FlatList, Alert } from "react-native";
import { colors } from "../../styles/colours";
import Navbar from "@/components/Navbar";
import { useRouter, useLocalSearchParams } from "expo-router";
import * as SignalR from "@microsoft/signalr";

type Player = {
    name: string;
    isHost: boolean;
};

export default function WaitingRoom() {
    const router = useRouter();
    const params = useLocalSearchParams();
    const roomCode = params.roomCode as string;
    const playerName = params.playerName as string;

    const [players, setPlayers] = useState<Player[]>([]);
    const [isConnected, setIsConnected] = useState(false);
    const [hostName, setHostName] = useState("");
    const connectionRef = useRef<SignalR.HubConnection | null>(null);

    useEffect(() => {
        const connection = new SignalR.HubConnectionBuilder()
            .withUrl("http://localhost:5000/hubs/game", {
                skipNegotiation: true,
                transport: SignalR.HttpTransportType.WebSockets,
            })
            .withAutomaticReconnect()
            .configureLogging(SignalR.LogLevel.Information)
            .build();

        // Listen for successful room join
        connection.on("RoomJoined", (data: any) => {
            console.log("Joined room:", data);
            setHostName(data.hostName);
            setPlayers(data.players);
            setIsConnected(true);
        });

        // Listen for players joining/leaving
        connection.on("PlayerJoined", (data: any) => {
            console.log("Player joined:", data);
            setPlayers(data.players);
        });

        connection.on("PlayerLeft", (data: any) => {
            console.log("Player left:", data);
            setPlayers(data.players);
        });

        // Listen for new host
        connection.on("NewHost", (newHostName: string) => {
            setHostName(newHostName);
            Alert.alert("New Host", `${newHostName} is now the host`);
        });

        // Listen for game selection (host chose a game)
        connection.on("GameSelected", (gameId: string) => {
            console.log("Game selected:", gameId);
            // All players navigate to game
            router.push({
                pathname: "/pages/ChooseGame",
                params: { gameId, roomCode, playerName, isHost: "false" },
            });
        });

        // Listen for errors
        connection.on("Error", (message: string) => {
            console.error("SignalR Error:", message);
            Alert.alert("Error", message);
            router.back();
        });

        // Connect and join room
        connection
            .start()
            .then(() => {
                console.log("SignalR Connected");
                return connection.invoke("JoinRoom", roomCode, playerName);
            })
            .catch((err) => {
                console.error("SignalR Connection Error:", err);
                Alert.alert("Connection Error", "Failed to join room");
                router.back();
            });

        connectionRef.current = connection;

        // Cleanup
        return () => {
            if (connection.state === SignalR.HubConnectionState.Connected) {
                connection.invoke("LeaveRoom").catch(console.error);
                connection.stop();
            }
        };
    }, [roomCode, playerName]);

    return (
        <View style={styles.container}>
            <Navbar title="Waiting Room" />
            <View style={styles.content}>
                <Text style={styles.title}>Room: {roomCode}</Text>

                {isConnected ? (
                    <>
                        <Text style={styles.subtitle}>
                            Waiting for {hostName} to start...
                        </Text>

                        <View style={styles.playersSection}>
                            <Text style={styles.playersTitle}>
                                Players ({players.length})
                            </Text>

                            <FlatList
                                data={players}
                                keyExtractor={(item, index) => index.toString()}
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
                    </>
                ) : (
                    <Text style={styles.connecting}>Joining room...</Text>
                )}
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
        fontSize: 36,
        fontWeight: "900",
        color: colors.primary,
        marginBottom: 20,
        letterSpacing: 4,
    },
    subtitle: {
        fontSize: 18,
        color: colors.white,
        marginBottom: 40,
        textAlign: "center",
        opacity: 0.8,
    },
    connecting: {
        fontSize: 18,
        color: colors.white,
        opacity: 0.7,
    },
    playersSection: {
        width: "100%",
        maxHeight: 400,
    },
    playersTitle: {
        fontSize: 20,
        color: colors.white,
        marginBottom: 15,
        textAlign: "center",
        fontWeight: "700",
    },
    playerCard: {
        backgroundColor: colors.primary || "#FF5252",
        padding: 15,
        borderRadius: 10,
        marginVertical: 5,
    },
    playerName: {
        fontSize: 16,
        color: colors.white,
        textAlign: "center",
        fontWeight: "600",
    },
});