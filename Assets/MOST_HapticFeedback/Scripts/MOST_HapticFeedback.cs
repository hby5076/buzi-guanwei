using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using Unity.VisualScripting;

namespace Solo.MOST_IN_ONE
{
    public static class MOST_HapticFeedback
    {
        [Serializable]
        [Tooltip("Each element = one pulse")]
        public struct CustomHapticPattern
        {
            [Tooltip("IOS Pulse data")]
            public IOS_Haptic[] IOS_HapticPattern;
            [Tooltip("Android Pulse data")]
            public Android_Haptic[] Android_HapticPattern;

            // class constructor
            public CustomHapticPattern(IOS_Haptic[] iosHaptic, Android_Haptic[] androidHaptic)
            {
                IOS_HapticPattern = iosHaptic;
                Android_HapticPattern = androidHaptic;
            }

            public readonly float GetDuration()
            {
#if UNITY_ANDROID
                return AndroidDuration();
#elif UNITY_IOS
                return IOSDuration();
#else
                return -1;
#endif
            }

            public readonly float IOSDuration()
            {
                float delay = 0;
                foreach (IOS_Haptic d in IOS_HapticPattern) delay += d.Delay;
                return (delay + 350f) / 1000f;
            }

            public readonly float AndroidDuration()
            {
                float delay = 0;
                foreach (Android_Haptic d in Android_HapticPattern) delay += d.Delay;
                return (delay + Android_HapticPattern[^1].PulseTime) / 1000f;
            }
        }

        [Serializable]
        public struct IOS_Haptic
        {
            // iOS haptic data
            [Tooltip("Delay before starting this pulse in milliseconds")]
            public float Delay;
            [Tooltip("Haptic type of this pulse")]
            public HapticTypes PulseType;

            // class constructor
            public IOS_Haptic(HapticTypes type, float delay)
            {
                Delay = delay;
                PulseType = type;
            }
        }

        [Serializable]
        public struct Android_Haptic
        {
            // Android haptic data
            [Tooltip("Delay before starting this pulse in milliseconds")]
            public long Delay;
            [Tooltip("Pulse time in milliseconds")]
            public long PulseTime;
            [Tooltip("vibration Strength of the pulse\ninteger (0-255)")]
            public int PulseStrength;

            // class constructor
            public Android_Haptic(long delay, long pattern, int amplitudes)
            {
                Delay = delay;
                PulseTime = pattern;
                PulseStrength = amplitudes;
            }
        }

        public enum HapticTypes
        {
            // These names are exactly the same as the objective c haptic feedback api (limited only to these feedbacks)
            // so i have used these names on android as well...
            // it's limited on ios but you can create unlimited custom patterns on android(check IOSDefaultHapticsToAndroidPatterns() line 49)
            Selection,    // case 0 // IOS 10+
            Success,      // case 1 // IOS 10+
            Warning,      // case 2 // IOS 10+
            Failure,      // case 3 // IOS 10+
            LightImpact,  // case 4 // IOS 10+
            MediumImpact, // case 5 // IOS 10+
            HeavyImpact,  // case 6 // IOS 10+
            RigidImpact,  // case 7 // IOS 13+ <<
            SoftImpact,   // case 8 // IOS 13+ <<
        }
#if UNITY_ANDROID && !UNITY_EDITOR  // This function reverse iOS default haptics enum to android haptic patterns (Android only)
        static void IOSDefaultHapticsToAndroidPatterns( 
            HapticTypes type, out long[] pattern, out int[] amplitudes)
        {
            // using 'pattern' and 'amplitudes' create android custom haptic feedback
            // pattern is the Timings for vibration pulses in milliseconds + delay betweem >>> Format [delay, vibrate, delay, vibrate, ...]
            // amplitudes is the strength of the pulse >>> integer (0-255)
            switch (type) 
            {
                case HapticTypes.Selection:
                    pattern = new long[] { 0, 20 };
                    amplitudes = new int[] { 0, 80 };
                    break;
                case HapticTypes.Success:
                    pattern = new long[] { 0, 100, 50, 100 };
                    amplitudes = new int[] { 0, 150, 0, 150 };
                    break;
                case HapticTypes.Warning:
                    pattern = new long[] { 0, 200 };
                    amplitudes = new int[] { 0, 200 };
                    break;
                case HapticTypes.Failure:
                    pattern = new long[] { 0, 40, 40, 40 };
                    amplitudes = new int[] { 0, 255, 0, 255 };
                    break;
                case HapticTypes.LightImpact:
                    pattern = new long[] { 0, 50 };
                    amplitudes = new int[] { 0, 100 };
                    break;
                case HapticTypes.MediumImpact:
                    pattern = new long[] { 0, 100 };
                    amplitudes = new int[] { 0, 180 };
                    break;
                case HapticTypes.HeavyImpact:
                    pattern = new long[] { 0, 200 };
                    amplitudes = new int[] { 0, 255 };
                    break;
                case HapticTypes.RigidImpact:
                    pattern = new long[] { 0, 25 };
                    amplitudes = new int[] { 0, 255 };
                    break;
                case HapticTypes.SoftImpact:
                    pattern = new long[] { 0, 80 };
                    amplitudes = new int[] { 0, 80 };
                    break;
                default:
                    pattern = new long[] { 0, 100 };
                    amplitudes = new int[] { 0, 150 };
                    break;
            }
        }
#endif

        static bool _initialized = false;
        static AndroidJavaObject _androidVibrator;
        static AndroidJavaClass _vibrationEffectClass;
        static int _androidApiLevel;
        static float _lastHapticTime;
        static float _hapticCooldown = 0.1f; // 100ms minimum between haptics

