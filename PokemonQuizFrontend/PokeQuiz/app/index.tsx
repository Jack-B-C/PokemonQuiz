// app/index.tsx
import React, { useRef, useEffect, useState } from "react";
import { View, Text, StyleSheet, Animated, Dimensions, TouchableOpacity } from "react-native";
import { colors } from "../styles/colours";
import Icon from "react-native-vector-icons/Ionicons";
import Navbar from "../components/Navbar";
import { useRouter } from "expo-router";

const { height: screenHeight } = Dimensions.get("window");

export default function HomeScreen() {
    const router = useRouter();
    const pulseAnim = useRef(new Animated.Value(1)).current;

    useEffect(() => {
        Animated.loop(
            Animated.sequence([
                Animated.timing(pulseAnim, {
                    toValue: 1.1,
                    duration: 800,
                    useNativeDriver: true,
                }),
                Animated.timing(pulseAnim, {
                    toValue: 1,
                    duration: 800,
                    useNativeDriver: true,
                }),
            ])
        ).start();
    }, [pulseAnim]);

    return (
        <View style={styles.safeArea}>
            <Navbar title="PokéQuiz Home" />

            <View style={styles.container}>
                <View style={styles.topHalf}>
                    <Text style={styles.title}>PokéQuiz</Text>
                </View>

                <View style={styles.bottomHalf}>
                    <Text style={styles.subtitle}>Test your Pokémon knowledge!</Text>
                </View>

                <View style={styles.dividerLine} />

                <Animated.View style={[styles.playButtonWrapper, { transform: [{ scale: pulseAnim }] }]}>
                    <TouchableOpacity
                        style={styles.circleButton}
                        onPress={() => router.push("/pages/mode")}
                        activeOpacity={0.8}
                    >
                        <Text style={styles.playButtonText}>Play</Text>
                    </TouchableOpacity>
                </Animated.View>

                <TouchableOpacity
                    style={styles.loginButton}
                    activeOpacity={0.7}
                    onPress={() => router.push("/pages/mode")}
                >
                    <Icon name="person-circle-outline" size={40} color={colors.primary} />
                </TouchableOpacity>

                <TouchableOpacity
                    style={styles.leaderboardButton}
                    activeOpacity={0.7}
                    onPress={() => router.push("/pages/mode")}
                >
                    <Icon name="bar-chart-outline" size={40} color={colors.white} />
                </TouchableOpacity>
            </View>
        </View>
    );
}

const styles = StyleSheet.create({
    safeArea: {
        flex: 1,
        backgroundColor: colors.surface,
    },
    container: {
        flex: 1,
        backgroundColor: colors.surface,
        alignItems: "center",
    },
    topHalf: {
        width: "100%",
        height: screenHeight * 0.5,
        backgroundColor: colors.primary,
        justifyContent: "center",
        alignItems: "center",
        paddingTop: 40,
    },
    bottomHalf: {
        width: "100%",
        flex: 1,
        backgroundColor: colors.surface,
        justifyContent: "center",
        alignItems: "center",
        paddingBottom: 40,
    },
    dividerLine: {
        position: "absolute",
        top: screenHeight * 0.5,
        left: 0,
        right: 0,
        height: 20,
        backgroundColor: "black",
    },
    title: {
        fontSize: 76,
        fontWeight: "bold",
        color: colors.white,
        textAlign: "center",
    },
    subtitle: {
        fontSize: 24,
        color: colors.text,
        textAlign: "center",
        paddingHorizontal: 20,
    },
    playButtonWrapper: {
        position: "absolute",
        top: screenHeight * 0.5 - 95,
        zIndex: 10,
    },
    circleButton: {
        width: 190,
        height: 190,
        borderRadius: 95,
        backgroundColor: colors.surface,
        borderWidth: 12,
        borderColor: "black",
        justifyContent: "center",
        alignItems: "center",
        shadowColor: "#000",
        shadowOffset: { width: 0, height: 5 },
        shadowOpacity: 0.3,
        shadowRadius: 10,
        elevation: 10,
    },
    playButtonText: {
        fontSize: 40,
        fontWeight: "bold",
        color: "black",
    },
    loginButton: {
        position: "absolute",
        top: 30,
        right: 30,
        width: 80,
        height: 80,
        borderRadius: 40,
        backgroundColor: colors.white,
        justifyContent: "center",
        alignItems: "center",
        shadowColor: "#000",
        shadowOffset: { width: 0, height: 2 },
        shadowOpacity: 0.25,
        shadowRadius: 6,
        elevation: 5,
    },
    leaderboardButton: {
        position: "absolute",
        bottom: 30,
        right: 30,
        width: 80,
        height: 80,
        borderRadius: 40,
        backgroundColor: colors.primary,
        justifyContent: "center",
        alignItems: "center",
        shadowColor: "#000",
        shadowOffset: { width: 0, height: 2 },
        shadowOpacity: 0.25,
        shadowRadius: 6,
        elevation: 5,
    },
});
