import React from "react";
import { View, Text, StyleSheet, TouchableOpacity, Platform, StatusBar } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { colors } from "../styles/colours.js";
import Icon from "react-native-vector-icons/Ionicons";
import { router } from 'expo-router';

export default function Navbar({ title, onBack, backTo }) {
    const handleBack = () => {
        if (onBack) {
            onBack();
            return;
        }

        if (backTo) {
            try {
                router.replace(backTo);
                return;
            } catch {
                // ignore and fallback to back
            }
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
        <SafeAreaView style={styles.safe}>
            <View style={styles.navbar}>
                <TouchableOpacity onPress={handleBack} style={styles.sideButton} accessibilityRole="button">
                    <Icon name="arrow-back-outline" size={24} color={colors.white} />
                </TouchableOpacity>

                <View style={styles.titleContainer}>
                    <Text numberOfLines={1} ellipsizeMode="tail" style={styles.title}>{title}</Text>
                </View>

                <View style={styles.sideButton} />
            </View>
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    safe: {
        backgroundColor: colors.primary,
    },
    navbar: {
        width: "100%",
        backgroundColor: colors.primary,
        flexDirection: "row",
        alignItems: "center",
        justifyContent: "space-between",
        paddingVertical: 10,
        paddingHorizontal: 12,
        elevation: 4,
        // include the status bar height on Android so layout is consistent
        paddingTop: Platform.OS === 'android' ? (StatusBar.currentHeight ?? 0) : 0,
        height: Platform.OS === 'android' ? (56 + (StatusBar.currentHeight ?? 0)) : 56,
    },
    sideButton: {
        width: 40,
        alignItems: 'flex-start',
        justifyContent: 'center'
    },
    titleContainer: {
        flex: 1,
        alignItems: 'center',
        justifyContent: 'center',
        paddingHorizontal: 8
    },
    title: {
        color: colors.white,
        fontSize: 18,
        fontWeight: "700",
        textAlign: "center",
    },
});
