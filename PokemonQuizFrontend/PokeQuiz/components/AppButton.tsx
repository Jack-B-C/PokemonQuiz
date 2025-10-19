// components/AppButton.tsx
// Reusable button component for consistent styling and behavior across the app.

import React from "react";
import { TouchableOpacity, Text, StyleSheet, ViewStyle, TextStyle } from "react-native";

// Define the expected props (inputs) for this component.
type Props = {
    label: string;                  // The text shown on the button
    onPress: () => void;            // Function that runs when the button is pressed
    backgroundColor?: string;       // Optional background color (defaults to red)
    textColor?: string;             // Optional text color (defaults to white)
    style?: ViewStyle;              // Optional additional container styles (e.g. margin)
    textStyle?: TextStyle;          // Optional text styling (e.g. fontSize override)
};

// The main AppButton component
export default function AppButton({
    label,
    onPress,
    backgroundColor = "#E63946",    // Default red background if none is provided
    textColor = "#fff",             // Default white text
    style,
    textStyle,                      // Allow custom text styling via props
}: Props) {
    return (
        // TouchableOpacity makes the button respond visually when tapped
        <TouchableOpacity
            style={[styles.button, { backgroundColor }, style]}  // Merge default + custom styles
            onPress={onPress}                                   // Handle press action
            activeOpacity={0.8}                                 // Slightly fade when pressed
        >
            {/* Display the label text, applying both default and custom text styles */}
            <Text style={[styles.text, { color: textColor }, textStyle]}>
                {label}
            </Text>
        </TouchableOpacity>
    );
}

// Default button styling
const styles = StyleSheet.create({
    button: {
        paddingVertical: 14,         // Top & bottom padding
        paddingHorizontal: 30,       // Left & right padding
        borderRadius: 12,            // Rounded corners
        alignItems: "center",        // Center text horizontally
    },
    text: {
        fontSize: 18,                // Default font size
        fontWeight: "bold",          // Bold text for emphasis
        textTransform: "uppercase",  // Converts label to uppercase automatically
    },
});
