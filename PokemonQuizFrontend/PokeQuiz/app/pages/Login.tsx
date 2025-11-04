import React, { useState, useEffect } from 'react';
import { View, Text, TextInput, StyleSheet, TouchableOpacity, Alert, KeyboardAvoidingView, Platform } from 'react-native';
import { colors } from '../../styles/colours';
import { useRouter, useLocalSearchParams } from 'expo-router';
import Navbar from '@/components/Navbar';
import AppButton from '@/components/AppButton';

export default function LoginPage() {
    const router = useRouter();
    const params = useLocalSearchParams();
    const returnTo = (params as any).returnTo as string | undefined;

    const [username, setUsername] = useState('');
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [confirm, setConfirm] = useState('');
    const [isSigningUp, setIsSigningUp] = useState(false);
    const [loading, setLoading] = useState(false);

    const apiBase = 'http://localhost:5168';

    useEffect(() => {
        // If already logged in, go back
        const u = (global as any).username;
        if (u) {
            router.back();
        }
    }, []);

    const parseErrorText = async (res: Response) => {
        try {
            const json = await res.json();
            if (json && json.message) return json.message;
            return JSON.stringify(json);
        } catch {
            try { return await res.text(); } catch { return 'Request failed'; }
        }
    };

    const doLogin = async (uname: string, pwd: string) => {
        const res = await fetch(`${apiBase}/api/auth/login`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ username: uname, password: pwd })
        });
        if (!res.ok) throw new Error(await parseErrorText(res));
        const data = await res.json();
        (global as any).userToken = data.token;
        (global as any).userId = data.userId;
        (global as any).username = uname;
    };

    const handleSubmit = async () => {
        if (!username || !password) {
            Alert.alert('Validation', 'Username and password are required');
            return;
        }

        if (isSigningUp) {
            if (!email) { Alert.alert('Validation', 'Email required'); return; }
            if (password !== confirm) { Alert.alert('Validation', 'Passwords do not match'); return; }

            setLoading(true);
            try {
                const res = await fetch(`${apiBase}/api/auth/register`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ username, email, password })
                });
                if (!res.ok) {
                    const txt = await parseErrorText(res);
                    throw new Error(txt || 'Register failed');
                }

                // Auto-login after successful registration
                try {
                    await doLogin(username, password);
                    Alert.alert('Success', 'Account created and logged in');
                    if (returnTo) router.push(returnTo as any);
                    else router.back();
                } catch (loginErr: any) {
                    Alert.alert('Registered', 'Account created. Please log in.');
                    setIsSigningUp(false);
                }
            } catch (err: any) {
                console.warn(err);
                Alert.alert('Error', err?.message ?? 'Register failed');
            } finally { setLoading(false); }
            return;
        }

        setLoading(true);
        try {
            await doLogin(username, password);
            Alert.alert('Success', 'Logged in');
            if (returnTo) router.push(returnTo as any);
            else router.back();
        } catch (err: any) {
            console.warn(err);
            Alert.alert('Error', err?.message ?? 'Login failed');
        } finally { setLoading(false); }
    };

    return (
        <View style={styles.container}>
            <Navbar title={isSigningUp ? 'Sign Up' : 'Login'} />
            <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : 'height'} style={styles.centerArea}>
                <View style={styles.card}>
                    <Text style={styles.title}>{isSigningUp ? 'Create an account' : 'Welcome back'}</Text>

                    <Text style={styles.label}>Username</Text>
                    <TextInput value={username} onChangeText={setUsername} style={styles.input} placeholder="Username" autoCapitalize="none" />

                    {isSigningUp && (
                        <>
                            <Text style={styles.label}>Email</Text>
                            <TextInput value={email} onChangeText={setEmail} style={styles.input} placeholder="you@example.com" autoCapitalize="none" keyboardType="email-address" />
                        </>
                    )}

                    <Text style={styles.label}>Password</Text>
                    <TextInput value={password} onChangeText={setPassword} style={styles.input} placeholder="Password" secureTextEntry />

                    {isSigningUp && (
                        <>
                            <Text style={styles.label}>Confirm password</Text>
                            <TextInput value={confirm} onChangeText={setConfirm} style={styles.input} placeholder="Confirm password" secureTextEntry />
                        </>
                    )}

                    <AppButton label={isSigningUp ? (loading ? 'Creating...' : 'Create account') : (loading ? 'Logging...' : 'Login')} onPress={handleSubmit} style={{ width: '100%', marginTop: 12 }} disabled={loading} />

                    <TouchableOpacity onPress={() => setIsSigningUp(s => !s)} style={{ marginTop: 12 }}>
                        <Text style={styles.link}>{isSigningUp ? 'Have an account? Login' : "Don't have an account? Sign up"}</Text>
                    </TouchableOpacity>

                    <TouchableOpacity style={{ marginTop: 8 }} onPress={() => router.back()}>
                        <Text style={{ color: colors.muted }}>Cancel</Text>
                    </TouchableOpacity>
                </View>
            </KeyboardAvoidingView>
        </View>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1, backgroundColor: colors.surface },
    centerArea: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: 20 },
    card: { width: '100%', maxWidth: 520, backgroundColor: colors.white, padding: 20, borderRadius: 14, shadowColor: '#000', shadowOffset: { width: 0, height: 6 }, shadowOpacity: 0.12, shadowRadius: 12, elevation: 6 },
    title: { fontSize: 22, fontWeight: '700', marginBottom: 12, color: colors.text, textAlign: 'center' },
    input: { width: '100%', padding: 12, marginVertical: 8, backgroundColor: '#f7f7f7', borderRadius: 8, borderWidth: 1, borderColor: '#eee' },
    label: { marginTop: 6, marginLeft: 2, color: colors.text, fontWeight: '600' },
    link: { marginTop: 12, color: colors.primary, textDecorationLine: 'underline', textAlign: 'center' }
});
