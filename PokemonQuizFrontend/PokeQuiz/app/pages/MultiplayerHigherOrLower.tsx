// Pokémon Quiz — Page: MultiplayerHigherOrLower
// Standard page header added for consistency. No behavior changes.

import React, { useEffect, useRef, useState } from 'react';
import { View, Text, Image, StyleSheet, ActivityIndicator, Platform, Modal, ScrollView, TouchableOpacity, StatusBar, useWindowDimensions, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter, useLocalSearchParams } from 'expo-router';
import Navbar from '@/components/Navbar';
import AppButton from '@/components/AppButton';
import { ensureConnection, getConnection } from '../../utils/signalrClient';
import * as SignalR from '@microsoft/signalr';
import { colors } from '../../styles/colours';

// -----------------------------------------------------------------------------
// MultiplayerHigherOrLower
//
// Purpose:
// - Multiplayer implementation for the "Higher or Lower" compare-stat game.
// - This file remains in the repository to preserve multiplayer logic and to
//   simplify future reactivation. The screen is temporarily disabled from the
//   app-level navigation to prevent users from accessing this experimental
//   game mode in production builds.
//
// Behavior while disabled:
// - If navigated to directly (e.g. deep link), the page will show a clear
//   "temporarily unavailable" message and provide a safe back action.
// ----------------------------------------------------------------------------/

let Audio: any = null;
try {
  Audio = require('expo-av').Audio;
} catch (e) {
  try { Audio = require('expo-audio').Audio; } catch { Audio = null; }
}

// Helper to normalize compare question payloads to consistent camelCase fields
function normalizeCompareQuestion(data: any) {
  if (!data) return null;
  const aName = data.pokemonAName ?? data.PokemonAName ?? data.pokemonAName ?? data.pokemonName ?? null;
  const aImg = data.pokemonAImageUrl ?? data.PokemonAImageUrl ?? data.pokemonAImageUrl ?? data.image_Url ?? data.imageUrl ?? null;
  const aVal = data.pokemonAValue ?? data.PokemonAValue ?? data.pokemonAValue ?? null;
  const bName = data.pokemonBName ?? data.PokemonBName ?? data.pokemonBName ?? null;
  const bImg = data.pokemonBImageUrl ?? data.PokemonBImageUrl ?? data.pokemonBImageUrl ?? null;
  const bVal = data.pokemonBValue ?? data.PokemonBValue ?? data.pokemonBValue ?? null;
  const stat = data.statToCompare ?? data.StatToCompare ?? data.stat_to_compare ?? data.stat ?? null;

  // If fields are present, return normalized
  if (aName || bName || stat) {
    return {
      pokemonAName: aName ?? null,
      pokemonAImageUrl: aImg ?? null,
      pokemonAValue: aVal ?? null,
      pokemonBName: bName ?? null,
      pokemonBImageUrl: bImg ?? null,
      pokemonBValue: bVal ?? null,
      statToCompare: stat ?? null
    };
  }

  return null;
}

type Player = { name: string; score: number; answered?: boolean; };

