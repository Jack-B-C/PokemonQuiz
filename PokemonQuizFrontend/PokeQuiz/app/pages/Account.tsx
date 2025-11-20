// Pokémon Quiz — Page: Account
// Standard page header added for consistency. No behavior changes.

import React, { useEffect, useState } from 'react';
import { View, Text, StyleSheet, ActivityIndicator, Alert, TouchableOpacity, Linking, Platform, ScrollView } from 'react-native';
import Navbar from '@/components/Navbar';
import AppButton from '@/components/AppButton';
import { colors } from '../../styles/colours';
import { useRouter } from 'expo-router';
import { fetchAuth } from '@/utils/fetchAuth';
import { getToken, clearToken } from '@/utils/tokenStorage';

export default function AccountPage() {
    const router = useRouter();
    const [loading, setLoading] = useState(true);
    const [user, setUser] = useState<{ id?: string; username?: string; email?: string; role?: string } | null>(null);
    const [stats, setStats] = useState<any[] | null>(null);
    const [totalGames, setTotalGames] = useState<number | null>(null);

    const serverIp = Platform.OS === 'android' ? '10.0.2.2' : 'localhost';
    const apiBase = `http://${serverIp}:5168`;

    // Immediate redirect if no token present to avoid showing "Not logged in" message
    useEffect(() => {
        (async () => {
            const t = await getToken();
            if (!t) {
                router.replace('/pages/Login');
                return;
            }
            (global as any).userToken = t;
        })();
    }, []);

    useEffect(() => {
        (async () => {
            const token = (global as any).userToken as string | undefined;
            if (!token) return;

            try {
                const res = await fetchAuth(`${apiBase}/api/auth/me`, { headers: {} });
                if (!res.ok) {
                    if (res.status === 401) { doLogout(); return; }
                    throw new Error('Failed to fetch user info');
                }
                const js = await res.json();
                setUser(js);

                try {
                    const sres = await fetchAuth(`${apiBase}/api/auth/stats`, { headers: {} });
                    if (sres.ok) {
                        const sjs = await sres.json();
                        const rows = sjs.stats ?? [];
                        setStats(rows);
                        // compute total. account for different property names returned by API
                        const total = (rows as any[]).reduce((acc, r) => {
                            const p = r.GamesPlayed ?? r.gamesPlayed ?? r.GamesPlayed ?? 0;
                            return acc + (typeof p === 'number' ? p : parseInt(p || '0'));
                        }, 0);
                        setTotalGames(total);
                    } else if (sres.status === 401) {
                        doLogout(); return;
                    } else {
                        console.warn('Failed to load stats', sres.status);
                        setStats([]); setTotalGames(0);
                    }
                } catch (e) {
                    console.warn('Failed to load stats', e); setStats([]); setTotalGames(0);
                }

            } catch (e) {
                console.warn('Failed to load user info', e);
                Alert.alert('Error', 'Failed to load account info');
            } finally {
                setLoading(false);
            }
        })();
    }, []);

    const doLogout = async () => {
        (global as any).userToken = undefined;
        (global as any).userId = undefined;
        (global as any).username = undefined;
        await clearToken();
        setUser(null);
        // Redirect explicitly to login page
        router.replace('/pages/Login');
    };

    const openAdmin = async () => {
        // open admin SPA served by API
        const token = (global as any).userToken as string | undefined;
        const url = token ? `http://${serverIp}:5168/admin#token=${encodeURIComponent(token)}` : `http://${serverIp}:5168/admin`;
        try {
            await Linking.openURL(url);
        } catch (e) {
            Alert.alert('Error', `Failed to open admin UI: ${url}`);
        }
    };

    // If we redirected because no token, render nothing
    // rendering will continue after token check completes

    if (loading) {
        return (
            <View style={styles.container}>
                <Navbar title="Account" />
                <View style={styles.content}>
                    <ActivityIndicator size="large" color={colors.primary} />
                </View>
            </View>
        );
    }

    if (!user) {
        // if user not populated after loading, ensure redirect
        router.replace('/pages/Login');
        return null;
    }

    return (
        <View style={styles.container}>
            <Navbar title="Account" backTo={'/'} />
            <ScrollView contentContainerStyle={styles.content}>
                <Text style={styles.title}>{user.username}</Text>
                <Text style={styles.row}><Text style={styles.label}>Email: </Text>{user.email ?? '—'}</Text>
                <Text style={styles.row}><Text style={styles.label}>Role: </Text>{user.role ?? 'User'}</Text>

                <View style={{ height: 12 }} />
                <Text style={styles.row}><Text style={styles.label}>Total games played: </Text>{totalGames ?? 0}</Text>

                <View style={{ height: 16 }} />

                <AppButton label="Logout" onPress={doLogout} backgroundColor={colors.primaryDark || '#666'} style={styles.button} />

                <View style={{ height: 24 }} />

                {user.role && user.role.toLowerCase() === 'admin' ? (
                    <AppButton label="Open Admin Dashboard" onPress={openAdmin} backgroundColor={'#123e7a'} style={styles.button} />
                ) : null}

                <View style={{ height: 24 }} />

                <Text style={styles.sectionTitle}>Per-game stats</Text>
                <View style={styles.statsBox}>
                    {stats && stats.length > 0 ? (
                        stats.map((s: any, idx: number) => (
                            <View key={idx} style={styles.statRow}>
                                <Text style={styles.statGame}>{s.GameId ?? s.gameId ?? 'Unknown'}</Text>
                                <Text style={styles.statText}>Games played: {s.GamesPlayed ?? s.gamesPlayed ?? 0}</Text>
                                <Text style={styles.statText}>Best score: {s.BestScore ?? s.bestScore ?? 0}</Text>
                                <Text style={styles.statText}>Average score: {Math.round((s.AverageScore ?? s.avgScore ?? 0)*100)/100}</Text>
                            </View>
                        ))
                    ) : (
                        <Text style={styles.muted}>No game stats available</Text>
                    )}
                </View>

            </ScrollView>
        </View>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, backgroundColor: colors.surface },
    content: { padding: 20, alignItems: 'center' },
    title: { fontSize: 28, fontWeight: '800', marginTop: 12, color: colors.text },
    row: { marginTop: 8, color: colors.text },
    label: { fontWeight: '700', color: colors.text },
    message: { marginTop: 20, color: colors.text },
    button: { width: '80%', marginTop: 12 },
    sectionTitle: { fontSize: 18, fontWeight: '800', color: colors.text, marginTop: 6 },
    statsBox: { width: '100%', marginTop: 8 },
    statRow: { padding: 12, backgroundColor: '#f3f4f6', borderRadius: 10, marginBottom: 10 },
    statGame: { fontWeight: '800', color: '#111', marginBottom: 6 },
    statText: { color: '#111', marginTop: 2 },
    muted: { color: '#666' }
});
