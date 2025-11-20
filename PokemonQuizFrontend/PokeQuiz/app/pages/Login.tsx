import React, { useState, useEffect, useCallback } from 'react';
import { View, Text, TextInput, StyleSheet, TouchableOpacity, Alert, KeyboardAvoidingView, Platform } from 'react-native';
import { colors } from '../../styles/colours';
import { useRouter, useLocalSearchParams } from 'expo-router';
import Navbar from '@/components/Navbar';
import AppButton from '@/components/AppButton';
import { getToken, setToken, clearToken } from '@/utils/tokenStorage';

// Login / Signup page for the Pokémon Quiz app.
// Responsibilities:
// - Allow users to login or register
// - Validate input on the client for faster feedback
// - Persist token via `tokenStorage` utility
// NOTE: Token/state storage strategy should be reviewed for production (secure storage, HTTPS, refresh tokens).
export default function LoginPage() {
    const router = useRouter();
    const params = useLocalSearchParams();
    const returnTo = (params as any).returnTo as string | undefined;
    const signup = (params as any).signup as string | undefined;

    // Form state
    const [username, setUsername] = useState('');
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [confirm, setConfirm] = useState('');
    const [isSigningUp, setIsSigningUp] = useState(signup === 'true');
    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState<string | null>(null);

    // Use emulator-friendly host for Android emulators; use localhost on other platforms.
    const serverIp = Platform.OS === 'android' ? '10.0.2.2' : 'localhost';
    const apiBase = `http://${serverIp}:5168`;

    // Parse response body to present useful error messages. Always try JSON first, then text.
    const parseErrorText = async (res: Response) => {
        try {
            const json = await res.json();
            if (json && (json.message || json.error)) return json.message ?? json.error;
            return JSON.stringify(json);
        } catch {
            try { return await res.text(); } catch { return 'Request failed'; }
        }
    };

    // Perform login request and persist token using tokenStorage.
    const doLogin = useCallback(async (uname: string, pwd: string) => {
        const res = await fetch(`${apiBase}/api/auth/login`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ username: uname, password: pwd })
        });
        if (!res.ok) {
            const err = await parseErrorText(res);
            throw new Error(err || `Login failed (${res.status})`);
        }
        const data = await res.json();
        // NOTE: Storing token on global is a quick convenience for development only.
        // In production, prefer secure storage and avoid global mutable state.
        (global as any).userToken = data.token;
        (global as any).userId = data.userId;
        (global as any).username = uname;
        await setToken(data.token);
    }, [apiBase]);

    // Basic client-side password strength check to give fast feedback to users.
    const validatePasswordStrength = (pwd: string) => {
        if (!pwd || pwd.length < 8) return 'Password must be at least 8 characters';
        if (!/[A-Za-z]/.test(pwd) || !/[0-9]/.test(pwd)) return 'Password must include letters and numbers';
        return null;
    };

    // Basic email validation regex (simple sanity check, not exhaustive).
    const isValidEmail = (em: string) => {
        return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(em);
    };

    // Submit handler: handles both login and sign-up flows.
    const handleSubmit = useCallback(async () => {
        setErrorMessage(null);

        // Trim inputs to prevent accidental whitespace issues.
        const uname = username.trim();
        const pwd = password;
        const mail = email.trim();

        if (!uname || !pwd) {
            setErrorMessage('Username and password are required');
            Alert.alert('Validation', 'Username and password are required');
            return;
        }

        if (isSigningUp) {
            if (!mail) {
                setErrorMessage('Email required');
                Alert.alert('Validation', 'Email required');
                return;
            }
            if (!isValidEmail(mail)) {
                setErrorMessage('Please enter a valid email address');
                Alert.alert('Validation', 'Please enter a valid email address');
                return;
            }
            if (pwd !== confirm) {
                setErrorMessage('Passwords do not match');
                Alert.alert('Validation', 'Passwords do not match');
                return;
            }

            const pwdErr = validatePasswordStrength(pwd);
            if (pwdErr) {
                setErrorMessage(pwdErr);
                Alert.alert('Validation', pwdErr);
                return;
            }

            setLoading(true);
            try {
                const res = await fetch(`${apiBase}/api/auth/register`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ username: uname, email: mail, password: pwd })
                });
                if (!res.ok) {
                    const txt = await parseErrorText(res);
                    setErrorMessage(txt);
                    Alert.alert('Error', txt);
                    throw new Error(txt || `Register failed (${res.status})`);
                }

                // Auto-login after successful registration
                try {
                    await doLogin(uname, pwd);
                    setErrorMessage(null);
                    Alert.alert('Success', 'Account created and logged in');
                    if (returnTo) router.replace(returnTo as any);
                    else router.replace('/pages/Account');
                } catch (loginErr: any) {
                    setErrorMessage(null);
                    Alert.alert('Registered', 'Account created. Please log in.');
                    setIsSigningUp(false);
                }
            } catch (err: any) {
                console.warn(err);
            } finally { setLoading(false); }
            return;
        }

        // Login flow
        setLoading(true);
        try {
            await doLogin(uname, pwd);
            setErrorMessage(null);
            if (returnTo) router.replace(returnTo as any);
            else router.replace('/pages/Account');
        } catch (err: any) {
            const msg = err?.message ?? 'Login failed';
            setErrorMessage(msg);
            Alert.alert('Error', msg);
        } finally { setLoading(false); }
    }, [username, password, email, confirm, isSigningUp, apiBase, doLogin, returnTo, router]);

    useEffect(() => {
        // If a token exists, verify it and redirect to Account (or returnTo).
        // Use AbortController to avoid updating state after unmount.
        const controller = new AbortController();
        let isMounted = true;

        (async () => {
            const token = await getToken();
            if (!token) return;
            try {
                const res = await fetch(`${apiBase}/api/auth/me`, { headers: { Authorization: `Bearer ${token}` }, signal: controller.signal });
                if (!isMounted) return;
                if (res.ok) {
                    const js = await res.json();
                    // Save minimal user info for session behaviour. Consider secure alternatives in production.
                    (global as any).username = js.username;
                    (global as any).userId = js.id;
                    (global as any).userToken = token;
                    if (returnTo) router.replace(returnTo as any);
                    else router.replace('/pages/Account');
                } else {
                    // invalid token -> clear
                    await clearToken();
                }
            } catch (e) {
                // If aborted or network error, clear token to force fresh login on next attempt.
                try { await clearToken(); } catch { /* ignore */ }
            }
        })();

        return () => {
            isMounted = false;
            controller.abort();
        };
    }, [apiBase, returnTo, router]);

    return (
        <View style={styles.container}>
            <Navbar title={isSigningUp ? 'Sign Up' : 'Login'} backTo={returnTo ?? '/'} />
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

                    {/* Inline error message for visibility */}
                    {errorMessage ? <Text style={styles.error}>{errorMessage}</Text> : null}

                    <TouchableOpacity onPress={() => setIsSigningUp(s => !s)} style={{ marginTop: 12 }}>
                        <Text style={styles.link}>{isSigningUp ? 'Have an account? Login' : "Don't have an account? Sign up"}</Text>
                    </TouchableOpacity>

                    <TouchableOpacity style={{ marginTop: 8 }} onPress={() => router.replace('/') }>
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
    link: { marginTop: 12, color: colors.primary, textDecorationLine: 'underline', textAlign: 'center' },
    error: { marginTop: 10, color: '#b00020', fontWeight: '600', textAlign: 'center' }
});
