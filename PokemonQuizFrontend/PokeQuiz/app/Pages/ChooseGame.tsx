import React from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { useNavigation } from '@react-navigation/native';
import { colors } from '../../styles/colours';
import Navbar from '@/components/Navbar';
import AppButton from '@/components/AppButton';

export default function ChooseGame() {
    const navigation = useNavigation();

    return (
        <View style={styles.container}>
            <Navbar title="Choose Game" showBackButton />

            <View style={styles.content}>
                <Text style={styles.subtitle}>Choose your game mode</Text>

                <AppButton
                    label="Single Player"
                    onPress={() => navigation.navigate('SinglePlayer')}
                    backgroundColor="#3D52D5"
                    textColor="#FFFFFF"
                    style={styles.button}
                />

                <AppButton
                    label="Multiplayer"
                    onPress={() => navigation.navigate('Multiplayer')}
                    backgroundColor="#3D52D5"
                    textColor="#FFFFFF"
                    style={styles.button}
                />
            </View>
        </View>
    );
}

const styles = StyleSheet.create({
    container: {
        flex: 1,
        backgroundColor: '#151515',
    },
    content: {
        flex: 1,
        justifyContent: 'center',
        alignItems: 'center',
        padding: 20,
    },
    subtitle: {
        fontSize: 18,
        color: colors.white,
        marginBottom: 20,
        textAlign: 'center',
    },
    button: {
        marginVertical: 10,
        width: '80%',
    },
});
