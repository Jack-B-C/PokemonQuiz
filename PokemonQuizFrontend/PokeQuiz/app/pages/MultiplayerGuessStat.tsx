// Pokémon Quiz — Page: MultiplayerGuessStat
// Standard page header added for consistency. No behavior changes.

import React, { useState, useEffect, useRef } from 'react';
import { View, Text, StyleSheet, Image, Platform, ActivityIndicator, Modal, ScrollView, StyleProp, ViewStyle, TouchableOpacity, useWindowDimensions, Alert } from "react-native";
import { SafeAreaView } from 'react-native-safe-area-context';
import { colors } from "../../styles/colours";
import Navbar from "@/components/Navbar";
import AppButton from "@/components/AppButton";
import { useRouter, useLocalSearchParams } from "expo-router";
import * as SignalR from "@microsoft/signalr";
import { ensureConnection, getConnection } from '../../utils/signalrClient';

let Audio: any = null;
try {
    // Attempt dynamic import of new packages if present; fall back gracefully
    Audio = require('expo-av').Audio;
} catch (e) {
    try { Audio = (require('expo-audio').Audio); } catch { Audio = null; }
}

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
    const { width: winW } = useWindowDimensions();
    const dynamicImgSize = Math.min(220, Math.round(winW * 0.6));

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
    const showLeaderboardFallbackRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const allAnsweredReceivedRef = useRef<boolean>(false); // Track if AllAnswered was received this round

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
            if (showLeaderboardFallbackRef.current) { clearTimeout(showLeaderboardFallbackRef.current); showLeaderboardFallbackRef.current = null; }
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, ['/' + roomCode, playerName]);

    const setupConnection = async () => {
        if (!roomCode || !playerName) return;
        const hubUrl = `http://${serverIp}:5168/hubs/game`;
        try {
            const connection = await ensureConnection(hubUrl);
            if (!connection) {
                console.error('Failed to establish SignalR connection in MultiplayerGuessStat');
                Alert.alert('Connection Error', 'Failed to connect to multiplayer server');
                try { router.back(); } catch { }
                return;
            }
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
                
                // Mark that we received AllAnswered - this prevents duplicate leaderboard shows
                if (allAnsweredReceivedRef.current) {
                    console.debug('AllAnswered already processed for this round, ignoring duplicate');
                    return;
                }
                allAnsweredReceivedRef.current = true;
                
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
                
                // clear fallback if set
                if (showLeaderboardFallbackRef.current) { 
                    clearTimeout(showLeaderboardFallbackRef.current); 
                    showLeaderboardFallbackRef.current = null; 
                }
                
                // Add 1-second delay before showing leaderboard to let players see the results
                setTimeout(() => setShowLeaderboard(true), 1000);
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

            // Ensure clients close leaderboard and rehydrate when a new game is started
            connection.on("GameStarted", async (payload: any) => {
                console.debug('SignalR GameStarted payload:', payload);
                try {
                    // Close any open leaderboard modal
                    setShowLeaderboard(false);
                    setShowResult(false);
                    setWaitingForOthers(false);

                    // Check if payload includes currentQuestion (new format from server)
                    if (payload && typeof payload === 'object' && payload.currentQuestion) {
                        console.debug('GameStarted included currentQuestion, using it directly');
                        const startedAt = payload.questionStartedAt ? Date.parse(payload.questionStartedAt as string) : NaN;
                        if (!isNaN(startedAt)) handleNewQuestion(payload.currentQuestion, startedAt);
                        else handleNewQuestion(payload.currentQuestion);
                    } else {
                        // Fallback: if no question in payload (old-style string gameId), rehydrate via GetRoomInfo
                        console.debug('GameStarted missing currentQuestion, rehydrating via GetRoomInfo');
                        try {
                            const info = await connection.invoke('GetRoomInfo', roomCode);
                            if (info && info.currentQuestion) {
                                const startedAt = info.questionStartedAt ? Date.parse(info.questionStartedAt as string) : NaN;
                                if (!isNaN(startedAt)) handleNewQuestion(info.currentQuestion, startedAt);
                                else handleNewQuestion(info.currentQuestion);
                            }
                        } catch (e) {
                            console.warn('Failed to rehydrate room after GameStarted', e);
                        }
                    }

                    // Navigate to correct multiplayer page based on payload (game id)
                    const gameId = (payload && typeof payload === 'object') ? payload.gameId : payload;
                    if (gameId === 'higher-or-lower' || gameId === 'compare-stat') {
                        try { router.replace({ pathname: '/pages/MultiplayerHigherOrLower', params: { roomCode, playerName, isHost: isHost ? 'true' : 'false' } } as any); } catch { router.push({ pathname: '/pages/MultiplayerHigherOrLower', params: { roomCode, playerName } } as any); }
                        return;
                    }

                } catch (e) {
                    console.warn('Error handling GameStarted', e);
                }
            });
            connection.on("gamestarted", async (payload: any) => {
                console.debug('SignalR gamestarted payload:', payload);
                try {
                    setShowLeaderboard(false);
                    setShowResult(false);
                    setWaitingForOthers(false);

                    // Check if payload includes currentQuestion (new format from server)
                    if (payload && typeof payload === 'object' && payload.currentQuestion) {
                        console.debug('gamestarted included currentQuestion, using it directly');
                        const startedAt = payload.questionStartedAt ? Date.parse(payload.questionStartedAt as string) : NaN;
                        if (!isNaN(startedAt)) handleNewQuestion(payload.currentQuestion, startedAt);
                        else handleNewQuestion(payload.currentQuestion);
                    } else {
                        // Fallback: rehydrate via GetRoomInfo
                        console.debug('gamestarted missing currentQuestion, rehydrating via GetRoomInfo');
                        const info = await connection.invoke('GetRoomInfo', roomCode);
                        if (info && info.currentQuestion) {
                            const startedAt = info.questionStartedAt ? Date.parse(info.questionStartedAt as string) : NaN;
                            if (!isNaN(startedAt)) handleNewQuestion(info.currentQuestion, startedAt);
                            else handleNewQuestion(info.currentQuestion);
                        }
                    }

                    const gameId = (payload && typeof payload === 'object') ? payload.gameId : payload;
                    if (gameId === 'higher-or-lower' || gameId === 'compare-stat') {
                        try { router.replace({ pathname: '/pages/MultiplayerHigherOrLower', params: { roomCode, playerName, isHost: isHost ? 'true' : 'false' } } as any); } catch { router.push({ pathname: '/pages/MultiplayerHigherOrLower', params: { roomCode, playerName } } as any); }
                        return;
                    }

                } catch (e) {
                    console.warn('Error handling gamestarted', e);
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
        allAnsweredReceivedRef.current = false; // Reset for new round

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
                            // Don't show leaderboard here - wait for AllAnswered from server
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
             if (Audio) {
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
             }
         } catch (error) {
             console.warn("Sound failed", error);
         }

         try {
             await connectionRef.current.invoke("SubmitAnswer", roomCode, value, timeTaken);

             // Failsafe: if AllAnswered doesn't arrive within 3 seconds, show leaderboard anyway
             if (showLeaderboardFallbackRef.current) { clearTimeout(showLeaderboardFallbackRef.current); showLeaderboardFallbackRef.current = null; }
             showLeaderboardFallbackRef.current = setTimeout(() => {
                 if (!allAnsweredReceivedRef.current) {
                     console.warn('AllAnswered not received, showing leaderboard as failsafe');
                     setShowLeaderboard(true);
                 }
             }, 3000) as any;

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
        allAnsweredReceivedRef.current = false; // Reset for new round

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

            await connectionRef.current.invoke("SendQuestionToRoom", roomCode, data);

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

    // Leave or disband - placed inside component so it can access roomCode and router
    const handleLeaveOrDisband = async (disband: boolean) => {
        const conn = getConnection();
        try {
            if (conn) {
                try { await conn.invoke('LeaveRoom', roomCode); } catch { }
                if (disband) {
                    try { await conn.invoke('EndGame', roomCode); } catch { }
                }
                await conn.stop();
            }
        } catch (e) {
            console.warn('Leave/disband failed', e);
        }
        // navigate out
        try { router.replace('/pages/ChooseGame'); } catch { router.push('/'); }
    };

    // Loading state before first question
    if (!pokemonData && !showLeaderboard) {
        return (
            <View style={styles.container}>
                <Navbar title="Multiplayer Quiz" onBack={handleBack} backTo={'/pages/ChooseGame'} />
                <View style={styles.mainContent}>
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

    // sort players for leaderboard display
    const sortedPlayers = [...players].sort((a, b) => (b.score ?? 0) - (a.score ?? 0));

    return (
        <SafeAreaView style={styles.container}>
            <Navbar title="Multiplayer Quiz" onBack={handleBack} />
            <ScrollView contentContainerStyle={{ paddingBottom: 40 }}>
            <View style={styles.header}>
                <Text style={styles.roundText} accessibilityRole="header">
                    Round {currentRound} / {MAX_ROUNDS}
                </Text>
                <View style={[styles.timerContainer, timer <= 5 && styles.timerUrgent]}>
                    <Text style={styles.timerText}>⏱️ {timer} s</Text>
                </View>
            </View>

            <View style={styles.mainContent}>
                {pokemonData && !showLeaderboard && (
                    <View style={styles.contentInner}>
                        <Text style={styles.title} accessibilityRole="header" numberOfLines={3}>
                            What is {pokemonData.pokemonName}'s{"\n"}
                            {pokemonData.statToGuess}?
                        </Text>
                        <Image source={{ uri: pokemonData.image_Url }} style={[styles.pokemonImage, { width: dynamicImgSize, height: dynamicImgSize }]} resizeMode="contain" />
                        {showResult && (
                            <View style={[styles.resultBanner, isCorrect ? styles.correctBanner : styles.incorrectBanner]}>
                                <Text style={styles.resultEmoji}>{isCorrect ? "🎉" : "❌"}</Text>
                                <View>
                                    <Text style={styles.resultText} numberOfLines={2}>
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
                                <Text key={i} style={styles.playerText} accessibilityLabel={`${player.name} ${player.answered ? 'answered' : 'waiting'}`}>
                                    {player.answered ? "✓" : "⏳"} {player.name}
                                </Text>
                            ))}
                        </View>
                    </View>
                )}
            </View>
            </ScrollView>

            <Modal visible={showLeaderboard} transparent animationType="slide">
                <View style={styles.modalOverlay}>
                    <View style={styles.modalContent}>
                        <Text style={styles.modalTitle}>🏆 Round Results</Text>
                        {roundResults ? (
                            <ScrollView style={styles.leaderboardScroll}>
                                {roundResults.map((r, i) => (
                                    <View key={i} style={styles.leaderboardRowSmall}>
                                        <Text style={styles.leaderboardNameSmall}>{r.name}</Text>
                                        <Text style={r.correct ? styles.correctText : styles.incorrectText}>
                                            {r.correct ? "✔️" : "❌"} {r.answer !== null && r.answer !== -1 ? r.answer : "No Answer"}
                                        </Text>
                                    </View>
                                ))}
                            </ScrollView>
                        ) : null}

                        <Text style={[styles.modalTitle, { fontSize: 22, marginTop: 10 }]}>Leaderboard</Text>
                        <ScrollView style={styles.leaderboardScroll}>
                            {sortedPlayers.map((player, index) => (
                                <View key={index} style={[styles.leaderboardRow, index === 0 ? styles.topPlayerRow : null]}>
                                    <Text style={styles.leaderboardRank}>{index === 0 ? '🥇' : `#${index + 1}`}</Text>
                                    <Text style={[styles.leaderboardName, index === 0 ? styles.topPlayerName : null]}>{player.name}</Text>
                                    <View style={styles.scorePill}>
                                        <Text style={styles.leaderboardScore}>{player.score}</Text>
                                    </View>
                                </View>
                            ))}
                        </ScrollView>

                        <View style={{ marginTop: 20, alignItems: 'center' }}>
                            {isHost && currentRound < MAX_ROUNDS ? (
                                <AppButton
                                    label="Next Question"
                                    onPress={() => { setShowLeaderboard(false); sendNextQuestion(); }}
                                    style={{ width: '80%', marginBottom: 12 }}
                                />
                            ) : (
                                <Text style={{ color: colors.text, textAlign: 'center', marginVertical: 12 }}>
                                    {isHost ? 'Game Complete!' : 'Waiting for host...'}
                                </Text>
                            )}
                            <TouchableOpacity style={{ marginTop: 8 }} onPress={() => handleLeaveOrDisband(false)}>
                                <Text style={{ color: colors.error, textAlign: 'center', fontWeight: '700' }}>
                                    Leave Room
                                </Text>
                            </TouchableOpacity>
                        </View>
                    </View>
                </View>
            </Modal>
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, backgroundColor: colors.background },
    mainContent: { flex: 1, justifyContent: "center", alignItems: "center", padding: 20 },
    loadingText: { marginTop: 12, fontSize: 16, color: colors.text },
    header: { padding: 16, backgroundColor: colors.surface, borderBottomWidth: 1, borderBottomColor: colors.grey },
    roundText: { fontSize: 18, fontWeight: "800", color: colors.text, marginBottom: 8, textAlign: "center" },
    timerContainer: { backgroundColor: colors.primaryLight, paddingHorizontal: 12, paddingVertical: 6, borderRadius: 16, alignSelf: "center", marginTop: 8 },
    timerUrgent: { backgroundColor: colors.error },
    timerText: { fontSize: 16, fontWeight: "700", color: colors.text },
    questionText: { fontSize: 18, fontWeight: "800", color: colors.text, marginBottom: 8, textAlign: "center" },
    statsRow: { flexDirection: "row", justifyContent: "space-around", marginTop: 8 },
    statChip: { backgroundColor: colors.primaryLight, paddingHorizontal: 12, paddingVertical: 6, borderRadius: 16 },
    statText: { fontSize: 14, fontWeight: "700", color: colors.text },
    content: { flex: 1, padding: 20 },
    contentInner: { width: "100%", alignItems: "center" },
    pokemonCard: { alignItems: "center", marginBottom: 20 },
    title: { fontSize: 22, fontWeight: "900", color: colors.text, marginBottom: 12, textAlign: "center" },
    pokemonName: { fontSize: 24, fontWeight: "900", color: colors.text, marginBottom: 12 },
    pokemonImage: { width: 200, height: 200, marginBottom: 20 },
    image: { width: 200, height: 200, marginBottom: 20 },
    resultBanner: { flexDirection: "row", alignItems: "center", padding: 12, borderRadius: 8, marginBottom: 16 },
    correctBanner: { backgroundColor: "#10b98133" },
    incorrectBanner: { backgroundColor: "#ef444433" },
    resultEmoji: { fontSize: 32, marginRight: 12 },
    pointsText: { fontSize: 14, fontWeight: "700", color: colors.primary, marginTop: 4 },
    optionsContainer: { width: "100%", marginBottom: 20 },
    optionButton: { width: "100%", padding: 15, marginVertical: 5, borderRadius: 10, elevation: 2 },
    optionButtonText: { fontSize: 18, fontWeight: "600", color: colors.text, textAlign: "center" },
    selectedOption: { borderWidth: 3, borderColor: colors.primary },
    correctButton: { backgroundColor: "#10b981" },
    incorrectButton: { backgroundColor: "#ef4444" },
    correctOption: { backgroundColor: "#10b981" },
    incorrectOption: { backgroundColor: "#ef4444" },
    resultText: { fontSize: 18, fontWeight: "700", marginTop: 10, textAlign: "center", color: colors.text },
    playersContainer: { marginTop: 20, width: "100%", padding: 12, backgroundColor: colors.surface, borderRadius: 8 },
    playersTitle: { fontSize: 16, fontWeight: "700", color: colors.text, marginBottom: 8 },
    playerText: { fontSize: 14, color: colors.text, paddingVertical: 4 },
    playersHeader: { fontSize: 16, fontWeight: "700", color: colors.text, marginTop: 20, marginBottom: 8 },
    playersList: { maxHeight: 120 },
    playerRow: { flexDirection: "row", justifyContent: "space-between", paddingVertical: 6, borderBottomWidth: 1, borderBottomColor: colors.grey },
    playerName: { fontSize: 14, color: colors.text, flex: 1 },
    playerScore: { fontSize: 14, fontWeight: "700", color: colors.primary },
    modalOverlay: { flex: 1, backgroundColor: "rgba(0,0,0,0.6)", justifyContent: "center", alignItems: "center" },
    modalContent: { width: "90%", maxHeight: "80%", backgroundColor: colors.surface, borderRadius: 16, padding: 20 },
    modalCard: { width: "90%", maxHeight: "80%", backgroundColor: colors.surface, borderRadius: 16, padding: 20 },
    modalTitle: { fontSize: 24, fontWeight: "900", color: colors.text, marginBottom: 12, textAlign: "center" },
    leaderboardScroll: { maxHeight: 300, marginVertical: 12 },
    leaderboardRow: { flexDirection: "row", alignItems: "center", paddingVertical: 10, borderBottomWidth: 1, borderBottomColor: colors.grey },
    topPlayerRow: { backgroundColor: colors.primaryLight, borderRadius: 8, paddingHorizontal: 8 },
    leaderboardRank: { fontSize: 18, fontWeight: "800", color: colors.text, width: 40 },
    leaderboardName: { flex: 1, fontSize: 16, fontWeight: "600", color: colors.text },
    topPlayerName: { fontWeight: "900" },
    scorePill: { backgroundColor: colors.primary, paddingHorizontal: 12, paddingVertical: 4, borderRadius: 12 },
    leaderboardScore: { fontSize: 16, fontWeight: "800", color: colors.white },
    roundResultsContainer: { marginBottom: 16 },
    roundResultRow: { flexDirection: "row", justifyContent: "space-between", paddingVertical: 6, borderBottomWidth: 1, borderBottomColor: colors.grey },
    roundResultName: { fontSize: 14, color: colors.text, flex: 1 },
    roundResultStatus: { fontSize: 14, fontWeight: "700" },
    correctStatus: { color: "#10b981" },
    incorrectStatus: { color: "#ef4444" },
    leaderboardRowSmall: { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 6, marginBottom: 4 },
    leaderboardNameSmall: { fontSize: 14, color: colors.text, flex: 1 },
    correctText: { color: '#10b981', fontWeight: '700' },
    incorrectText: { color: '#ef4444', fontWeight: '700' }
});