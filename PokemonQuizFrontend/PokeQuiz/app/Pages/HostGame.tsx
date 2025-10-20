import React, { useState } from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { colors } from '../../styles/colours';
import Navbar from '@/components/Navbar';
import AppButton from '@/components/AppButton';
import { useNavigation } from '@react-navigation/native';

const samplePlayers = [
    { id: 1, name: 'Player1' },
    { id: 2, name: 'Player2' },
]
export default function HostGame() {
    const navigation = useNavigation();
    const [roomCode, setRoomCode] = useState('');
    const [players, setPlayers] = useState(samplePlayers);
    
    useEffect(() => {
        // Logic to generate a unique room code
        const generatedCode = Math.random().toString(36).substring(2, 8).toUpperCase();
        setRoomCode(generatedCode);
    }, []);
    return (
        <View style={styles.container}>
            <Navbar title="Host Game" navigation={navigation} />
            <View style={styles.content}>
                <Text style={styles.subtitle}>Room Code: {roomCode}</Text>
                <Text style={styles.subtitle}>Players Joined:</Text>
                {players.map((player) => (
                    <Text key={player.id} style={styles.playerName}>{player.name}</Text>
                ))}
                <AppButton
                    label="Start Game"
                    onPress={() => navigation.navigate('GameScreen', { roomCode })}
                    
                />
            </View>
        </View>
    );
}
const styles = StyleSheet.create({
    container: {
        flex: 1,
        backgroundColor: colors.background || '#151515',
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
        marginBottom: 10,
        textAlign: 'center',
    },
    playerName: {
        fontSize: 16,
        color: colors.white,
        marginVertical: 5,
    },
});

    
