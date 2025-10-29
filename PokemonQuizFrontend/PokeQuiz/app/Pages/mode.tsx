import React, { useRef } from "react";
import {
    View,
    Text,
    StyleSheet,
    Dimensions,
    Image,
    Animated,
    Platform,
    Pressable,
} from "react-native";
import { colors } from "../../styles/colours";
import Navbar from "@/components/Navbar";
import { router } from 'expo-router';

const { width: screenWidth } = Dimensions.get("window");

const singlePlayerImage = require("../../assets/images/charizard.png");
const multiplayerImage = require("../../assets/images/maushold.png");

export default function ModeScreen() {
    const singleScale = useRef(new Animated.Value(1)).current;
    const multiScale = useRef(new Animated.Value(1)).current;

    const handleHover = (scaleRef: Animated.Value, hovering: boolean) => {
        Animated.timing(scaleRef, {
            toValue: hovering ? 1.03 : 1,
            duration: 150,
            useNativeDriver: true,
        }).start();
    };

    const createHandlers = (scaleRef: Animated.Value, routeName: string) => {
        if (Platform.OS === "web") {
            return {
                onHoverIn: () => handleHover(scaleRef, true),
                onHoverOut: () => handleHover(scaleRef, false),
                onPress: () => router.push(routeName as any),
            };
        }
        return {
            onPressIn: () => handleHover(scaleRef, true),
            onPressOut: () => handleHover(scaleRef, false),
            onPress: () => router.push(routeName as any),
        };
    };

    return (
        <View style={styles.container}>
            <Navbar title="Select Mode" />

            <View style={styles.contentContainer}>
                <Text style={styles.title}>Choose Your Preferred Game Mode</Text>

                <View style={styles.modeButtonsContainer}>
                    {/* Single Player */}
                    <Pressable {...createHandlers(singleScale, "/pages/ChooseGame")}>
                        <Animated.View
                            style={[
                                styles.modeCard,
                                { transform: [{ scale: singleScale }] },
                                Platform.OS === "web"
                                    ? ({ cursor: "pointer", transition: "transform 0.2s ease-in-out" } as any)
                                    : {},
                            ]}
                        >
                            <Text style={styles.cardTitle}>Single Player</Text>
                            <Image source={singlePlayerImage} style={styles.cardImage} resizeMode="contain" />
                            <Text style={styles.cardDescription}>
                                Take quizzes, test your knowledge, and beat that high score!
                            </Text>
                        </Animated.View>
                    </Pressable>

                    {/* Multiplayer */}
                    <Pressable {...createHandlers(multiScale, "/pages/MultiplayerSetup")}>
                        <Animated.View
                            style={[
                                styles.modeCard,
                                { transform: [{ scale: multiScale }] },
                                Platform.OS === "web"
                                    ? ({ cursor: "pointer", transition: "transform 0.2s ease-in-out" } as any)
                                    : {},
                            ]}
                        >
                            <Text style={styles.cardTitle}>Multiplayer</Text>
                            <Image source={multiplayerImage} style={styles.cardImage} resizeMode="contain" />
                            <Text style={styles.cardDescription}>
                                Take quizzes as a group and see who becomes the ultimate trainer!
                            </Text>
                        </Animated.View>
                    </Pressable>
                </View>
            </View>
        </View>
    );
}

const styles = StyleSheet.create({
    container: {
        flex: 1,
        backgroundColor: colors.white,
    },
    contentContainer: {
        flex: 1,
        justifyContent: "center",
        alignItems: "center",
        paddingHorizontal: 20,
        paddingVertical: 30,
    },
    title: {
        fontSize: 32,
        fontWeight: "700",
        color: "#666",
        textAlign: "center",
        marginBottom: 60,
    },
    modeButtonsContainer: {
        flexDirection: screenWidth > 700 ? "row" : "column",
        justifyContent: "center",
        alignItems: "center",
        gap: 80,
    },
    modeCard: {
        width: screenWidth > 700 ? 400 : 300,
        aspectRatio: 0.95,
        backgroundColor: colors.primary || "#FF5252",
        borderRadius: 28,
        padding: 48,
        justifyContent: "space-between",
        alignItems: "center",
        shadowColor: "#000",
        shadowOpacity: 0.25,
        shadowRadius: 8,
        shadowOffset: { width: 0, height: 6 },
        elevation: 5,
    },
    cardTitle: {
        fontSize: 28,
        fontWeight: "700",
        color: colors.white,
        textAlign: "center",
    },
    cardImage: {
        width: 160,
        height: 160,
    },
    cardDescription: {
        fontSize: 15,
        color: colors.white,
        textAlign: "center",
        lineHeight: 22,
    },
});