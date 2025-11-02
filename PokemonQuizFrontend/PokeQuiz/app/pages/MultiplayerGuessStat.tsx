import React, { useState, useEffect, useRef } from "react";
import {
    View,
    Text,
    StyleSheet,
    Image,
    Platform,
    ActivityIndicator,
    Modal,
    ScrollView,
    StyleProp,
    ViewStyle,
} from "react-native";
import { colors } from "../../styles/colours";
import Navbar from "@/components/Navbar";
import AppButton from "@/components/AppButton";
import { Audio } from "expo-av";
import { useRouter, useLocalSearchParams } from "expo-router";
import * as SignalR from "@microsoft/signalr";
import { ensureConnection, getConnection } from '../../utils/signalrClient';

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

type Player = {
    name: string;
    score: number;
    answered?: boolean;
};

export default function MultiplayerPokemonGame() {
    const [pokemonData, setPokemonData] = useState<PokemonGameData | null>(null);
    const [options, setOptions] = useState<StatOption[]>([]);
    const [selectedAnswer, setSelectedAnswer] = useState<number | null>(null);
    const selectedAnswerRef = useRef<number | null>(null);
    const [showResult, setShowResult] = useState(false);
    const [isCorrect, setIsCorrect] = useState(false);
    const [timer, setTimer] = useState(20);
    const [players, setPlayers] = useState<Player[]>([]);
    const [showLeaderboard, setShowLeaderboard] = useState(false);
    const [currentRound, setCurrentRound] = useState(0);
    const [pointsEarned, setPointsEarned] = useState(0);
    const [roundResults, setRoundResults] = useState<{ name: string; correct: boolean; answer: number | null }[] | null>(null);
    const [waitingForOthers, setWaitingForOthers] = useState(false);
    const questionReceivedRef = useRef<number>(0);

    const router = useRouter();
    const params = useLocalSearchParams();
    const serverIp = Platform.OS === "android" ? "10.0.2.2" : "localhost";

    const roomCode = (params as any).roomCode as string;
    const playerName = (params as any).playerName as string;
    const isHost = String((params as any).isHost ?? "") === "true";

    const connectionRef = useRef<SignalR.HubConnection | null>(null);
    const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);
    const questionStartTimeRef = useRef<number>(0);

    const MAX_ROUNDS = 10;
    const QUESTION_TIME = 20;

    // back handler: leave room then navigate to choose game
    const handleBack = async () => {
        try {
            if (connectionRef.current && roomCode) {
                try { await connectionRef.current.invoke('LeaveRoom', roomCode); } catch (e) { /* ignore */ }
                try { await connectionRef.current.stop(); } catch { }
            }
        } catch {
            // ignore errors
        }
        // replace to ChooseGame so back stack is reset
        try { router.replace('/pages/ChooseGame'); } catch { router.push('/'); }
    };

    // Setup SignalR connection
    useEffect(() => {
        setupConnection();
        return () => {
            if (timerRef.current) clearInterval(timerRef.current);
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    const setupConnection = async () => {
        if (!roomCode || !playerName) return;
        const hubUrl = `http://${serverIp}:5168/hubs/game`;
        try {
            const connection = await ensureConnection(hubUrl);
            connectionRef.current = connection;

            // detach handlers to avoid duplicates
            try { connection.off("Question"); } catch {}
            try { connection.off("ScoreUpdated"); } catch {}
            try { connection.off("AllAnswered"); } catch {}
            try { connection.off("GameOver"); } catch {}
            try { connection.off("gameover"); } catch {}
            try { connection.off("RoomJoined"); } catch {}
            try { connection.off("Error"); } catch {}

            connection.on("Question", (data: any) => {
                console.debug('SignalR Question payload:', data);
                handleNewQuestion(data);
            });

            connection.on("ScoreUpdated", (data: any) => {
                console.debug('SignalR ScoreUpdated payload:', data);
                if (data?.players) {
                    setPlayers(data.players.map((p: any) => ({ name: p.Name ?? p.name, score: p.Score ?? p.score, answered: p.Answered ?? p.answered })));
                }
            });

            connection.on("AllAnswered", (data: any) => {
                console.debug('SignalR AllAnswered payload:', data);
                if (data?.leaderboard) {
                    setPlayers(data.leaderboard.map((p: any) => ({ name: p.Name ?? p.name, score: p.Score ?? p.score })));
                }
                if (data?.submissions) {
                    const results = (data.submissions as any[]).map((s: any, i: number) => ({
                        name: s.data?.playerName ?? s.data?.name ?? players[i]?.name ?? `Player ${i+1}`,
                        correct: !!s.data?.correct,
                        answer: s.data?.selectedValue ?? null
                    }));
                    setRoundResults(results);
                }
                setWaitingForOthers(false);
                setShowLeaderboard(true);
            });

            connection.on("RoomJoined", (data: any) => {
                console.debug('SignalR RoomJoined payload:', data);
                if (data?.players) setPlayers(data.players);
                if (data?.currentQuestion) {
                    const q = data.currentQuestion;
                    if (q && data?.questionStartedAt) {
                        const started = Date.parse(data.questionStartedAt as string);
                        if (!isNaN(started)) {
                            handleNewQuestion(q, started);
                        } else {
                            handleNewQuestion(q);
                        }
                    } else if (q) {
                        handleNewQuestion(q);
                    }
                }
            });

            connection.on("Error", (msg: string) => {
                // ignore noisy "Game already started" when rehydrating; log others
                if (typeof msg === 'string' && msg.includes('Game already started')) {
                    console.debug('Ignored server error during join:', msg);
                    return;
                }
                console.warn('Server Error', msg);
            });

            connection.on("GameOver", (payload: any) => {
                console.debug('SignalR GameOver payload:', payload);
                try {
                    const leaderboard = payload?.leaderboard ?? payload;
                    const rc = payload?.roomCode ?? roomCode;
                    const leaderboardStr = JSON.stringify(leaderboard);
                    // navigate to GameOver screen with leaderboard and roomCode
                    try { router.replace({ pathname: '/pages/GameOver', params: { leaderboard: leaderboardStr, roomCode: rc } } as any); } catch { router.push({ pathname: '/pages/GameOver', params: { leaderboard: leaderboardStr, roomCode: rc } } as any); }
                } catch (e) {
                    console.warn('Failed to handle GameOver payload', e);
                }
            });
            connection.on("gameover", (payload: any) => {
                console.debug('SignalR gameover payload:', payload);
                try {
                    const leaderboard = payload?.leaderboard ?? payload;
                    const rc = payload?.roomCode ?? roomCode;
                    const leaderboardStr = JSON.stringify(leaderboard);
                    try { router.replace({ pathname: '/pages/GameOver', params: { leaderboard: leaderboardStr, roomCode: rc } } as any); } catch { router.push({ pathname: '/pages/GameOver', params: { leaderboard: leaderboardStr, roomCode: rc } } as any); }
                } catch (e) {
                    console.warn('Failed to handle gameover payload', e);
                }
            });

            // Try to rehydrate room info (this will also inform whether the game already started)
            try {
                const info = await connection.invoke('GetRoomInfo', roomCode);
                console.debug('GetRoomInfo result:', info);
                if (info) {
                    if (info.players) setPlayers(info.players);

                    // If server tells us the game already started, don't attempt a JoinRoom (it would error)
                    const gameStarted = !!info.gameStarted || !!info.GameStarted || false;

                    if (info.currentQuestion) {
                        const startedAt = info.questionStartedAt ? Date.parse(info.questionStartedAt as string) : NaN;
                        if (!isNaN(startedAt)) handleNewQuestion(info.currentQuestion, startedAt);
                        else handleNewQuestion(info.currentQuestion);
                    }

                    if (!gameStarted) {
                        try { await connection.invoke('JoinRoom', roomCode, playerName); } catch (e) { console.warn('JoinRoom failed in multiplayer page', e); }
                    } else {
                        // If game already started, attempt to claim host if our name matches host (for reconnect) otherwise skip Join
                        console.debug('Room already started, skipping JoinRoom to avoid server error');
                    }
                }

            } catch (e) {
                console.warn('GetRoomInfo/JoinRoom failed in multiplayer setup', e);
            }

        } catch (err) {
            console.error('Multiplayer setup failed', err);
            alert('Connection Error');
        }
    };

    // Updated handleNewQuestion to accept optional start timestamp
    const handleNewQuestion = (data: any, startAtMs?: number | null) => {
        questionReceivedRef.current = Date.now();
        const normalized = normalizeIncomingQuestion(data);
        console.debug('Normalized question:', normalized);
        if (!normalized) {
            // show fallback
            setPokemonData({ pokemonName: 'Unknown', image_Url: '', statToGuess: 'stat', correctValue: 0, otherValues: [] });
            setOptions([]);
            return;
        }

        const shuffled = [
            { stat: normalized.statToGuess, value: normalized.correctValue },
            ...normalized.otherValues,
        ].sort(() => Math.random() - 0.5);

        // reset selected answer ref and state
        selectedAnswerRef.current = null;
        setSelectedAnswer(null);
        setShowResult(false);
        setIsCorrect(false);
        setWaitingForOthers(false);

        setOptions(shuffled.map(o => ({ stat: o.stat, value: Number(o.value) })));
        setPokemonData(normalized);
        setShowLeaderboard(false);
        setRoundResults(null);
        setPointsEarned(0);
        setCurrentRound((prev) => prev + 1);

        // compute timer based on provided startAtMs (rehydration) or now
        const now = Date.now();
        const started = startAtMs ?? now;
        questionStartTimeRef.current = started;

        const elapsedSec = Math.floor((now - started) / 1000);
        const remaining = Math.max(0, QUESTION_TIME - elapsedSec);
        setTimer(remaining);

        if (timerRef.current) clearInterval(timerRef.current);
        if (remaining > 0) {
            timerRef.current = setInterval(() => {
                setTimer((prev) => {
                    const currentSelected = selectedAnswerRef.current;
                    if (prev <= 1) {
                        if (timerRef.current) clearInterval(timerRef.current);
                        if (currentSelected === null) {
                            submitAnswer(-1);
                        }
                        return 0;
                    }
                    return prev - 1;
                });
            }, 1000);
        }
    };

    const normalizeIncomingQuestion = (data: any): PokemonGameData | null => {
        if (!data) return null;
        const pokemonName = data.pokemonName ?? data.PokemonName ?? data.pokemon_name ?? data.name ?? null;
        const image_Url = data.image_Url ?? data.Image_Url ?? data.imageUrl ?? data.ImageUrl ?? data.image_url ?? null;
        const statToGuess = data.statToGuess ?? data.StatToGuess ?? data.stat_to_guess ?? data.stat ?? null;
        const correctValue = data.correctValue ?? data.CorrectValue ?? data.correct_value ?? null;
        let otherValues = data.otherValues ?? data.OtherValues ?? data.other_values ?? null;

        if (Array.isArray(otherValues)) {
            otherValues = otherValues.map((ov: any) => {
                if (typeof ov === 'number') return { stat: statToGuess ?? '', value: ov };
                return { stat: ov.stat ?? ov.Stat ?? statToGuess ?? '', value: ov.value ?? ov.Value ?? ov };
            });
        } else {
            otherValues = [];
        }

        if (!pokemonName || !statToGuess || correctValue == null) return null;

        return {
            pokemonName,
            image_Url: image_Url ?? '',
            statToGuess,
            correctValue: Number(correctValue),
            otherValues
        };
    };

    const submitAnswer = async (value: number) => {
        if (selectedAnswerRef.current !== null || !pokemonData || !connectionRef.current) return;

        selectedAnswerRef.current = value;
        setSelectedAnswer(value);
         setWaitingForOthers(true);

         // mark local player as answered for UI
         setPlayers(prev => prev.map(p => p.name === playerName ? { ...p, answered: true } : p));

         const timeTaken = Date.now() - questionStartTimeRef.current;
         const correct = value === pokemonData.correctValue;
         setIsCorrect(correct);
         setShowResult(true);

         if (timerRef.current) clearInterval(timerRef.current);

         let points = 0;
         if (correct) {
             const basePoints = 1000;
             const speedBonus = Math.floor((timer / QUESTION_TIME) * 500);
             points = basePoints + speedBonus;
             setPointsEarned(points);
         }

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
             console.warn("Sound failed", error);
         }

         try {
             await connectionRef.current.invoke("SubmitAnswer", roomCode, value, timeTaken);
         } catch (error) {
             console.error("Failed to submit:", error);
             // clear waiting state if submit failed
             setWaitingForOthers(false);
             selectedAnswerRef.current = null;
             setSelectedAnswer(null);
         }
     };

     const sendNextQuestion = async () => {
        if (!isHost || !connectionRef.current) return;

        if (currentRound >= MAX_ROUNDS) {
            try {
                await connectionRef.current.invoke("EndGame", roomCode);
            } catch (error) {
                console.error("Failed to end game:", error);
            }
            return;
        }

        // reset local per-question state
        setShowLeaderboard(false);
        setShowResult(false);
        setRoundResults(null);
        setWaitingForOthers(false);
        selectedAnswerRef.current = null;
        setSelectedAnswer(null);

        try {
            const maxAttempts = 3;
            let data: any = null;
            let attempt = 0;
            while (attempt < maxAttempts) {
                attempt++;
                const res = await fetch(`http://${serverIp}:5168/api/game/random`);
                if (!res.ok) {
                    console.warn(`Random API returned ${res.status} on attempt ${attempt}`);
                    continue;
                }
                data = await res.json();

                // basic validation: has pokemonName and correctValue
                const hasName = !!(data && (data.pokemonName || data.PokemonName || data.name));
                const hasCorrect = data && (data.correctValue != null || data.CorrectValue != null || data.correct_value != null);
                const hasOptions = Array.isArray(data.otherValues) && data.otherValues.length > 0 || Array.isArray(data.OtherValues) && data.OtherValues.length > 0;

                if (hasName && hasCorrect && hasOptions) {
                    break; // valid
                }

                console.warn('Invalid question payload from API, retrying...', { attempt, data });
                data = null;
            }

            if (!data) {
                console.warn('Failed to fetch a valid question from API after attempts; server will generate fallback.');
            }

            console.debug('Host invoking SendQuestionToRoom with data:', data);
            // reset questionReceived timestamp before invoking
            questionReceivedRef.current = 0;

            await connectionRef.current.invoke("SendQuestionToRoom", roomCode, data ?? {});

            // wait briefly for clients to receive Question via SignalR; if none received, rehydrate via GetRoomInfo
            setTimeout(async () => {
                const elapsed = Date.now() - (questionReceivedRef.current || 0);
                if (questionReceivedRef.current === 0 || elapsed < 0 || elapsed > 5000) {
                    // no question received within 2s — attempt to rehydrate
                    console.debug('No Question event received within timeout, rehydrating via GetRoomInfo');
                    try {
                        const conn = connectionRef.current!;
                        const info = await conn.invoke('GetRoomInfo', roomCode);
                        console.debug('Rehydrate GetRoomInfo result after SendQuestion:', info);
                        if (info && info.currentQuestion) {
                            const startedAt = info.questionStartedAt ? Date.parse(info.questionStartedAt as string) : NaN;
                            if (!isNaN(startedAt)) handleNewQuestion(info.currentQuestion, startedAt);
                            else handleNewQuestion(info.currentQuestion);
                        }
                    } catch (e) {
                        console.warn('Rehydrate GetRoomInfo failed', e);
                    }
                } else {
                    console.debug('Question event was received by clients (timestamp)', questionReceivedRef.current);
                }
            }, 2000);

        } catch (error) {
            console.error("Failed to send question:", error);
            alert("Failed to load next question");
        }
     };

    // Loading state before first question
    if (!pokemonData && !showLeaderboard) {
        return (
            <View style={styles.container}>
                <Navbar title="Multiplayer Quiz" onBack={handleBack} />
                <View style={styles.content}>
                    <ActivityIndicator size="large" color={colors.primary || "#3b82f6"} />
                    <Text style={styles.loadingText}>
                        {isHost ? "Waiting for you to start..." : "Waiting for host to start..."}
                    </Text>
                    {isHost && (
                        <AppButton label="Start Game" onPress={sendNextQuestion} style={{ marginTop: 20 }} />
                    )}
                </View>
            </View>
        );
    }

    return (
        <View style={styles.container}>
            <Navbar title="Multiplayer Quiz" onBack={handleBack} />
            <View style={styles.header}>
                <Text style={styles.roundText}>
                    Round {currentRound} / {MAX_ROUNDS}
                </Text>
                <View style={[styles.timerContainer, timer <= 5 && styles.timerUrgent]}>
                    <Text style={styles.timerText}>⏱️ {timer} s</Text>
                </View>
            </View>
            {pokemonData && !showLeaderboard && (
                <View style={styles.content}>
                    <Text style={styles.title}>
                        What is {pokemonData.pokemonName}'s{"\n"}
                        {pokemonData.statToGuess}?
                    </Text>
                    <Image source={{ uri: pokemonData.image_Url }} style={styles.pokemonImage} resizeMode="contain" />
                    {showResult && (
                        <View style={[styles.resultBanner, isCorrect ? styles.correctBanner : styles.incorrectBanner]}>
                            <Text style={styles.resultEmoji}>{isCorrect ? "🎉" : "❌"}</Text>
                            <View>
                                <Text style={styles.resultText}>
                                    {isCorrect ? "Correct!" : `Wrong! It's ${pokemonData.correctValue}`}
                                </Text>
                                {isCorrect && <Text style={styles.pointsText}>+{pointsEarned} points</Text>}
                            </View>
                        </View>
                    )}
                    <View style={styles.optionsContainer}>
                        {options.map((item, index) => {
                            const isSelected = selectedAnswer === item.value;
                            const isCorrectOption = item.value === pokemonData!.correctValue;
                            const showCorrect = showResult && isCorrectOption;
                            const showIncorrect = showResult && isSelected && !isCorrect;
                            const btnStyles: Array<StyleProp<ViewStyle>> = [styles.optionButton];
                            if (showCorrect) btnStyles.push(styles.correctButton);
                            if (showIncorrect) btnStyles.push(styles.incorrectButton);
                            return (
                                <AppButton
                                    key={`${item.value}-${index}`}
                                    label={`${item.value}`}
                                    onPress={() => submitAnswer(item.value)}
                                    style={btnStyles as any}
                                    disabled={selectedAnswer !== null || timer === 0}
                                />
                            );
                        })}
                    </View>
                    <View style={styles.playersContainer}>
                        <Text style={styles.playersTitle}>Players:</Text>
                        {players.map((player, i) => (
                            <Text key={i} style={styles.playerText}>
                                {player.answered ? "✓" : "⏳"} {player.name}
                            </Text>
                        ))}
                    </View>
                </View>
            )}
            <Modal visible={showLeaderboard} transparent animationType="slide">
                <View style={styles.modalOverlay}>
                    <View style={styles.modalContent}>
                        <Text style={styles.modalTitle}>🏆 Round Results</Text>
                        {roundResults ? (
                            <ScrollView style={styles.leaderboardScroll}>
                                {roundResults.map((r, i) => (
                                    <View key={i} style={styles.leaderboardRow}>
                                        <Text style={styles.leaderboardName}>{r.name}</Text>
                                        <Text style={r.correct ? styles.correctBanner : styles.incorrectBanner}>
                                            {r.correct ? "✔️" : "❌"} {r.answer !== null && r.answer !== -1 ? r.answer : "No Answer"}
                                        </Text>
                                    </View>
                                ))}
                            </ScrollView>
                        ) : null}
                        <Text style={[styles.modalTitle, { fontSize: 22, marginTop: 10 }]}>Leaderboard</Text>
                        <ScrollView style={styles.leaderboardScroll}>
                            {players.map((player, index) => (
                                <View key={index} style={styles.leaderboardRow}>
                                    <Text style={styles.leaderboardRank}>#{index + 1}</Text>
                                    <Text style={styles.leaderboardName}>{player.name}</Text>
                                    <Text style={styles.leaderboardScore}>{player.score}</Text>
                                </View>
                            ))}
                        </ScrollView>
                        {isHost ? (
                            <AppButton
                                label={currentRound >= MAX_ROUNDS ? "End Game" : "Next Question"}
                                onPress={sendNextQuestion}
                                style={styles.nextButton}
                            />
                        ) : (
                            <Text style={styles.waitingText}>Waiting for host...</Text>
                        )}
                    </View>
                </View>
            </Modal>
        </View>
    );
}

const styles = StyleSheet.create({
    container: {
        flex: 1,
        backgroundColor: colors.background || "#151515",
    },
    content: {
        flex: 1,
        justifyContent: "center",
        alignItems: "center",
        padding: 20,
    },
    header: {
        flexDirection: "row",
        justifyContent: "space-between",
        alignItems: "center",
        padding: 16,
        backgroundColor: colors.primary || "#3b82f6",
    },
    roundText: {
        fontSize: 18,
        fontWeight: "700",
        color: "#fff",
    },
    timerContainer: {
        backgroundColor: "#10b981",
        paddingHorizontal: 16,
        paddingVertical: 8,
        borderRadius: 20,
    },
    timerUrgent: {
        backgroundColor: "#ef4444",
    },
    timerText: {
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
        marginBottom: 20,
    },
    resultBanner: {
        flexDirection: "row",
        alignItems: "center",
        padding: 16,
        borderRadius: 12,
        marginBottom: 20,
        minWidth: "80%",
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
    pointsText: {
        fontSize: 16,
        color: "#fff",
        marginTop: 4,
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
    playersContainer: {
        marginTop: 20,
        padding: 16,
        backgroundColor: colors.surface || "#1f2937",
        borderRadius: 12,
        width: "100%",
        maxWidth: 400,
    },
    playersTitle: {
        fontSize: 16,
        fontWeight: "700",
        marginBottom: 8,
        color: colors.text || "#fff",
    },
    playerText: {
        fontSize: 14,
        color: colors.text || "#fff",
        marginVertical: 4,
    },
    loadingText: {
        marginTop: 16,
        fontSize: 16,
        color: colors.text || "#fff",
        textAlign: "center",
    },
    modalOverlay: {
        flex: 1,
        justifyContent: "center",
        alignItems: "center",
        backgroundColor: "rgba(0,0,0,0.8)",
    },
    modalContent: {
        width: "90%",
        maxHeight: "80%",
        backgroundColor: colors.surface || "#1f2937",
        borderRadius: 16,
        padding: 24,
    },
    modalTitle: {
        fontSize: 28,
        fontWeight: "700",
        textAlign: "center",
        marginBottom: 20,
        color: colors.text || "#fff",
    },
    leaderboardScroll: {
        maxHeight: 400,
        marginBottom: 20,
    },
    leaderboardRow: {
        flexDirection: "row",
        justifyContent: "space-between",
        alignItems: "center",
        paddingVertical: 12,
        paddingHorizontal: 16,
        backgroundColor: colors.background || "#151515",
        borderRadius: 8,
        marginBottom: 8,
    },
    leaderboardRank: {
        fontSize: 18,
        fontWeight: "700",
        color: colors.primary || "#3b82f6",
        width: 40,
    },
    leaderboardName: {
        fontSize: 18,
        fontWeight: "600",
        flex: 1,
        color: colors.text || "#fff",
    },
    leaderboardScore: {
        fontSize: 18,
        fontWeight: "700",
        color: colors.accent || "#fbbf24",
    },
    nextButton: {
        width: "100%",
    },
    waitingText: {
        textAlign: "center",
        fontSize: 16,
        color: colors.muted || "#9ca3af",
    },
});