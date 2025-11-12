import React, { useEffect, useState } from 'react';
import { View, Text, StyleSheet, ActivityIndicator, Alert, ScrollView, Platform } from 'react-native';
import Navbar from '@/components/Navbar';
import AppButton from '@/components/AppButton';
import { colors } from '../../styles/colours';
import { useRouter, useLocalSearchParams } from 'expo-router';

export default function AdminUserPage() {
    const router = useRouter();
    const params = useLocalSearchParams();
    const userId = (params as any).userId as string | undefined;
    const [loading, setLoading] = useState(true);
    const [stats, setStats] = useState<any[]>([]);

    const serverIp = Platform.OS === 'android' ? '10.0.2.2' : 'localhost';
    const apiBase = `http://${serverIp}:5168`;

    useEffect(() => {
        (async () => {
            if (!userId) { Alert.alert('No userId'); router.back(); return; }
            try {
                const token = (global as any).userToken as string | undefined;
                const headers: Record<string,string> = {};
                if (token) headers['Authorization'] = `Bearer ${token}`;
                const res = await fetch(`${apiBase}/api/admin/users/${encodeURIComponent(userId)}/stats`, { headers });
                if (!res.ok) throw new Error('Failed');
                const js = await res.json();
                setStats(js.stats ?? []);
            } catch (e) {
                console.warn(e);
                Alert.alert('Failed to load user stats');
                router.back();
            } finally { setLoading(false); }
        })();
    }, []);

    if (loading) return (<View style={{flex:1}}><Navbar title='User stats' /><ActivityIndicator /></View>);

    return (
        <View style={{flex:1}}>
            <Navbar title='User stats' />
            <ScrollView contentContainerStyle={{padding:20}}>
                {stats.map((s, i) => (
                    <View key={i} style={{padding:12, backgroundColor: colors.surface==='#fff' ? '#f3f4f6' : '#111827', borderRadius:8, marginBottom:8}}>
                        <Text style={{fontWeight:'800', color: colors.text}}>{s.GameId || 'General'}</Text>
                        <Text style={{color:colors.text}}>Played: {s.GamesPlayed}</Text>
                        <Text style={{color:colors.text}}>Best: {s.BestScore}</Text>
                        <Text style={{color:colors.text}}>Avg: {Math.round((s.AverageScore ?? 0) * 100) / 100}</Text>
                        <Text style={{color:colors.text}}>Q: {s.TotalQuestions} / Correct: {s.CorrectAnswers} ({Math.round(((s.Accuracy ?? 0) * 100))}% )</Text>
                    </View>
                ))}
            </ScrollView>
        </View>
    );
}
