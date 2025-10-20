import React from "react";
import { View, Text, StyleSheet, TouchableOpacity } from "react-native";
import { colors } from "../styles/colours";
import Icon from "react-native-vector-icons/Ionicons";

type Props = {
    title: string;
    onBack?: () => void;
    navigation?: any;
};

export default function Navbar({ title, onBack, navigation }: Props) {
    const handleBack = () => {
        if (onBack) {
            onBack();
        } else if (navigation) {
            navigation.navigate("Home");
        }
    };

    return (
        <View style={styles.navbar}>
            {/* Back button (optional) */}
            <TouchableOpacity onPress={handleBack} style={styles.backButton}>
                <Icon name="arrow-back-outline" size={26} color={colors.white} />
            </TouchableOpacity>

            {/* Page Title */}
            <Text style={styles.title}>{title}</Text>

            {/* Placeholder to balance layout (for symmetry) */}
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