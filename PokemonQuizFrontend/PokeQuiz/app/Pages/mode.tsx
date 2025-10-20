import React from "react";
import { View, Text, StyleSheet, TouchableOpacity, Dimensions } from "react-native";
import { colors } from "../../styles/colours";
import Icon from 'react-native-vector-icons/Ionicons';
import Navbar from "@/components/Navbar";

const { height: screenHeight, width: screenWidth } = Dimensions.get('window');

export default function ModeScreen({ navigation }: any) {
    return (
        <View style={styles.container}>
            {/* Navbar */}
            <Navbar title="Select mode" navigation={navigation} />

            {/* Main Content */}
            <View style={styles.contentContainer}>
                {/* Subtitle */}
                <Text style={styles.subtitle}>Choose your preferred game mode</Text>

                {/* Mode Selection Buttons */}
                <View style={styles.modeButtonsContainer}>
                    {/* Single Player Card */}
                    <TouchableOpacity
                        style={styles.modeCard}
                        onPress={() => navigation.navigate("SinglePlayer")}
                        activeOpacity={0.85}
                    >
                        <Text style={styles.cardTitle}>Single Player</Text>
                        <View style={styles.cardIconContainer}>
                            <Icon name="person-circle-outline" size={50} color={colors.white} />
                        </View>
                        <Text style={styles.cardDescription}>
                            Take quizzes, test your knowledge, and beat that high score!
                        </Text>
                    </TouchableOpacity>

                    {/* Multiplayer Card */}
                    <TouchableOpacity
                        style={styles.modeCard}
                        onPress={() => navigation.navigate("Multiplayer")}
                        activeOpacity={0.85}
                    >
                        <Text style={styles.cardTitle}>Multiplayer</Text>
                        <View style={styles.cardIconContainer}>
                            <Icon name="people-circle-outline" size={48} color={colors.white} />
                        </View>
                        <Text style={styles.cardDescription}>
                            Take quizzes as a group, as a competitive challenge. Who will get the highest score?
                        </Text>
                    </TouchableOpacity>
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

    /* Content Container */
    contentContainer: {
        flex: 1,
        paddingHorizontal: 20,
        paddingVertical: 40,
        justifyContent: "center",
        alignItems: "center",
    },

    subtitle: {
        fontSize: 16,
        color: "#999",
        textAlign: "center",
        marginBottom: 40,
        fontWeight: "500",
    },

    /* Mode Buttons Container */
    modeButtonsContainer: {
        flexDirection: "row",
        gap: 32,
        width: "100%",
        maxWidth: 700,
    },

    /* Mode Card Styles */
    modeCard: {
        flex: 1,
        aspectRatio: 0.85,
        backgroundColor: colors.primary || "#FF5252",
        borderRadius: 24,
        padding: 32,
        justifyContent: "space-between",
        alignItems: "center",
        boxShadow: "0px 6px 12px rgba(0, 0, 0, 0.2)",
    },

    cardTitle: {
        fontSize: 28,
        fontWeight: "700",
        color: colors.white,
        textAlign: "center",
    },

    cardIconContainer: {
        alignItems: "center",
        justifyContent: "center",
        marginVertical: 8,
    },

    cardDescription: {
        fontSize: 15,
        color: colors.white,
        textAlign: "center",
        lineHeight: 22,
    },
});