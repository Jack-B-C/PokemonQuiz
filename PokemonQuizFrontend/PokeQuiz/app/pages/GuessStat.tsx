import React, { useState, useEffect } from "react";
import {
    View,
    Text,
    StyleSheet,
    Image,
    Platform,
    ActivityIndicator,
} from "react-native";
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
                console.log('Duplicate Pokémon, fetching another...');
                await fetchRandomPokemon();
                return;
            }

            // Shuffle options ONCE when data is loaded
            const shuffledOptions = [
                { stat: data.statToGuess, value: data.correctValue },
                ...data.otherValues,
            ].sort(() => Math.random() - 0.5);

            setOptions(shuffledOptions);
            setPokemonData(data);
            setUsedPokemonNames(prev => new Set([...prev, data.pokemonName]));
        } catch (err) {
            console.error(err);
            alert("Failed to fetch Pokémon from server. Make sure the API is running!");
        } finally {
            setIsLoading(false);
        }
    };

    useEffect(() => {
        fetchRandomPokemon();
    }, []);

    const checkAnswer = async (value: number) => {
        if (!pokemonData || selectedAnswer !== null) return;

        setSelectedAnswer(value);
        setTotalQuestions(prev => prev + 1);

        const correct = value === pokemonData.correctValue;
        setIsCorrect(correct);
        setShowResult(true);

        if (correct) {
            setScore(prev => prev + 1);
        }

        // Play sound
        try {
            const { sound } = await Audio.Sound.createAsync(
                correct
                    ? require("../../assets/sounds/correct.mp3")
                    : require("../../assets/sounds/incorrect.mp3")
            );
            await sound.playAsync();
            sound.setOnPlaybackStatusUpdate((status) => {
                if (status.isLoaded && status.didJustFinish) {
                    sound.unloadAsync();
                }
            });
        } catch (error) {
            console.warn("Sound playback failed", error);
        }

        // Auto-advance after 2 seconds
        setTimeout(async () => {
            const nextQuestion = totalQuestions + 1;
            if (nextQuestion >= MAX_QUESTIONS) {
                // End game
                router.push({
                    pathname: "/pages/GameOver",
                    params: {
                        score: (score + (correct ? 1 : 0)).toString(),
                        total: MAX_QUESTIONS.toString()
                    }
                } as any);
            } else {
                await fetchRandomPokemon();
            }
        }, 2000);
    };

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

    if (!pokemonData || options.length === 0) {
        return (
            <View style={styles.container}>
                <Navbar title="Guess Pokémon Stat" onBack={handleBack} />
                <View style={styles.content}>
                    <Text style={styles.errorText}>Failed to load Pokémon data</Text>
                    <AppButton
                        label="Retry"
                        onPress={fetchRandomPokemon}
                        style={styles.retryButton}
                    />
                </View>
            </View>
        );
    }

    return (
        <View style={styles.container}>
            <Navbar title="Guess Pokémon Stat" onBack={handleBack} />

            {/* Score Display */}
            <View style={styles.scoreContainer}>
                <Text style={styles.scoreText}>
                    Question {totalQuestions + 1}/{MAX_QUESTIONS} | Score: {score}
                </Text>
            </View>

            <View style={styles.content}>
                <Text style={styles.title}>
                    What is {pokemonData.pokemonName}{"'"}s{"\n"}{pokemonData.statToGuess}?
                </Text>

                <Image
                    source={{ uri: pokemonData.image_Url }}
                    style={styles.pokemonImage}
                    resizeMode="contain"
                />

                {/* Result Feedback Banner */}
                {showResult && (
                    <View style={[
                        styles.resultBanner,
                        isCorrect ? styles.correctBanner : styles.incorrectBanner
                    ]}>
                        <Text style={styles.resultEmoji}>
                            {isCorrect ? "🎉" : "❌"}
                        </Text>
                        <Text style={styles.resultText}>
                            {isCorrect
                                ? "Correct!"
                                : `Wrong! It's ${pokemonData.correctValue}`
                            }
                        </Text>
                    </View>
                )}

                <View style={styles.optionsContainer}>
                    {options.map((item, index) => {
                        const isSelected = selectedAnswer === item.value;
                        const isCorrectOption = item.value === pokemonData.correctValue;
                        const showCorrect = showResult && isCorrectOption;
                        const showIncorrect = showResult && isSelected && !isCorrect;

                        let buttonStyle = styles.optionButton;
                        if (showCorrect) {
                            buttonStyle = { ...styles.optionButton, ...styles.correctButton };
                        } else if (showIncorrect) {
                            buttonStyle = { ...styles.optionButton, ...styles.incorrectButton };
                        }

                        return (
                            <AppButton
                                key={`${item.value}-${index}`}
                                label={`${item.value}`}
                                onPress={() => checkAnswer(item.value)}
                                style={buttonStyle}
                                disabled={selectedAnswer !== null}
                            />
                        );
                    })}
                </View>

                {/* Progress indicator */}
                <Text style={styles.progressText}>
                    Unique Pokémon seen: {usedPokemonNames.size}
                </Text>
            </View>
        </View>
    );
}

const styles = StyleSheet.create({
    container: {
        flex: 1,
        backgroundColor: colors.background || "#151515"
    },
    content: {
        flex: 1,
        justifyContent: "center",
        alignItems: "center",
        padding: 20
    },
    scoreContainer: {
        backgroundColor: colors.primary || "#3b82f6",
        padding: 16,
        alignItems: "center",
    },
    scoreText: {
        fontSize: 18,
        fontWeight: "700",
        color: "#fff",
    },
    title: {
        fontSize: 24,
        fontWeight: "700",
        marginBottom: 20,
        textAlign: "center",
        color: colors.text || "#fff",
    },
    pokemonImage: {
        width: 200,
        height: 200,
        marginBottom: 20
    },
    resultBanner: {
        flexDirection: "row",
        alignItems: "center",
        padding: 16,
        borderRadius: 12,
        marginBottom: 20,
        minWidth: "80%",
        justifyContent: "center",
    },
    correctBanner: {
        backgroundColor: "#10b981",
    },
    incorrectBanner: {
        backgroundColor: "#ef4444",
    },
    resultEmoji: {
        fontSize: 32,
        marginRight: 12,
    },
    resultText: {
        fontSize: 20,
        fontWeight: "700",
        color: "#fff",
    },
    optionsContainer: {
        width: "100%",
        maxWidth: 400,
        gap: 12,
    },
    optionButton: {
        width: "100%",
        paddingVertical: 16,
    },
    correctButton: {
        backgroundColor: "#10b981",
    },
    incorrectButton: {
        backgroundColor: "#ef4444",
    },
    progressText: {
        marginTop: 20,
        fontSize: 14,
        color: "#9ca3af",
    },
    loadingText: {
        marginTop: 16,
        fontSize: 16,
        color: colors.text || "#fff",
    },
    errorText: {
        fontSize: 18,
        color: colors.error || "#ef4444",
        marginBottom: 20,
        textAlign: "center",
    },
    retryButton: {
        width: "80%",
    },
});