import React, { useRef } from 'react';
import { View, Text, StyleSheet, TouchableOpacity, Image, Platform, Pressable, Animated,  } from 'react-native';

import { useRouter, useLocalSearchParams } from 'expo-router';
import { colors } from '../../styles/colours';
import Navbar from '@/components/Navbar';

export default function ChooseGame() {
    const router = useRouter();
    const params = useLocalSearchParams();
    const isMultiplayer = params.mode === 'multiplayer';
    const roomCode = params.roomCode as string;

    const games = [
        { id: 'guess-type', name: 'Guess the Type', image: require('../../assets/images/charizard.png') },
        { id: 'guess-stats', name: 'Guess the Stats', image: require('../../assets/images/charizard.png') },
        { id: 'who-that-pokemon', name: "Who's That Pokémon?", image: require('../../assets/images/charizard.png') },
    ];

    const handleGameSelect = (gameId: string) => {
        if (isMultiplayer && roomCode) {
            // Go to waiting room with game selected
            router.push({
                pathname: '/pages/WaitingRoom',
                params: { gameId, roomCode }
            });
        } else {
            // Single player - start game immediately
            router.push({
                pathname: '/pages/ChooseGame',
                params: { gameId, mode: 'single' }
            });
        }
    };

    return (
        <View style={styles.container}>
            <Navbar title={isMultiplayer ? "Choose Game (Room: " + roomCode + ")" : "Choose Game"} />

            <View style={styles.content}>
                <Text style={styles.subtitle}>
                    {isMultiplayer ? 'Select a game for your room' : 'Select a game to play'}
                </Text>

                <View style={styles.gamesContainer}>
                    {games.map((game) => (
                        <TouchableOpacity
                            key={game.id}
                            style={styles.gameCard}
                            onPress={() => handleGameSelect(game.id)}
                            activeOpacity={0.8}
                        >
                            <Image source={game.image} style={styles.gameImage} resizeMode="contain" />
                            <Text style={styles.gameName}>{game.name}</Text>
                        </TouchableOpacity>
                    ))}
                </View>
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
        padding: 20,
        alignItems: 'center',
    },
    subtitle: {
        fontSize: 20,
        color: colors.white,
        marginVertical: 30,
        textAlign: 'center',
        fontWeight: '600',
    },
    gamesContainer: {
        flexDirection: 'row',
        flexWrap: 'wrap',
        justifyContent: 'center',
        gap: 20,
        width: '100%',
    },
    gameCard: {
        width: 280,
        backgroundColor: colors.primary || '#FF5252',
        borderRadius: 20,
        padding: 20,
        alignItems: 'center',
        shadowColor: '#000',
        shadowOpacity: 0.25,
        shadowRadius: 8,
        shadowOffset: { width: 0, height: 4 },
        elevation: 5,
    },
    gameImage: {
        width: 150,
        height: 150,
        marginBottom: 15,
    },
    gameName: {
        fontSize: 18,
        fontWeight: '700',
        color: colors.white,
        textAlign: 'center',
    },
});