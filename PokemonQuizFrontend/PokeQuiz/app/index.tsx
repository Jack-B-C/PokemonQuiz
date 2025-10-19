import React, { useRef, useEffect, useState } from "react";
import { View, Text, StyleSheet, Animated, Dimensions, TouchableOpacity } from "react-native";
import { colors } from "../styles/colours";
import Icon from 'react-native-vector-icons/Ionicons';

// Get the screen width and height (used for responsive layout)
const { height: screenHeight, width: screenWidth } = Dimensions.get('window');

export default function HomeScreen({ navigation }: any) {
    // Animation reference for pulsating "Play" button
    const pulseAnim = useRef(new Animated.Value(1)).current;

    // States for potential hover effects (not yet used in mobile)
    const [loginHovered, setLoginHovered] = useState(false);
    const [leaderboardHovered, setLeaderboardHovered] = useState(false);

    // Create a looped animation effect to make the Play button gently pulse
    useEffect(() => {
        Animated.loop(
            Animated.sequence([
                Animated.timing(pulseAnim, {
                    toValue: 1.1,      // slightly scale up
                    duration: 800,     // time to grow
                    useNativeDriver: true,
                }),
                Animated.timing(pulseAnim, {
                    toValue: 1,        // scale back down
                    duration: 800,     // time to shrink
                    useNativeDriver: true,
                }),
            ])
        ).start(); // Start looping the animation
    }, [pulseAnim]);

    return (
        <View style={styles.safeArea}>
            <View style={styles.container}>

                {/* ---------- Top Red Half ---------- */}
                <View style={styles.topHalf}>
                    <Text style={styles.title}>PokéQuiz</Text>
                </View>

                {/* ---------- Bottom White Half ---------- */}
                <View style={styles.bottomHalf}>
                    <Text style={styles.subtitle}>Test your Pokemon Knowledge!</Text>
                </View>

                {/* ---------- Black Divider Line Between Halves ---------- */}
                <View style={styles.dividerLine} />

                {/* ---------- Central Play Button ---------- */}
                <Animated.View
                    style={[styles.playButtonWrapper, { transform: [{ scale: pulseAnim }] }]}
                >
                    <TouchableOpacity
                        style={styles.circleButton}
                        onPress={() => navigation.navigate("Game")} // Navigate to Game screen
                        activeOpacity={0.8}
                    >
                        <Text style={styles.playButtonText}>Play</Text>
                    </TouchableOpacity>
                </Animated.View>

                {/* ---------- Top Right Login Button ---------- */}
                <TouchableOpacity
                    style={styles.loginButton}
                    activeOpacity={0.7}
                >
                    <Icon
                        name="person-circle-outline"
                        size={40}
                        color={colors.primary || '#F44336'}
                    />
                </TouchableOpacity>

                {/* ---------- Bottom Right Leaderboard Button ---------- */}
                <TouchableOpacity
                    style={styles.leaderboardButton}
                    activeOpacity={0.7}
                >
                    <Icon
                        name="bar-chart-outline"
                        size={40}
                        color="white"
                    />
                </TouchableOpacity>
            </View>
        </View>
    );
}

const styles = StyleSheet.create({
    // Main background area (covers the whole screen)
    safeArea: {
        flex: 1,
        backgroundColor: 'white',
    },
    // Root container for all elements
    container: {
        flex: 1,
        backgroundColor: 'white',
        alignItems: "center",
    },
    // Top red half of the screen
    topHalf: {
        width: '100%',
        height: screenHeight * 0.5,
        backgroundColor: colors.primary || '#F44336',
        justifyContent: "center",
        alignItems: "center",
        paddingTop: 40,
    },
    // Bottom white half of the screen
    bottomHalf: {
        width: '100%',
        flex: 1,
        backgroundColor: 'white',
        justifyContent: "center",
        alignItems: "center",
        paddingBottom: 40,
    },
    // Black divider line in the middle
    dividerLine: {
        position: 'absolute',
        top: screenHeight * 0.5,
        left: 0,
        right: 0,
        height: 20,
        backgroundColor: 'black',
    },
    // Main title (PokéQuiz)
    title: {
        fontSize: 76,
        fontWeight: "bold",
        color: 'white',
        marginBottom: 16,
        textAlign: 'center',
    },
    // Subtitle below divider line
    subtitle: {
        fontSize: 24,
        color: '#333',
        textAlign: 'center',
        paddingHorizontal: 20,
    },
    // Container for Play button
    playButtonWrapper: {
        position: 'absolute',
        top: screenHeight * 0.5 - 95, // Centers the button on divider
        zIndex: 10,
    },
    // Circular Play button styling
    circleButton: {
        width: 190,
        height: 190,
        borderRadius: 95,
        backgroundColor: 'white',
        borderWidth: 12,
        borderColor: 'black',
        justifyContent: 'center',
        alignItems: 'center',
        shadowColor: "#000",
        shadowOffset: { width: 0, height: 5 },
        shadowOpacity: 0.3,
        shadowRadius: 10,
        elevation: 10, // Android shadow
    },
    // Text inside Play button
    playButtonText: {
        fontSize: 40,
        fontWeight: 'bold',
        color: 'black',
        textTransform: 'none',
    },
    // Top-right Login button
    loginButton: {
        position: 'absolute',
        top: 30,
        right: 30,
        width: 80,
        height: 80,
        borderRadius: 40,
        backgroundColor: 'white',
        justifyContent: 'center',
        alignItems: 'center',
        shadowColor: "#000",
        shadowOffset: { width: 0, height: 2 },
        shadowOpacity: 0.25,
        shadowRadius: 6,
        elevation: 5,
    },
    // Bottom-right Leaderboard button
    leaderboardButton: {
        position: 'absolute',
        bottom: 30,
        right: 30,
        width: 80,
        height: 80,
        borderRadius: 40,
        backgroundColor: colors.primary || '#F44336',
        justifyContent: 'center',
        alignItems: 'center',
        shadowColor: "#000",
        shadowOffset: { width: 0, height: 2 },
        shadowOpacity: 0.25,
        shadowRadius: 6,
        elevation: 5,
    },
});
