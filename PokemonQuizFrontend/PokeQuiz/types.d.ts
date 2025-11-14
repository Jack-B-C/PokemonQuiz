declare const global: any;

declare module 'expo-av';
declare module 'expo-router' {
  export const Stack: any;
  export const router: any;
  export function useRouter(): any;
  export function useLocalSearchParams(): any;
  const _default: any;
  export default _default;
}
declare module '@react-navigation/native' {
  export const DarkTheme: any;
  export const DefaultTheme: any;
  export const ThemeProvider: any;
  const _default: any;
  export default _default;
}
declare module 'expo-status-bar' {
  export const StatusBar: any;
  const _default: any;
  export default _default;
}
declare module 'react-native-vector-icons/Ionicons' {
  const Icon: any;
  export default Icon;
}
declare module 'react-native-vector-icons/*' {
  const Icon: any;
  export default Icon;
}

// Keep minimal shims for any third-party modules without types
declare module 'expo-av';
declare module 'expo-audio';

// Let TypeScript pick up proper React / React Native types from node_modules
// Avoid broad shims that override official types.
