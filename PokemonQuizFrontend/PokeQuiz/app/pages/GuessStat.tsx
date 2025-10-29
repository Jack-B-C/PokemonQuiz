import React, { useState, useEffect } from "react";
import {
    View,
    Text,
    StyleSheet,
    Image,
    ActivityIndicator,
    Platform,
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
    const [usedPokemons, setUsedPokemons] = useState<string[]>([]);
    const [selectedValue, setSelectedValue] = useState<number | null>(null);
    const [showAnswer, setShowAnswer] = useState(false);
    const [options, setOptions] = useState<StatOption[]>([]);

    const router = useRouter();
    const serverIp = Platform.OS === "android" ? "10.0.2.2" : "localhost";
    const MAX_QUESTIONS = 10;

    const fetchRandomPokemon = async () => {
        setIsLoading(true);
        setShowAnswer(false);
        setSelectedValue(null);
        try {
            const res = await fetch(`http://${serverIp}:5168/api/game/random`);
            if (!res.ok) throw new Error(`Server returned ${res.status}`);
            const data = await res.json();

            if (usedPokemons.includes(data.pokemonName)) {
                await fetchRandomPokemon();
                return;
            }

            setPokemonData(data);
            setUsedPokemons([...usedPokemons, data.pokemonName]);
        } catch (err) {
            console.error(err);
        } finally {
            setIsLoading(false);
        }
    };

    useEffect(() => {
        if (pokemonData) {
            const shuffled = [
                { stat: pokemonData.statToGuess, value: pokemonData.correctValue },
                ...pokemonData.otherValues,
            ].sort(() => Math.random() - 0.5);
            setOptions(shuffled);
        }
    }, [pokemonData]);

    useEffect(() => {
        fetchRandomPokemon();
    }, []);

    const endGame = () => {
        // Use object syntax for Expo Router to pass query params
        router.push({
            pathname: "/pages/GameOver",
            params: { score: score.toString(), total: MAX_QUESTIONS.toString() },
        });
    };

    const checkAnswer = async (value: number) => {
        if (!pokemonData || showAnswer) return;

        setSelectedValue(value);
        setShowAnswer(true);
        const correct = value === pokemonData.correctValue;
        if (correct) setScore((prev) => prev + 1);

        try {
            const { sound } = await Audio.Sound.createAsync(
                correct
                    ? require("../../assets/sounds/correct.mp3")
                    : require("../../assets/sounds/incorrect.mp3")
            );
            await sound.playAsync();
            sound.setOnPlaybackStatusUpdate((status) => {
                if (status.isLoaded && status.didJustFinish) sound.unloadAsync();
            });
        } catch (error) {
            console.warn("Sound playback failed", error);
        }

        setTotalQuestions((prev) => prev + 1);

        setTimeout(() => {
            const nextQuestion = totalQuestions + 1;
            if (nextQuestion >= MAX_QUESTIONS) {
                endGame();
            } else {
                fetchRandomPokemon();
            }
        }, 1000);
    };

    if (isLoading && !pokemonData) {
        return (
            <View style={styles.container}>
                <Navbar title="Guess Pokémon Stat" />
                <ActivityIndicator size="large" color={colors.primary || "#3b82f6"} />
            </View>
        );
    }

    if (!pokemonData) {
        return (
            <View style={styles.container}>
                <Navbar title="Guess Pokémon Stat" />
                <Text style={styles.errorText}>Failed to load Pokémon data</Text>
                <AppButton label="Retry" onPress={fetchRandomPokemon} style={styles.retryButton} />
            </View>
        );
    }

    return (
        <View style={styles.container}>
            <Navbar title="Guess Pokémon Stat" />

            <View style={styles.scoreContainer}>
                <Text style={styles.scoreText}>
                    {totalQuestions}/{MAX_QUESTIONS} questions
                </Text>
                <AppButton
                    label="Give Up"
                    onPress={endGame}
                    style={styles.giveUpButton}
                />
            </View>

            <View style={styles.content}>
                <Text style={styles.title}>
                    What is {pokemonData.pokemonName}'s{"\n"}
                    <Text style={{ color: colors[pokemonData.statToGuess as keyof typeof colors] }}>
                        {pokemonData.statToGuess}
                    </Text>
                    ?
                </Text>

                <Image
                    source={{ uri: pokemonData.image_Url }}
                    style={styles.pokemonImage}
                    resizeMode="contain"
                />

                <View style={styles.optionsContainer}>
                    {options.map((item, index) => {
                        let bgColor = colors.primary || "#3b82f6";
                        if (showAnswer) {
                            if (item.value === pokemonData.correctValue) {
                                bgColor = "#22c55e";
                            } else if (item.value === selectedValue) {
                                bgColor = "#ef4444";
                            }
                        }

                        return (
                            <AppButton
                                key={`${item.value}-${index}`}
                                label={`${item.value}`}
                                onPress={() => checkAnswer(item.value)}
                                backgroundColor={bgColor}
                                disabled={showAnswer}
                                style={styles.optionButton}
                            />
                        );
                    })}
                </View>
            </View>
        </View>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, backgroundColor: colors.background || "#151515" },
    content: { flex: 1, justifyContent: "center", alignItems: "center", padding: 20 },
    scoreContainer: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", paddingHorizontal: 20, marginVertical: 10 },
    scoreText: { fontSize: 18, fontWeight: "700", color: "#fff" },
    giveUpButton: { width: 100, paddingVertical: 6 },
    title: { fontSize: 26, fontWeight: "700", marginBottom: 20, textAlign: "center", color: colors.text || "#fff" },
    pokemonImage: { width: 220, height: 220, marginBottom: 20 },
    optionsContainer: { flexDirection: "row", flexWrap: "wrap", justifyContent: "space-between", width: "100%", maxWidth: 400 },
    optionButton: { width: "48%", paddingVertical: 20, marginBottom: 12, borderRadius: 12, alignItems: "center", justifyContent: "center" },
    errorText: { fontSize: 18, color: colors.error || "#ef4444", marginBottom: 20, textAlign: "center" },
    retryButton: { width: "80%" },
});
