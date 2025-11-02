import React from "react";
import { View, Text, StyleSheet, TouchableOpacity, Platform, StatusBar, SafeAreaView } from 'react-native';
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
        <SafeAreaView style={styles.safe}>
            <View style={styles.navbar}>
                <TouchableOpacity onPress={handleBack} style={styles.backButton}>
                    <Icon name="arrow-back-outline" size={26} color={colors.white} />
                </TouchableOpacity>

                <Text numberOfLines={1} ellipsizeMode="tail" style={styles.title}>{title}</Text>

                <View style={{ width: 30 }} />
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
        paddingVertical: 12,
        paddingHorizontal: 16,
        elevation: 4,
        // on Android include the status bar height as padding
        paddingTop: Platform.OS === 'android' ? StatusBar.currentHeight : 0,
    },
    backButton: {
        padding: 6,
        zIndex: 2,
    },
    title: {
        color: colors.white,
        fontSize: 18,
        fontWeight: "700",
        textAlign: "center",
        position: 'absolute',
        left: 0,
        right: 0,
        alignSelf: 'center',
        zIndex: 1,
        paddingHorizontal: 48,
    },
});