export default function MultiplayerHigherOrLower() {
  const router = useRouter();
  const params = useLocalSearchParams();

  // If this page is reached by direct navigation, show a clear disabled UI
  // message. The page remains available in source control for future use.
  const [disabledMessageShown] = useState(true);
  if (disabledMessageShown) {
    const topOffset = Platform.OS === 'android' ? (StatusBar.currentHeight ?? 24) : 0;
    return (
      <SafeAreaView style={[styles.container, { paddingTop: topOffset }]}> 
        <Navbar title={"Multiplayer - Temporarily Unavailable"} onBack={() => { try{ router.replace('/pages/ChooseGame'); } catch { router.push('/pages/ChooseGame'); } }} />
        <View style={{ flex: 1, justifyContent: 'center', alignItems: 'center', padding: 24 }}>
          <Text style={{ fontSize: 24, fontWeight: '800', textAlign: 'center', marginBottom: 12, color: colors.text }}>This multiplayer mode is temporarily unavailable.</Text>
          <Text style={{ textAlign: 'center', color: colors.muted }}>We're preserving the multiplayer implementation in the codebase for future testing and reactivation. Please select another game for now.</Text>
          <View style={{ marginTop: 18, width: '60%' }}>
            <AppButton label='Back to Games' onPress={() => { try{ router.replace('/pages/ChooseGame'); } catch { router.push('/pages/ChooseGame'); } }} />
          </View>
        </View>
      </SafeAreaView>
    );
  }

  // Full implementation retained below for future activation.
  const roomCode = params.roomCode as string;
  const playerName = params.playerName as string;
  const isHost = String(params.isHost ?? '') === 'true';
  const initialQuestionParam = params.initialQuestion as string | undefined;
  const questionStartedAtParam = params.questionStartedAt as string | undefined;

  const serverIp = Platform.OS === 'android' ? '10.0.2.2' : 'localhost';
  const hubUrl = `http://${serverIp}:5168/hubs/game`;

  const connRef = useRef<SignalR.HubConnection | null>(null);
  const [question, setQuestion] = useState<any | null>(null);
  const [players, setPlayers] = useState<Player[]>([]);
  const [timer, setTimer] = useState(20);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const questionStartRef = useRef<number>(0);

  const [showLeaderboard, setShowLeaderboard] = useState(false);
  const [roundResults, setRoundResults] = useState<any[] | null>(null);
  const [localCorrect, setLocalCorrect] = useState<boolean | null>(null);
  const selectedSideRef = useRef<'left'|'right'|'timeout'|null>(null);
  const [selectedSide, setSelectedSideState] = useState<'left'|'right'|'timeout'|null>(null);
  const questionReceivedRef = useRef<number>(0);
  const gameStartedWaitRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // fallback timer for showing leaderboard when server/allanswered is missed
  const showLeaderboardFallbackRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // track current round and maximum rounds
  const [currentRound, setCurrentRound] = useState<number>(0);
  const MAX_ROUNDS = 10;
  const QUESTION_TIME = 20;

  const setSelectedSide = (v: 'left'|'right'|'timeout'|null) => {
    selectedSideRef.current = v;
    setSelectedSideState(v);
  };

  useEffect(() => {
    // If we have an initial question from navigation params, use it immediately
    if (initialQuestionParam) {
      try {
        const parsedQuestion = JSON.parse(initialQuestionParam);
        const startedAt = questionStartedAtParam ? Date.parse(questionStartedAtParam) : Date.now();
        console.debug('Using initialQuestion from route params', parsedQuestion);
        handleNewQuestion(parsedQuestion, startedAt);
      } catch (e) {
        console.warn('Failed to parse initialQuestion param', e);
      }
    }

    setup();
    return () => {
      if (timerRef.current) clearInterval(timerRef.current);
      if (gameStartedWaitRef.current) { clearTimeout(gameStartedWaitRef.current); gameStartedWaitRef.current = null; }
      if (showLeaderboardFallbackRef.current) { clearTimeout(showLeaderboardFallbackRef.current); showLeaderboardFallbackRef.current = null; }
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const playSound = async (correct: boolean) => {
    if (!Audio) return;
    try {
      const soundFile = correct ? require('../../assets/sounds/correct.mp3') : require('../../assets/sounds/incorrect.mp3');
      const { sound } = await Audio.Sound.createAsync(soundFile);
      await sound.playAsync();
      sound.setOnPlaybackStatusUpdate((status: any) => {
        if (status.isLoaded && status.didJustFinish) sound.unloadAsync();
      });
    } catch (e) { console.warn('Sound play failed', e); }
  };

  const handleNewQuestion = (data: any, startAtMs?: number | null) => {
    console.debug('handleNewQuestion called', { data, startAtMs });
    if (gameStartedWaitRef.current) { clearTimeout(gameStartedWaitRef.current); gameStartedWaitRef.current = null; }
    questionReceivedRef.current = Date.now();

    const nq = normalizeCompareQuestion(data) ?? data;
    if (!nq) {
      console.warn('handleNewQuestion failed to normalize', data);
      return;
    }

    // set question and reset per-question state
    setQuestion(nq);
    setShowLeaderboard(false);
    if (showLeaderboardFallbackRef.current) { clearTimeout(showLeaderboardFallbackRef.current); showLeaderboardFallbackRef.current = null; }
    setRoundResults(null);
    setLocalCorrect(null);
    setSelectedSide(null);

    // Simple increment: just add 1 each time, cap at MAX_ROUNDS
    setCurrentRound(prev => Math.min(MAX_ROUNDS, prev + 1));

    const now = Date.now();
    const started = startAtMs ?? now;
    questionStartRef.current = started;

    // compute remaining based on startAtMs
    const elapsedSec = Math.floor((now - started) / 1000);
    const remaining = Math.max(0, QUESTION_TIME - elapsedSec);
    setTimer(remaining);

    if (timerRef.current) clearInterval(timerRef.current as any);
    if (remaining > 0) {
      timerRef.current = setInterval(() => {
        setTimer(t => {
          if (t <= 1) {
            try {
              if (!selectedSideRef.current) {
                setSelectedSide('timeout');
                submit('timeout');
                setShowLeaderboard(true);
                if (showLeaderboardFallbackRef.current) { clearTimeout(showLeaderboardFallbackRef.current); showLeaderboardFallbackRef.current = null; }
              }
            } catch (e) { console.warn('Auto-submit failed', e); }
            if (timerRef.current) clearInterval(timerRef.current as any);
            return 0;
          }
          return t - 1;
        });
      }, 1000) as any;
    }
  };

  const setup = async () => {
    if (!roomCode || !playerName) return;
    try {
      const conn = await ensureConnection(hubUrl);
      if (!conn) {
        console.error('Failed to establish SignalR connection in MultiplayerHigherOrLower');
        Alert.alert('Connection Error', 'Failed to connect to multiplayer server');
        try { router.back(); } catch { }
        return;
      }
      connRef.current = conn;

      try { conn.off('Question'); } catch {}
      try { conn.off('ScoreUpdated'); } catch {}
      try { conn.off('AllAnswered'); } catch {}
      try { conn.off('RoomJoined'); } catch {}
      try { conn.off('GameStarted'); } catch {}
      try { conn.off('GameOver'); } catch {}
      try { conn.off('gameover'); } catch {}

      conn.on('Question', (data: any) => {
        console.debug('Question event received', data);
        if (!data) return;
        handleNewQuestion(data);
      });

      conn.on('ScoreUpdated', (payload: any) => {
        if (payload?.players) setPlayers(payload.players.map((p: any) => ({ name: p.Name ?? p.name, score: p.Score ?? p.score, answered: p.Answered ?? p.answered })));
      });

      conn.on('AllAnswered', (payload: any) => {
        if (payload?.submissions) {
          const results = (payload.submissions as any[]).map((s: any, i: number) => ({
            name: s.data?.playerName ?? s.data?.name ?? `Player ${i+1}`,
            correct: !!s.data?.correct
          }));
          setRoundResults(results);
        }
        if (payload?.leaderboard) setPlayers(payload.leaderboard.map((p:any)=>({ name: p.Name ?? p.name, score: p.Score ?? p.score })));
        if (showLeaderboardFallbackRef.current) { clearTimeout(showLeaderboardFallbackRef.current); showLeaderboardFallbackRef.current = null; }
        // Add 1.5-second delay before showing leaderboard to let players see the results
        setTimeout(() => setShowLeaderboard(true), 1500);
      });

      conn.on('RoomJoined', (data: any) => {
        console.debug('RoomJoined payload', data);
        if (data?.players) setPlayers(data.players);
        if (data?.currentQuestion) {
          const started = data.questionStartedAt ? Date.parse(data.questionStartedAt as string) : NaN;
          if (!isNaN(started)) handleNewQuestion(data.currentQuestion, started);
          else handleNewQuestion(data.currentQuestion);
        }
      });

      conn.on('GameStarted', async (payload: any) => {
        console.debug('GameStarted handler invoked, payload=', payload);
        setShowLeaderboard(false);
        setRoundResults(null);
        
        // Check if payload includes currentQuestion (new format from server)
        if (payload && typeof payload === 'object' && payload.currentQuestion) {
          console.debug('GameStarted included currentQuestion, using it directly');
          const started = payload.questionStartedAt ? Date.parse(payload.questionStartedAt as string) : NaN;
          if (!isNaN(started)) handleNewQuestion(payload.currentQuestion, started);
          else handleNewQuestion(payload.currentQuestion);
        } else {
          // Fallback: if no question in payload, rehydrate via GetRoomInfo
          console.debug('GameStarted missing currentQuestion, rehydrating via GetRoomInfo');
          try {
            const info = await conn.invoke('GetRoomInfo', roomCode);
            if (info && info.currentQuestion) {
              const started = info.questionStartedAt ? Date.parse(info.questionStartedAt as string) : NaN;
              if (!isNaN(started)) handleNewQuestion(info.currentQuestion, started);
              else handleNewQuestion(info.currentQuestion);
            }
          } catch (e) { console.warn('GetRoomInfo failed after GameStarted', e); }
        }
      });

      conn.on('GameOver', (payload: any) => {
        console.debug('GameOver payload', payload);
        try {
          const leaderboard = payload?.leaderboard ?? payload;
          const rc = payload?.roomCode ?? roomCode;
          const leaderboardStr = JSON.stringify(leaderboard);
          try { router.replace({ pathname: '/pages/GameOver', params: { leaderboard: leaderboardStr, roomCode: rc } } as any); } catch { router.push({ pathname: '/pages/GameOver', params: { leaderboard: leaderboardStr, roomCode: rc } } as any); }
        } catch (e) { console.warn('GameOver navigation failed', e); }
      });

      conn.on('gameover', (payload: any) => {
        console.debug('gameover payload', payload);
        try {
          const leaderboard = payload?.leaderboard ?? payload;
          const rc = payload?.roomCode ?? roomCode;
          const leaderboardStr = JSON.stringify(leaderboard);
          try { router.replace({ pathname: '/pages/GameOver', params: { leaderboard: leaderboardStr, roomCode: rc } } as any); } catch { router.push({ pathname: '/pages/GameOver', params: { leaderboard: leaderboardStr, roomCode: rc } } as any); }
        } catch (e) { console.warn('gameover navigation failed', e); }
      });

      // initial hydrate
      try {
        // conn is non-null here
        const info = await conn.invoke('GetRoomInfo', roomCode);
        console.debug('Initial GetRoomInfo result:', info);
        if (info?.players) setPlayers(info.players);
        if (info?.currentQuestion) {
          const started = info.questionStartedAt ? Date.parse(info.questionStartedAt as string) : NaN;
          if (!isNaN(started)) handleNewQuestion(info.currentQuestion, started);
          else handleNewQuestion(info.currentQuestion);
        }

        if (!info?.gameStarted) {
          try { await conn.invoke('JoinRoom', roomCode, playerName); } catch (e) { console.warn('JoinRoom failed', e); }
        }
      } catch (e) { console.warn('GetRoomInfo failed', e); }

    } catch (e) { console.warn('connection failed', e); }
  };

  const submit = async (side: 'left'|'right'|'timeout') => {
    if (!connRef.current || !question) return;
    if (selectedSideRef.current) return;

    setSelectedSide(side === 'timeout' ? 'timeout' : side);

    // mark local player as answered for UI
    setPlayers(prev => prev.map(p => p.name === playerName ? { ...p, answered: true } : p));

    const a = Number(question.pokemonAValue || 0);
    const b = Number(question.pokemonBValue || 0);
    let correctSide: 'left'|'right'|'either' = 'either';
    if (a > b) correctSide = 'left';
    else if (b > a) correctSide = 'right';

    const isCorrect = correctSide === 'either' ? true : (side === correctSide);
    setLocalCorrect(isCorrect);

    try { playSound(isCorrect); } catch {}

    const now = Date.now();
    const timeTaken = now - (questionStartRef.current || now);

    try {
      await connRef.current.invoke('SubmitCompareAnswer', roomCode, side, timeTaken);

      if (side === 'timeout') setShowLeaderboard(true);

      if (showLeaderboardFallbackRef.current) { clearTimeout(showLeaderboardFallbackRef.current); showLeaderboardFallbackRef.current = null; }
      showLeaderboardFallbackRef.current = setTimeout(async () => {
        try {
          const info = await connRef.current!.invoke('GetRoomInfo', roomCode);
          if (info && info.currentQuestion) setShowLeaderboard(true);
        } catch (e) { console.warn('Leaderboard fallback GetRoomInfo failed', e); }
      }, 2500) as any;

    } catch (err: any) {
      console.warn('submit failed', err);
      setSelectedSide(null);
      setLocalCorrect(null);
      setPlayers(prev => prev.map(p => p.name === playerName ? { ...p, answered: false } : p));
    }
  };

  const sendNext = async () => {
    if (!isHost) return;
    const conn = connRef.current ?? await ensureConnection(hubUrl);
    if (!conn) return;

    try {
      questionReceivedRef.current = 0;
      // Don't send empty {} - let server generate question
      await conn.invoke('SendQuestionToRoom', roomCode, null);

      setTimeout(async () => {
        const elapsed = Date.now() - (questionReceivedRef.current || 0);
        if (questionReceivedRef.current === 0 || elapsed > 5000) {
          try {
            const info = await conn.invoke('GetRoomInfo', roomCode);
            if (info && info.currentQuestion) {
              const started = info.questionStartedAt ? Date.parse(info.questionStartedAt as string) : NaN;
              if (!isNaN(started)) handleNewQuestion(info.currentQuestion, started);
              else handleNewQuestion(info.currentQuestion);
            }
          } catch (e) { console.warn('Rehydrate after SendQuestion failed', e); }
        }
      }, 2000);

    } catch (e) { console.warn('SendQuestionToRoom failed', e); }
  };

  const handleLeaveOrDisband = async (disband: boolean) => {
    const conn = connRef.current ?? getConnection();
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
    try { router.replace('/pages/ChooseGame'); } catch { router.push('/'); }
  };

  const handleBack = async () => {
    try {
      if (connRef.current && roomCode) {
        try { await connRef.current.invoke('LeaveRoom', roomCode); } catch (e) { /* ignore */ }
        try { await connRef.current.stop(); } catch { }
      }
    } catch { }
    try { router.replace('/pages/ChooseGame'); } catch { router.push('/'); }
  };

  const { width: winW } = useWindowDimensions();
  const imgSize = Math.min(180, Math.round(winW * 0.42));

  const ready = !!question;
  if (!ready) {
    return (
      <View style={styles.container}>
        <Navbar title={"Multiplayer - Higher or Lower"} onBack={handleBack} />
        <View style={styles.mainContent}>
          <ActivityIndicator size='large' color={colors.primary} />
          <Text style={{ color: colors.text, marginTop: 12 }}>{isHost ? 'Waiting to generate question...' : 'Waiting for host...'}</Text>
          {isHost && <AppButton label='Send Question' onPress={sendNext} style={{ marginTop: 12 }} />}
        </View>
      </View>
    );
  }

  const leftName = question.pokemonAName;
  const rightName = question.pokemonBName;
  const stat = question.statToCompare;
  const leftImg = question.pokemonAImageUrl;
  const rightImg = question.pokemonBImageUrl;

  const leftStatValue = question.pokemonAValue != null ? Number(question.pokemonAValue) : null;
  const rightStatValue = question.pokemonBValue != null ? Number(question.pokemonBValue) : null;

  const leftHigher = leftStatValue != null && rightStatValue != null && leftStatValue > rightStatValue;
  const rightHigher = leftStatValue != null && rightStatValue != null && rightStatValue > leftStatValue;

  const isCorrectLeft = selectedSide && localCorrect && selectedSide === 'left';
  const isCorrectRight = selectedSide && localCorrect && selectedSide === 'right';
  const isIncorrectLeft = selectedSide && !localCorrect && selectedSide === 'left';
  const isIncorrectRight = selectedSide && !localCorrect && selectedSide === 'right';

  const leftStatStyles = [styles.statValue, leftHigher ? styles.statValueWinner : styles.statValueNormal];
  const rightStatStyles = [styles.statValue, rightHigher ? styles.statValueWinner : styles.statValueNormal];

  if (selectedSide && (isCorrectLeft || isIncorrectLeft)) leftStatStyles.push(styles.statValueOnPrimary);
  if (selectedSide && (isCorrectRight || isIncorrectRight)) rightStatStyles.push(styles.statValueOnPrimary);

  const topOffset = Platform.OS === 'android' ? (StatusBar.currentHeight ?? 24) : 0;

  const sortedPlayers = [...players].sort((a, b) => (b.score ?? 0) - (a.score ?? 0));

  return (
    <SafeAreaView style={[styles.container, { paddingTop: topOffset }]}> 
      <Navbar title={`Multiplayer - Higher or Lower`} onBack={handleBack} />
      <ScrollView contentContainerStyle={{ paddingBottom: 40 }}> 
        <View style={styles.header}>
          <Text style={styles.roundText}>Round {currentRound || 1} / {MAX_ROUNDS}</Text>
          <View style={[styles.timerContainer, timer <= 5 && styles.timerUrgent]}>
            <Text style={styles.timerText}>Timer: {timer}s</Text>
          </View>
        </View>
        <View style={styles.content}>
          <Text style={styles.questionTitle}>Which Pokemon has higher {stat || ''}?</Text>
          <View style={styles.row}>
            <TouchableOpacity
              style={[
                styles.card,
                isCorrectLeft ? styles.cardCorrect : null,
                isIncorrectLeft ? styles.cardIncorrect : null
              ]}
              onPress={() => submit('left')}
              disabled={!!selectedSide}
              activeOpacity={0.85}
            >
              <Image source={{ uri: leftImg || '' }} style={[styles.image, { width: imgSize, height: imgSize }]} resizeMode='contain' />
              <Text style={[styles.name, isCorrectLeft || isIncorrectLeft ? styles.nameOnPrimary : null]} numberOfLines={2}>{leftName || ''}</Text>
              {selectedSide ? <Text style={leftStatStyles}>{`${stat || ''}: ${leftStatValue ?? '?'}`}</Text> : null}
            </TouchableOpacity>

            <TouchableOpacity
              style={[
                styles.card,
                isCorrectRight ? styles.cardCorrect : null,
                isIncorrectRight ? styles.cardIncorrect : null
              ]}
              onPress={() => submit('right')}
              disabled={!!selectedSide}
              activeOpacity={0.85}
            >
              <Image source={{ uri: rightImg || '' }} style={[styles.image, { width: imgSize, height: imgSize }]} resizeMode='contain' />
              <Text style={[styles.name, isCorrectRight || isIncorrectRight ? styles.nameOnPrimary : null]} numberOfLines={2}>{rightName || ''}</Text>
              {selectedSide ? <Text style={rightStatStyles}>{`${stat || ''}: ${rightStatValue ?? '?'}`}</Text> : null}
            </TouchableOpacity>
          </View>

          <View style={styles.playersContainer}>
            <Text style={styles.playersTitle}>Players:</Text>
            {players.map((player, i) => (
              <Text key={i} style={styles.playerText} accessibilityLabel={`${player.name} ${player.answered ? 'answered' : 'waiting'}`}>
                {player.answered ? "?" : "?"} {player.name}
              </Text>
            ))}
          </View>

          <Modal visible={showLeaderboard} transparent animationType='slide'>
            <View style={styles.modalOverlay}>
              <View style={styles.modalContent}>
                <Text style={styles.modalTitle}>?? Round Results</Text>
                {roundResults ? (
                  <ScrollView style={styles.leaderboardScroll}>
                    {roundResults.map((r,i)=>
                      <View key={i} style={styles.leaderboardRowSmall}>
                        <Text style={styles.leaderboardNameSmall}>{r.name}</Text>
                        <Text style={r.correct ? styles.correctText : styles.incorrectText}>{r.correct ? '??' : '?'}</Text>
                      </View>
                    )}
                  </ScrollView>
                ) : null}
                <Text style={[styles.modalTitle, { fontSize: 22, marginTop: 10 }]}>Leaderboard</Text>
                <ScrollView style={styles.leaderboardScroll}>
                  {sortedPlayers.map((player, index) => (
                    <View key={index} style={[styles.leaderboardRow, index === 0 ? styles.topPlayerRow : null]}>
                      <Text style={styles.leaderboardRank}>{index === 0 ? '??' : `#${index + 1}`}</Text>
                      <Text style={[styles.leaderboardName, index === 0 ? styles.topPlayerName : null]}>{player.name}</Text>
                      <View style={styles.scorePill}><Text style={styles.leaderboardScore}>{player.score}</Text></View>
                    </View>
                  ))}
                </ScrollView>
                <View style={{ marginTop: 12 }}>
                  {isHost ? (
                    <AppButton label={currentRound >= MAX_ROUNDS ? 'End Game' : 'Next Question'} onPress={() => { setShowLeaderboard(false); sendNext(); }} style={{ width: '100%' }} />
                  ) : (
                    <Text style={styles.waitingText}>Waiting for host...</Text>
                  )}
                </View>
                <View style={styles.modalActionContainer}>
                  <AppButton label='Leave' onPress={() => handleLeaveOrDisband(false)} style={styles.leaveButton} />
                </View>
              </View>
            </View>
          </Modal>

        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  mainContent: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  header: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', padding: 12, backgroundColor: colors.surface, borderBottomWidth: 1, borderBottomColor: colors.grey },
  roundText: { fontSize: 18, fontWeight: '700', color: colors.text },
  timerContainer: { backgroundColor: colors.primaryLight, paddingHorizontal: 16, paddingVertical: 8, borderRadius: 20 },
  timerUrgent: { backgroundColor: colors.error },
  timerText: { fontSize: 16, fontWeight: '700', color: colors.text },
  content: { flex: 1, padding: 16, alignItems: 'center' },
  questionTitle: { fontSize: 22, fontWeight: '900', color: colors.text, marginBottom: 12, textAlign: 'center' },
  row: { flexDirection: 'row', width: '100%', justifyContent: 'space-between', marginTop: 12 },
  card: { alignItems: 'center', width: '48%', backgroundColor: colors.surface, padding: 12, borderRadius: 16, borderWidth: 1, borderColor: colors.grey },
  cardCorrect: { backgroundColor: '#22c55e', borderColor: '#1ca34d', borderWidth: 2 },
  cardIncorrect: { backgroundColor: '#ef4444', borderColor: '#c43a34', borderWidth: 2 },
  image: { width: 160, height: 160, borderRadius: 12, backgroundColor: 'rgba(0,0,0,0.05)' },
  name: { color: colors.text, fontWeight: '800', marginVertical: 12, textAlign: 'center', fontSize: 18 },
  nameOnPrimary: { color: colors.white },
  statValue: { marginTop: 8, fontWeight: '900', fontSize: 16, color: colors.text },
  statValueWinner: { color: '#22c55e' },
  statValueNormal: { color: colors.muted },
  statValueOnPrimary: { color: colors.white },
  playersContainer: { marginTop: 20, padding: 16, backgroundColor: colors.surface, borderRadius: 16, width: '100%', maxWidth: 400, borderWidth: 1, borderColor: colors.grey },
  playersTitle: { fontSize: 16, fontWeight: '700', marginBottom: 8, color: colors.text },
  playerText: { fontSize: 14, color: colors.text, marginVertical: 4 },
  modalOverlay: { flex: 1, justifyContent: 'center', alignItems: 'center', backgroundColor: 'rgba(0,0,0,0.7)' },
  modalContent: { width: '90%', maxHeight: '80%', backgroundColor: colors.surface, padding: 20, borderRadius: 16, borderWidth: 1, borderColor: colors.grey },
  modalTitle: { fontSize: 24, fontWeight: '900', textAlign: 'center', marginBottom: 12, color: colors.text },
  leaderboardScroll: { maxHeight: 400, marginBottom: 12 },
  leaderboardRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', paddingVertical: 10, paddingHorizontal: 12, backgroundColor: colors.surface, borderRadius: 10, marginBottom: 8, borderWidth: 1, borderColor: colors.grey },
  topPlayerRow: { backgroundColor: colors.primaryLight },
  leaderboardRank: { fontSize: 18, fontWeight: '700', color: colors.primary, width: 48, textAlign: 'center' },
  leaderboardName: { fontSize: 16, fontWeight: '600', flex: 1, color: colors.text },
  topPlayerName: { color: colors.text, fontSize: 18, fontWeight: '800' },
  scorePill: { backgroundColor: colors.primary, paddingHorizontal: 10, paddingVertical: 4, borderRadius: 16 },
  leaderboardScore: { fontSize: 16, fontWeight: '700', color: colors.white },
  leaderboardRowSmall: { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 6, paddingHorizontal: 6, marginBottom: 6 },
  leaderboardNameSmall: { color: colors.text },
  correctText: { color: '#22c55e', fontWeight: '700' },
  incorrectText: { color: '#ef4444', fontWeight: '700' },
  waitingText: { textAlign: 'center', fontSize: 16, color: colors.muted },
  modalActionContainer: { marginTop: 14, alignItems: 'center', justifyContent: 'center' },
  leaveButton: { width: '60%', backgroundColor: '#ef4444', paddingVertical: 12, borderRadius: 8 }
});
