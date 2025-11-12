import React, { useState, useEffect, useRef } from 'react';
import { View, Text, Image, StyleSheet, Platform, ActivityIndicator, Alert, TouchableOpacity, StatusBar, ScrollView, useWindowDimensions } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import Navbar from '@/components/Navbar';
import { Audio } from 'expo-av';
import { useRouter } from 'expo-router';
import AppButton from '@/components/AppButton';
import { colors } from '../../styles/colours';

export default function HigherOrLowerSingle() {
  const router = useRouter();
  const serverIp = Platform.OS === 'android' ? '10.0.2.2' : 'localhost';
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [question, setQuestion] = useState<any | null>(null);
  const [loading, setLoading] = useState(false);
  const [points, setPoints] = useState(0);
  const [correctCount, setCorrectCount] = useState(0);
  const [index, setIndex] = useState(0);
  const [showResult, setShowResult] = useState<{ correct: boolean; name?: string; aValue?: number; bValue?: number } | null>(null);
  const [lastSelected, setLastSelected] = useState<'left' | 'right' | null>(null);
  const [answered, setAnswered] = useState(false);
  const answeredRef = useRef(false);
  const MAX_QUESTIONS = 10;
  const { width: winW, height: winH } = useWindowDimensions();
  const imageHeight = Math.min(260, Math.round(winW * 0.55));

  const startSession = async () => {
    setLoading(true);
    try {
      const token = (global as any).userToken as string | undefined;
      const headers: Record<string, string> = { 'Content-Type': 'application/json' };
      if (token) headers['Authorization'] = `Bearer ${token}`;

      // Server expects mode 'compare-stat'
      const res = await fetch(`http://${serverIp}:5168/api/game/session/start`, {
        method: 'POST',
        headers,
        body: JSON.stringify({ mode: 'compare-stat', questions: MAX_QUESTIONS })
      });
      if (!res.ok) {
        const text = await res.text();
        console.warn('Start session failed', res.status, text);
        throw new Error('Failed to start session');
      }
      const js = await res.json();
      setSessionId(js.sessionId);
      setIndex(0);
      setPoints(0);
      setCorrectCount(0);
    } catch (e) {
      console.error(e);
      Alert.alert('Error', 'Failed to start game session');
    } finally { setLoading(false); }
  };

  const fetchNext = async (sid?: string) => {
    const id = sid ?? sessionId;
    if (!id) return;
    setLoading(true);
    answeredRef.current = false;
    setAnswered(false);
    setShowResult(null);
    setLastSelected(null);
    try {
      const res = await fetch(`http://${serverIp}:5168/api/game/session/${id}/next`);
      if (!res.ok) throw new Error('Failed to get next question');
      const js = await res.json();
      if (js.finished) {
        router.replace({ pathname: '/pages/GameOver', params: { score: correctCount, total: MAX_QUESTIONS, points, returnTo: '/pages/HigherOrLowerSingle' } } as any);
        return;
      }
      setQuestion(js);
    } catch (e) {
      console.error(e);
      Alert.alert('Error', 'Failed to load question');
    } finally { setLoading(false); }
  };

  useEffect(() => { (async () => { await startSession(); })(); }, []);
  useEffect(() => { if (sessionId) fetchNext(sessionId); }, [sessionId]);

  const playSound = async (correct: boolean) => {
    try {
      const { sound } = await Audio.Sound.createAsync(
        correct ? require('../../assets/sounds/correct.mp3') : require('../../assets/sounds/incorrect.mp3')
      );
      await sound.playAsync();
      sound.setOnPlaybackStatusUpdate((status: any) => { if (status.isLoaded && status.didJustFinish) sound.unloadAsync(); });
    } catch (e) {
      console.warn('sound error', e);
    }
  };

  const submit = async (side: 'left' | 'right') => {
    if (answeredRef.current || loading) return;
    answeredRef.current = true;
    setAnswered(true);

    if (!sessionId || !question) {
      answeredRef.current = false;
      setAnswered(false);
      return;
    }

    setLastSelected(side);
    setLoading(true);
    try {
      const token = (global as any).userToken as string | undefined;
      const headers: Record<string, string> = { 'Content-Type': 'application/json' };
      if (token) headers['Authorization'] = `Bearer ${token}`;

      const res = await fetch(`http://${serverIp}:5168/api/game/session/${sessionId}/answer/${question.questionId}`, {
        method: 'POST',
        headers,
        body: JSON.stringify({ selectedSide: side, timeTakenMs: 1000 })
      });
      if (!res.ok) throw new Error('Failed to submit answer');
      const js = await res.json();

      const gotCorrect = !!js.correct;
      const earnedPoints = js.points ?? 0;

      const newCorrect = correctCount + (gotCorrect ? 1 : 0);
      const newPoints = points + (gotCorrect ? earnedPoints : 0);

      setCorrectCount(newCorrect);
      setPoints(newPoints);

      // Capture returned stat values when available
      const aValRaw = js.pokemonAValue ?? question.pokemonAValue ?? null;
      const bValRaw = js.pokemonBValue ?? question.pokemonBValue ?? null;
      const aVal = aValRaw != null ? Number(aValRaw) : null;
      const bVal = bValRaw != null ? Number(bValRaw) : null;

      setShowResult({ correct: js.correct, name: js.correctName ?? undefined, aValue: aVal ?? undefined, bValue: bVal ?? undefined });

      playSound(gotCorrect);

      if (js.finished) {
        // give user time to read final result
        setTimeout(() => router.replace({ pathname: '/pages/GameOver', params: { score: newCorrect, total: MAX_QUESTIONS, points: newPoints, returnTo: '/pages/HigherOrLowerSingle' } } as any), 2200);
        return;
      }

      // wait a bit longer so users can read the stat values
      setTimeout(async () => {
        setIndex(i => i + 1);
        await fetchNext(sessionId);
      }, 2200);
    } catch (e) {
      console.error(e);
      Alert.alert('Error', 'Failed to submit answer');
      answeredRef.current = false;
      setAnswered(false);
    } finally { setLoading(false); }
  };

  const handleBack = () => { try { router.replace({ pathname: '/pages/ChooseGame' } as any); } catch { router.push('/pages/ChooseGame' as any); } };

  if (loading && !question) {
    return (
      <View style={styles.container}>
        <Navbar title={'Higher or Lower'} onBack={handleBack} />
        <View style={styles.content}>
          <ActivityIndicator size="large" color={colors.primary} />
          <Text style={{ marginTop: 12, color: colors.text }}>Loading...</Text>
        </View>
      </View>
    );
  }

  if (!question) {
    return (
      <View style={styles.container}>
        <Navbar title={'Higher or Lower'} onBack={handleBack} />
        <View style={styles.content}>
          <Text style={{ color: colors.text }}>No question available</Text>
          <AppButton label="Retry" onPress={() => startSession()} />
        </View>
      </View>
    );
  }

  const leftName = question.pokemonAName;
  const rightName = question.pokemonBName;
  const stat = question.statToCompare;
  const leftImg = question.pokemonAImageUrl;
  const rightImg = question.pokemonBImageUrl;

  const leftStatValue = showResult?.aValue ?? (question.pokemonAValue != null ? Number(question.pokemonAValue) : null);
  const rightStatValue = showResult?.bValue ?? (question.pokemonBValue != null ? Number(question.pokemonBValue) : null);

  const leftHigher = leftStatValue != null && rightStatValue != null && Number(leftStatValue) > Number(rightStatValue);
  const rightHigher = leftStatValue != null && rightStatValue != null && Number(rightStatValue) > Number(leftStatValue);

  const isCorrectLeft = showResult && showResult.name === leftName;
  const isCorrectRight = showResult && showResult.name === rightName;
  const isIncorrectLeft = showResult && !showResult.correct && lastSelected === 'left';
  const isIncorrectRight = showResult && !showResult.correct && lastSelected === 'right';

  // stat text styles per side
  const leftStatStyles = [styles.statValue, leftHigher ? styles.statValueWinner : styles.statValueNormal];
  const rightStatStyles = [styles.statValue, rightHigher ? styles.statValueWinner : styles.statValueNormal];

  // if the card itself is colored (correct/incorrect), make stat text white for contrast
  if (showResult && (isCorrectLeft || isIncorrectLeft)) leftStatStyles.push(styles.statValueOnPrimary);
  if (showResult && (isCorrectRight || isIncorrectRight)) rightStatStyles.push(styles.statValueOnPrimary);

  const topOffset = Platform.OS === 'android' ? (StatusBar.currentHeight ?? 24) : 0;

  return (
    <SafeAreaView style={[styles.container, { paddingTop: topOffset }]}> 
      <Navbar title={'Higher or Lower'} onBack={handleBack} />
      <ScrollView contentContainerStyle={{ paddingBottom: 32 }} showsVerticalScrollIndicator={false}>
        <View style={styles.header}>
          <Text style={styles.statText}>Which Pokemon has higher {stat}?</Text>
          <Text style={styles.scoreText}>Question {index+1}/{MAX_QUESTIONS} | Score: {correctCount} ({points} pts)</Text>
        </View>
        <View style={styles.content}>
          <View style={styles.row}>
            <TouchableOpacity
              accessibilityRole="button"
              accessibilityLabel={`Select ${leftName}`}
              style={[
                styles.card,
                isCorrectLeft ? styles.cardCorrect : null,
                isIncorrectLeft ? styles.cardIncorrect : null
              ]}
              onPress={() => submit('left')}
              activeOpacity={0.85}
              disabled={answered || loading}
            >
              <Image source={{ uri: leftImg }} style={[styles.image, { height: imageHeight }]} resizeMode="contain" />
              <Text style={[styles.name, isCorrectLeft || isIncorrectLeft ? styles.nameOnPrimary : null]} numberOfLines={2}>{leftName}</Text>
              {showResult ? <Text style={leftStatStyles}>{`${stat}: ${leftStatValue ?? '?'}`}</Text> : null}
            </TouchableOpacity>
            <TouchableOpacity
              accessibilityRole="button"
              accessibilityLabel={`Select ${rightName}`}
              style={[
                styles.card,
                isCorrectRight ? styles.cardCorrect : null,
                isIncorrectRight ? styles.cardIncorrect : null
              ]}
              onPress={() => submit('right')}
              activeOpacity={0.85}
              disabled={answered || loading}
            >
              <Image source={{ uri: rightImg }} style={[styles.image, { height: imageHeight }]} resizeMode="contain" />
              <Text style={[styles.name, isCorrectRight || isIncorrectRight ? styles.nameOnPrimary : null]} numberOfLines={2}>{rightName}</Text>
              {showResult ? <Text style={rightStatStyles}>{`${stat}: ${rightStatValue ?? '?'}`}</Text> : null}
            </TouchableOpacity>
          </View>
          {showResult && (
            <View style={[styles.resultBanner, showResult.correct ? styles.correctBanner : styles.incorrectBanner]}>
              <Text style={styles.resultText}>{showResult.correct ? 'Correct!' : `Wrong - ${showResult.name ?? ''} had higher ${stat}`}</Text>
              {!showResult.correct ? (
                <Text style={[styles.resultText, { marginTop: 8 }]}>{`${leftName}: ${showResult.aValue ?? question.pokemonAValue ?? ''}  |  ${rightName}: ${showResult.bValue ?? question.pokemonBValue ?? ''}`}</Text>
              ) : null}
            </View>
          )}
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  header: { paddingVertical: 12, paddingHorizontal: 16, backgroundColor: colors.surface, borderBottomWidth: 1, borderBottomColor: colors.grey, alignItems: 'center' },
  content: { flex: 1, justifyContent: 'flex-start', alignItems: 'center', padding: 20 },
  row: { flexDirection: 'row', width: '100%', justifyContent: 'space-between', marginTop: 12 },
  statValue: { color: colors.text, marginTop: 8, fontWeight: '900', fontSize: 16 },
  statValueWinner: { color: '#22c55e' },
  statValueNormal: { color: colors.muted },
  statValueOnPrimary: { color: colors.white },
  card: {
    alignItems: 'center',
    width: '48%',
    backgroundColor: colors.surface,
    padding: 12,
    borderRadius: 16,
    borderWidth: 1,
    borderColor: colors.grey,
    ...Platform.select({
      ios: { shadowColor: '#000', shadowOffset: { width: 0, height: 6 }, shadowOpacity: 0.08, shadowRadius: 10 },
      android: { elevation: 4 }
    })
  },
  cardCorrect: { backgroundColor: '#22c55e', borderColor: '#1ca34d', borderWidth: 2 },
  cardIncorrect: { backgroundColor: '#ef4444', borderColor: '#c43a34', borderWidth: 2 },
  image: { width: '100%', height: 240, borderRadius: 12, backgroundColor: 'rgba(0,0,0,0.05)' },
  name: { color: colors.text, fontWeight: '800', marginVertical: 12, textAlign: 'center', fontSize: 20 },
  nameOnPrimary: { color: colors.white },
  scoreText: { color: colors.text, marginTop: 6, fontWeight: '700' },
  statText: { color: colors.text, fontSize: 22, fontWeight: '900', marginBottom: 8, textAlign: 'center' },
  resultBanner: { marginTop: 16, padding: 14, borderRadius: 12, borderWidth: 1, borderColor: colors.grey, backgroundColor: colors.surface },
  correctBanner: { backgroundColor: '#22c55e22' },
  incorrectBanner: { backgroundColor: '#ef444422' },
  resultText: { color: colors.text, fontWeight: '800', textAlign: 'center' }
});