        // iOS Native Plugin Interface
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal", EntryPoint = "MOST_HapticFeedback")]
        static extern void M_HapticFeedback(int type);
#endif

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal", EntryPoint = "MOST_HapticPrewarm")]
        static extern void M_HapticPrewarm();
#endif

        [RuntimeInitializeOnLoadMethod]
        static void Initialize()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            InitializeAndroid();
#endif

#if UNITY_IOS && !UNITY_EDITOR
            try { M_HapticPrewarm(); } catch {}
#endif

            _initialized = true;
        }

        public static void GenerateWithCooldown(HapticTypes type, float cooldown = -1f)
        {
            float timeSinceLast = Time.unscaledTime - _lastHapticTime;
            float requiredCooldown = cooldown > 0 ? cooldown : _hapticCooldown;

            if (timeSinceLast >= requiredCooldown)
            {
                Generate(type);
                _lastHapticTime = Time.unscaledTime;
            }
        }

        static Task _activePattern;
        static CancellationTokenSource _cts;
        static readonly object _lock = new();

        public static bool IsPlaying => _activePattern != null && !_activePattern.IsCompleted;

        public static void GeneratePattern(CustomHapticPattern pattern)
        {
            if (!HapticsEnabled || !_initialized) return;
            lock (_lock)
            {
                if (IsPlaying) return;               // avoid overlap (or call Stop() first)
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                // NULL GUARDS
#if UNITY_IOS && !UNITY_EDITOR
        if (pattern.IOS_HapticPattern == null || pattern.IOS_HapticPattern.Length == 0) return;
#elif UNITY_ANDROID && !UNITY_EDITOR
        if (pattern.Android_HapticPattern == null || pattern.Android_HapticPattern.Length == 0) return;
#endif

                _activePattern = RunPatternAsync(pattern, _cts.Token);
                // NOTE: do NOT await here; this method remains fire-and-forget.
            }
        }

        static Task RunPatternAsync(CustomHapticPattern pattern, CancellationToken token)
        {
#if UNITY_IOS && !UNITY_EDITOR
    return RunIOSAsync(pattern, token);      // this one is async Task and awaits
#elif UNITY_ANDROID && !UNITY_EDITOR
    return RunAndroidAsync(pattern, token);  // this one is async Task and awaits
#else
            return Task.CompletedTask;               // no work on this platform
#endif
        }

        static async Task RunIOSAsync(CustomHapticPattern pattern, CancellationToken token)
        {
            foreach (var h in pattern.IOS_HapticPattern)
            {
                await Task.Delay((int)Mathf.Max(0, h.Delay), token);
#if UNITY_IOS && !UNITY_EDITOR
                GenerateIOS(h.PulseType);
#endif
            }
        }

        static async Task RunAndroidAsync(CustomHapticPattern pattern, CancellationToken token)
        {
            int total = 0; var p = new List<long>(); var a = new List<int>();
#if UNITY_ANDROID && !UNITY_EDITOR
            foreach (var h in pattern.Android_HapticPattern) { p.Add(h.Delay); a.Add(0); p.Add(h.PulseTime); a.Add(h.PulseStrength); total += (int)h.Delay; }
            GenerateAndroid(p.ToArray(), a.ToArray());
#endif
            await Task.Delay(total, token);
        }


        public static void Stop()
        {
            lock (_lock) { _cts?.Cancel(); }
        }


        public static void Generate(HapticTypes type)
        {
            if (!HapticsEnabled || !_initialized) return;

#if UNITY_IOS && !UNITY_EDITOR
            GenerateIOS(type);
#elif UNITY_ANDROID && !UNITY_EDITOR
            IOSDefaultHapticsToAndroidPatterns(type, out long[] pattern, out int[] amp);
            GenerateAndroid(pattern, amp);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static void InitializeAndroid()
        {
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    _androidVibrator = currentActivity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    _androidApiLevel = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");
                
                    if (_androidApiLevel >= 26) // Oreo 8.0+
                    {
                        _vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect");
                    }
                }
                Debug.Log("Android haptics initialized. API Level: " + _androidApiLevel);
            }
            catch (Exception e)
            {
                Debug.LogError("Android haptics initialization failed: " + e.Message);
            }
        }
#endif

#if UNITY_IOS && !UNITY_EDITOR
        static void GenerateIOS(HapticTypes type)
        {
            try
            {
                M_HapticFeedback((int)type);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to generate iOS haptic {type}: {e.Message}");
            }
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        static void GenerateAndroid(long[] pattern,int[] amplitudes)
        {
            if (_androidVibrator == null || !_androidVibrator.Call<bool>("hasVibrator")) return;
            try
            {
                if (_androidApiLevel >= 26)
                {
                    var effect = _vibrationEffectClass.CallStatic<AndroidJavaObject>(
                        "createWaveform", 
                        pattern, 
                        amplitudes, 
                        -1); // No repeat
                    _androidVibrator.Call("vibrate", effect);
                }
                else
                {
                    _androidVibrator.Call("vibrate", pattern, -1);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to generate Android haptic : {e.Message}");
            }
        }
#endif

        public static bool IsSupported()
        {
#if UNITY_IOS && !UNITY_EDITOR
        return true; // All iPhones since iPhone 7 support haptics
#elif UNITY_ANDROID && !UNITY_EDITOR
        return _androidVibrator != null && _androidVibrator.Call<bool>("hasVibrator");
#else
            return false;
#endif
        }

        public static bool HapticsEnabled
        {
            get => !PlayerPrefs.HasKey("MOST Haptic Toggle") || PlayerPrefs.GetInt("MOST Haptic Toggle") == 1;
            set
            {
                PlayerPrefs.SetInt("MOST Haptic Toggle", value ? 1 : 0);
                PlayerPrefs.Save(); // Optional: immediately save to disk
            }
        }
    }
}
