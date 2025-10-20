import React from "react";
import { View, Text, StyleSheet, TouchableOpacity, Dimensions } from "react-native";
import { colors } from "../../styles/colours";
import Icon from 'react-native-vector-icons/Ionicons';
import Navbar from "@/components/Navbar";

// Get the screen width and height (used for responsive layout)
const { height: screenHeight, width: screenWidth } = Dimensions.get('window');

export default function ModeScreen({ navigation }: any) {
    return (
            <View style={styles.container}>
                <View styles={Styles.header}>
                    <Text style={styles.title}>Select Mode</Text>
                    <Text style={styles.subtitle}>Choose your preferred game mode</Text>
                </View>
                

                {/* ---------- Mode Selection Buttons ---------- */}
                <View style={styles.modeButtonsContainer}> 
                    <TouchableOpacity
                        style={styles.modeButton}
                        onPress={() => navigation.navigate("SinglePlayer")} // Navigate to Single Player screen
                        activeOpacity={0.8}
                    >
                        <Text style={styles.modeButtonText}>Single Player</Text>
                        <Icon name="person-circle-outline" size={40} color={colors.white} />
                    </TouchableOpacity>
                    <TouchableOpacity
                        style={styles.modeButton}
                        onPress={() => navigation.navigate("Multiplayer")} // Navigate to Multiplayer screen
                        activeOpacity={0.8}
                    >
                        <Text style={styles.modeButtonText}>Multiplayer</Text>
                        <Icon name="people-circle-outline" size={40} color={colors.white} />
                    </TouchableOpacity>
                </View>
            </View>
    );
}


