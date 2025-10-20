import react, { useState } from "react";
import { View, Text, StyleSheet, TextInput } from "react-native";
import { colors } from "../../styles/colours";
import Navbar from "@/components/Navbar";
import AppButton from "@/components/AppButton";

export default function MultiplayerSetup(){
    const navigation = useNavigation();
    const [roomCode, setRoomCode] = useState("");

    const handleHostGame = () => {
        // Logic to host a game
        navigation.navigate("HostGame");
    };
    const handleJoinGame = () => {
        // Logic to join a game with the provided room code
        if(!roomCode) {
            alert("Please enter a room code to join a game.");
            return;
        }   
        navigation.navigate("JoinGame", { roomCode });
    };
    return (
        <View style={styles.container}>
            <Navbar title="Multiplayer Setup" navigation={navigation} />
            <View style={styles.content}>
                <Text style={styles.subtitle}>Host or Join a Multiplayer Game</Text>

                <AppButton
                    label="Host Game"
                    onPress={handleHostGame}
                    backgroundColor='#3d52d5'
                    textColor='#fff'
                    styles={styles.button}
                />

                <View style={styles.joinContainer}>
                    <TextInput
                        style={styles.input}
                        placeholder="Enter Room Code"
                        placeholderTextColor={colors.gray}
                        onChangeText={setRoomCode}
                        value={roomCode}
                    />
                    
                    <AppButton
                        label="Join Game"
                        onPress={handleJoinGame}
                        backgroundColor='#3d52d5'
                        textColor='#fff'
                        styles={styles.button}
                    />
                </View>
            </View>
        </View>
    );
}
const styles = StyleSheet.create({
    container: {
        flex: 1,
        backgroundColor: '#151515',
    },
    content: {
        flex: 1,
        justifyContent: 'center',
        alignItems: 'center',
        padding: 20,
    },
    subtitle: {
        fontSize: 18,
        color: colors.white,
        marginBottom: 20,
        textAlign: 'center',
    },
    button: {
        width: '80%',
        marginVertical: 10,
    },
    joinContainer: {
        width: '100%',
        alignItems: 'center',
        marginTop: 30,
    },
    input: {
        width: '80%',
        height: 50,
        borderColor: colors.gray,
        borderWidth: 1,
        borderRadius: 8,
        paddingHorizontal: 10,
        color: colors.white,
        marginBottom: 15,
        fontSize: 16,
        backgroundColor: '#252525',
    },
});