import react from 'react';
import { View, Text, StyleSheet, TouchableOpacity, Dimensions } from 'react-native';
import { colors } from '../../styles/colours';
import Icon from 'react-native-vector-icons/Ionicons';
import Navbar from '@/components/Navbar';
import AppButton from '@/components/AppButton';

export default function ChooseGame(){
    
        const navigation = useNavigation();
    return(
        <View style={styles.container}>
            <Navbar title="Choose Game" ShowBackButton />

            <View style={styles.modeButtonsContainer}>
                <text style={styles.subtitle}>Choose your game mode</text>
                <AppButton 
                label="Single Player"
                onPress={()=>navigation.navigate("SinglePlayer")}
                backgroundColor='#3d52d5'
                textColor='fff'
                styles = {style.button}
                />
                <AppButton 
                label="Multiplayer"
                onPress={()=>navigation.navigate("Multiplayer")}
                backgroundColor='#3d52d5'
                textColor='fff'
                styles = {style.button}
                />
            </View>
        </View>
    );
                }
            
                const styles = StyleSheet.create({
                    container: {
                        flex: 1,
                        backgroundColor: #151515,
                    },
                    Content : {
                        flex: 1,
                        justifyContent: 'center',
                        alignItems: 'center',
                        padding: 20,
                    },
                    subtitle: {
                        fontSize: 18,
                        color: colors.white,
                        marginBottom: 20,
                        textAlign: 'center',
                    },
                    modeButtonsContainer: { 
                        flex: 1,
                        justifyContent: 'center',
                        alignItems: 'center',
                        paddingHorizontal: 20,
                    },
                });
                

