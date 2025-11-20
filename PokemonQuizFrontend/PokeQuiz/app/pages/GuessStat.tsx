// Pokémon Quiz — Page: GuessStat
// Standard page header added for consistency. No behavior changes.

import React, { useState, useEffect } from "react";
import {
    View,
    Text,
    StyleSheet,
    Image,
    Platform,
    ActivityIndicator,
    StatusBar,
    ScrollView,
    useWindowDimensions
} from "react-native";
import { SafeAreaView } from 'react-native-safe-area-context';
import { colors } from "../../styles/colours";
import Navbar from "@/components/Navbar";
import AppButton from "@/components/AppButton";
import { Audio } from "expo-av";
import { useRouter } from "expo-router";

type StatOption = {
    stat: string;
    value: number;
};

type PokemonGameData = {
    pokemonName: string;
    image_Url: string;
    statToGuess: string;
    correctValue: number;
    otherValues: StatOption[];
};

export default function PokemonStatGame() {
    // hooks (must stay at top, before any conditional returns)
    const { width } = useWindowDimensions();

    const [pokemonData, setPokemonData] = useState<PokemonGameData | null>(null);
    const [isLoading, setIsLoading] = useState(false);
    const [score, setScore] = useState(0);
    const [totalQuestions, setTotalQuestions] = useState(0);
    const [usedPokemonNames, setUsedPokemonNames] = useState<Set<string>>(new Set());
    const [selectedAnswer, setSelectedAnswer] = useState<number | null>(null);
    const [showResult, setShowResult] = useState(false);
    const [isCorrect, setIsCorrect] = useState(false);
    const [options, setOptions] = useState<StatOption[]>([]);

    const router = useRouter();
    const serverIp = Platform.OS === "android" ? "10.0.2.2" : "localhost";
    const MAX_QUESTIONS = 10;

    // Add explicit back handler that replaces navigation to ChooseGame
    const handleBack = () => {
        // Navigate to game selection and reset stack so back won't return to quiz
        router.replace('/pages/ChooseGame');
    };

    const fetchRandomPokemon = async () => {
        setIsLoading(true);
        setSelectedAnswer(null);
        setShowResult(false);

        try {
            const res = await fetch(`http://${serverIp}:5168/api/game/random`);

            if (!res.ok) {
                throw new Error(`Server returned ${res.status}`);
            }

            const data = await res.json();

            // Check if we've already shown this Pokémon
            if (usedPokemonNames.has(data.pokemonName)) {
                console.log('Duplicate Pokémon, fetching a new one...');
                await fetchRandomPokemon(); // Fetch a new Pokémon
                return;
            }

            setPokemonData(data);
            // create a new Set copy when updating state
            setUsedPokemonNames((prev) => {
                const next = new Set(prev);
                next.add(data.pokemonName);
                return next;
            });
        } catch (error) {
            console.error(error);
        } finally {
            setIsLoading(false);
        }
    };

    useEffect(() => {
        fetchRandomPokemon();
    }, []);

    useEffect(() => {
        if (pokemonData) {
            const shuffledOptions = [...pokemonData.otherValues];
            shuffledOptions.push({ stat: pokemonData.statToGuess, value: pokemonData.correctValue });
            shuffledOptions.sort(() => Math.random() - 0.5); // Shuffle options

            setOptions(shuffledOptions);
        }
    }, [pokemonData]);

    const handleAnswerSelect = (value: number) => {
        if (!pokemonData || selectedAnswer !== null) return; // ignore if already answered

        setSelectedAnswer(value);

        const isAnswerCorrect = value === pokemonData.correctValue;
        setIsCorrect(isAnswerCorrect);
        setShowResult(true);

        if (isAnswerCorrect) setScore((s) => s + 1);
        setTotalQuestions((t) => t + 1);

        // Play sound
        playSound(isAnswerCorrect);

        // Auto-advance after 1.5s
        setTimeout(() => {
            if (totalQuestions + 1 < MAX_QUESTIONS) {
                // prepare for next question
                setSelectedAnswer(null);
                setShowResult(false);
                setIsCorrect(false);
                fetchRandomPokemon();
            } else {
                // navigate to results (GameOver)
                // ensure we pass the expected param names: 'score' and 'total'
                const finalScore = isAnswerCorrect ? score + 1 : score;
                const accuracyPoints = Math.round((finalScore / MAX_QUESTIONS) * 100);
                router.push({ pathname: '/pages/GameOver', params: { score: finalScore, total: MAX_QUESTIONS, points: accuracyPoints } } as any);
            }
        }, 1500);
    };

    const playSound = async (correct: boolean) => {
        try {
            const { sound } = await Audio.Sound.createAsync(
                correct
                    ? require("../../assets/sounds/correct.mp3")
                    : require("../../assets/sounds/incorrect.mp3")
            );
            await sound.playAsync();
            sound.setOnPlaybackStatusUpdate((status: any) => {
                if (status.isLoaded && status.didJustFinish) {
                    sound.unloadAsync();
                }
            });
        } catch (error) {
            console.log("Error playing sound", error);
        }
    };

    // ensure content not hidden under navbar/status bar on mobile
    const topOffset = Platform.OS === 'android' ? (StatusBar.currentHeight ?? 24) : 0;
    const imgSize = Math.min(220, Math.round(width * 0.6));

    // (early return kept below all hooks – safe)
    if (isLoading && !pokemonData) {
        return (
            <View style={styles.container}>
                <Navbar title="Guess Pokémon Stat" onBack={handleBack} />
                <View style={styles.content}>
                    <ActivityIndicator size="large" color={colors.primary || "#3b82f6"} />
                    <Text style={styles.loadingText}>Loading Pokémon...</Text>
                </View>
            </View>
        );
    }

    return (
        <SafeAreaView style={[styles.container, { paddingTop: topOffset }]}> 
            <Navbar title="Guess Pokémon Stat" onBack={handleBack} />
            <ScrollView contentContainerStyle={{ padding: 20, paddingBottom: 40 }} showsVerticalScrollIndicator={false}>
                <View style={styles.content}>
                    {pokemonData && (
                        <>
                            <Text style={styles.question} numberOfLines={3}>
                                {`What's the base ${pokemonData.statToGuess} of ${pokemonData.pokemonName}?`}
                            </Text>
                            <Image
                                source={{ uri: pokemonData.image_Url }}
                                style={[styles.image, { width: imgSize, height: imgSize }]}
                                resizeMode="contain"
                            />
                            <View style={styles.optionsContainer}>
                                {options.map((option, idx) => {
                                    const statColor = colors[option.stat as keyof typeof colors] as string | undefined;
                                    const bg = showResult
                                        ? (option.value === pokemonData.correctValue ? statColor ?? colors.primary : colors.surface)
                                        : colors.surface;
                                    return (
                                        <AppButton
                                            key={`${option.value}-${idx}`}
                                            label={`${option.stat}: ${option.value}`}
                                            onPress={() => handleAnswerSelect(option.value)}
                                            style={{ ...styles.optionButton, backgroundColor: bg }}
                                            textStyle={styles.optionButtonText}
                                            disabled={showResult}
                                            backgroundColor={bg}
                                            textColor={colors.text}
                                        />
                                    );
                                })}
                            </View>
                            {showResult && (
                                <Text style={styles.resultText} numberOfLines={2}>
                                    {isCorrect ? "Correct!" : `Wrong! The correct stat was ${pokemonData.correctValue}.`}
                                </Text>
                            )}
                        </>
                    )}
                </View>
            </ScrollView>
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, backgroundColor: colors.background },
    content: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 20 },
    question: { fontSize: 22, fontWeight: '900', marginBottom: 20, textAlign: 'center', color: colors.text },
    image: { width: 220, height: 220, marginBottom: 20, backgroundColor: 'rgba(0,0,0,0.05)', borderRadius: 16 },
    optionsContainer: { width: '100%', marginBottom: 20 },
    optionButton: { width: '100%', padding: 15, marginVertical: 6, borderRadius: 12, elevation: 2, backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.grey },
    optionButtonText: { fontSize: 16, fontWeight: '700', color: colors.text, textAlign: 'center' },
    resultText: { fontSize: 18, fontWeight: '700', marginVertical: 12, textAlign: 'center', color: colors.text },
    loadingText: { fontSize: 16, fontWeight: '600', marginTop: 12, textAlign: 'center', color: colors.text }
});