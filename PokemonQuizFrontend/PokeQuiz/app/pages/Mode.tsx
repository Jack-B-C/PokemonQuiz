import * as React from 'react';
import {
    View,
    Text,
    StyleSheet,
    Dimensions,
    Image,
    Animated,
    Platform,
    Pressable,
    StatusBar,
    ScrollView,
} from 'react-native';
import { colors } from '../../styles/colours';
import Navbar from '../../components/Navbar';
import { router } from 'expo-router';

// -----------------------------------------------------------------------------
// Mode Screen
//
// Purpose:
// - Entry point for selecting single-player vs multiplayer flows.
// - Provides polished visuals and accessible controls for mode selection.
//
// Notes:
// - Multiplayer flow navigates to the multiplayer setup screens. Certain
//   multiplayer game pages may be temporarily disabled in the UI while they
//   remain in the codebase for future reactivation.
// -----------------------------------------------------------------------------

export const options = { headerShown: false } as const;

const { width: screenWidth, height: screenHeight } = Dimensions.get('window');

const singlePlayerImage = require('../../assets/images/charizard.png');
const multiplayerImage = require('../../assets/images/maushold.png');

export default function ModeScreen() {
    const singleScale = React.useRef(new Animated.Value(1)).current;
    const multiScale = React.useRef(new Animated.Value(1)).current;

    const handleHover = (scaleRef: Animated.Value, hovering: boolean) => {
        Animated.timing(scaleRef, {
            toValue: hovering ? 1.03 : 1,
            duration: 150,
            useNativeDriver: true,
        }).start();
    };

    const createHandlers = (scaleRef: Animated.Value, routeName: string) => {
        if (Platform.OS === 'web') {
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

    // compute small-screen boolean
    const isSmall = screenHeight < 700 || screenWidth < 360;

    return (
        <View style={styles.container}>
            <Navbar title="Select Mode" backTo="/" />

            <ScrollView contentContainerStyle={[styles.contentContainer, { paddingVertical: isSmall ? 20 : 32 }]} showsVerticalScrollIndicator={false}>
                <Text style={styles.title}>Choose Your Preferred Game Mode</Text>

                <View style={styles.modeButtonsContainer}>
                    {/* Single Player */}
                    <Pressable {...createHandlers(singleScale, '/pages/ChooseGame')}>
                        <Animated.View
                            style={[
                                styles.modeCard,
                                { transform: [{ scale: singleScale }], padding: isSmall ? 28 : 48 },
                                Platform.OS === 'web'
                                    ? ({ cursor: 'pointer', transition: 'transform 0.2s ease-in-out' } as any)
                                    : {},
                            ]}
                        >
                            <Text style={styles.cardTitle}>Single Player</Text>
                            <Image source={singlePlayerImage} style={[styles.cardImage, isSmall ? { width: 120, height: 120 } : {}]} resizeMode="contain" />
                            <Text style={styles.cardDescription}>
                                Take quizzes, test your knowledge, and beat that high score!
                            </Text>
                        </Animated.View>
                    </Pressable>

                    {/* Multiplayer */}
                    <Pressable {...createHandlers(multiScale, '/pages/MultiplayerSetup')}>
                        <Animated.View
                            style={[
                                styles.modeCard,
                                { transform: [{ scale: multiScale }], padding: isSmall ? 28 : 48 },
                                Platform.OS === 'web'
                                    ? ({ cursor: 'pointer', transition: 'transform 0.2s ease-in-out' } as any)
                                    : {},
                            ]}
                        >
                            <Text style={styles.cardTitle}>Multiplayer</Text>
                            <Image source={multiplayerImage} style={[styles.cardImage, isSmall ? { width: 120, height: 120 } : {}]} resizeMode="contain" />
                            <Text style={styles.cardDescription}>
                                Take quizzes as a group and see who becomes the ultimate trainer!
                            </Text>
                        </Animated.View>
                    </Pressable>
                </View>
            </ScrollView>
        </View>
    );
}

const styles = StyleSheet.create({
    container: {
        flex: 1,
        backgroundColor: colors.white,
    },
    contentContainer: {
        // let ScrollView content determine spacing; avoid extra top padding so Navbar doesn't get duplicated
        justifyContent: 'flex-start',
        alignItems: 'center',
        paddingHorizontal: 20,
    },
    title: {
        fontSize: 28,
        fontWeight: '700',
        color: '#666',
        textAlign: 'center',
        marginBottom: 20,
    },
    modeButtonsContainer: {
        width: '100%',
        flexDirection: screenWidth > 700 ? 'row' : 'column',
        justifyContent: 'center',
        alignItems: 'center',
        gap: 20,
    },
    modeCard: {
        width: screenWidth > 700 ? 400 : '92%',
        // remove fixed aspectRatio so card expands naturally and can scroll
        backgroundColor: colors.primary || '#FF5252',
        borderRadius: 28,
        justifyContent: 'space-between',
        alignItems: 'center',
        shadowColor: '#000',
        shadowOpacity: 0.25,
        shadowRadius: 8,
        shadowOffset: { width: 0, height: 6 },
        elevation: 5,
        alignSelf: 'center',
        marginVertical: 12,
    },
    cardTitle: {
        fontSize: 26,
        fontWeight: '700',
        color: colors.white,
        textAlign: 'center',
        marginTop: 6,
    },
    cardImage: {
        width: 160,
        height: 160,
    },
    cardDescription: {
        fontSize: 15,
        color: colors.white,
        textAlign: 'center',
        lineHeight: 22,
        marginBottom: 6,
    },
});