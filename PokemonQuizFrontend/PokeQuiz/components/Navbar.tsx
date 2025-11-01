import React from "react";
import { View, Text, StyleSheet, TouchableOpacity } from "react-native";
import { colors } from "../styles/colours";
import Icon from "react-native-vector-icons/Ionicons";
import { router } from 'expo-router';

type Props = {
    title: string;
    onBack?: () => void;
};

export default function Navbar({ title, onBack }: Props) {
    const handleBack = () => {
        if (onBack) {
            onBack();
            return;
        }

        // Default: go back in history
        try {
            router.back();
        } catch {
            // Fallback to root if back fails
            router.push('/');
        }
    };

    return (
        <View style={styles.navbar}>
            <TouchableOpacity onPress={handleBack} style={styles.backButton}>
                <Icon name="arrow-back-outline" size={26} color={colors.white} />
            </TouchableOpacity>

            <Text style={styles.title}>{title}</Text>

            <View style={{ width: 30 }} />
        </View>
    );
}

const styles = StyleSheet.create({
    navbar: {
        width: "100%",
        backgroundColor: colors.primary,
        flexDirection: "row",
        alignItems: "center",
        justifyContent: "space-between",
        paddingVertical: 14,
        paddingHorizontal: 20,
        elevation: 4,
    },
    backButton: {
        padding: 6,
    },
    title: {
        color: colors.white,
        fontSize: 20,
        fontWeight: "bold",
        textAlign: "center",
        flex: 1,
    },
});