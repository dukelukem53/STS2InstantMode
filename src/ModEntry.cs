using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Extensions;
using Godot;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace InstantMode;

[ModInitializer("Init")]
public static class ModEntry
{
    private static bool _initialized = false;
    private static bool _logCleared = false;
    public const float FastSpeed = 10.0f;
    public static bool IsEnabled = true;
    
    // Use a real-time timestamp to create a "Lockout" period for transitions
    private static long _transitionLockUntil = 0;

    public static void StartTransitionLock(float durationSeconds)
    {
        // We lock the speed to 1.0x for slightly longer than the transition itself
        long bufferMs = 200; 
        _transitionLockUntil = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)(durationSeconds * 1000) + bufferMs;
    }

    public static bool IsInTransitionLock()
    {
        if (_transitionLockUntil == 0) return false;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now > _transitionLockUntil)
        {
            _transitionLockUntil = 0;
            return false;
        }
        return true;
    }

    [ThreadStatic]
    private static bool _isCheckingState = false;

    public static bool IsSafeForInstantSpeed()
    {
        if (_isCheckingState) return false;
        _isCheckingState = true;
        try {
            // Priority 1: If we are in a transition lock, it is NOT safe for instant speed.
            if (IsInTransitionLock()) return false;

            // Priority 2: Combat Room check
            if (NCombatRoom.Instance != null && !NCombatRoom.Instance.IsQueuedForDeletion()) 
            {
                if (CombatManager.Instance != null && CombatManager.Instance.IsEnding) return false;
                return true;
            }

            // Priority 3: Event check
            if (RunManager.Instance == null) return true;
            var state = RunManager.Instance.DebugOnlyGetState();
            if (state?.CurrentRoom == null) return true;
            
            if (state.CurrentRoom is EventRoom) return false;
            
            return true;
        } catch {
            return true; 
        } finally {
            _isCheckingState = false;
        }
    }

    private static string GetLogPath()
    {
        try {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string modDir = Path.GetDirectoryName(assemblyPath);
            return Path.Combine(modDir, "instant_mode_debug.log");
        } catch {
            return "instant_mode_debug.log";
        }
    }

    public static void LogDebug(string msg)
    {
        string path = GetLogPath();
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string fullMsg = $"[{timestamp}] {msg}";
        
        Log.Warn($"[InstantMode] {msg}");

        try {
            if (!_logCleared) {
                File.WriteAllText(path, fullMsg + System.Environment.NewLine);
                _logCleared = true;
            } else {
                File.AppendAllText(path, fullMsg + System.Environment.NewLine);
            }
        } catch {}
    }

    public static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        LogDebug("v1.3.14 - FIXED TRANSITION FADES (Time-Locked Logic)...");

        try {
            var harmony = new Harmony("com.instantmode.mod");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            
            TryPatchFastMode(harmony);
            
            var manager = new SpeedManager();
            manager.Name = "InstantModeSpeedManager";
            NGame.Instance?.CallDeferred(Node.MethodName.AddChild, manager);

            LogDebug("Init complete. Transitions now hardware-timed for smoothness.");
        } catch (Exception ex) {
            LogDebug($"FATAL INIT ERROR: {ex}");
        }
    }

    private static void TryPatchFastMode(Harmony harmony)
    {
        try {
            var saveManagerType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Saves.SaveManager");
            var prefsSaveProp = AccessTools.Property(saveManagerType, "PrefsSave");
            var fastModeProp = AccessTools.Property(prefsSaveProp.PropertyType, "FastMode");
            var getter = fastModeProp.GetGetMethod();
            var prefix = AccessTools.Method(typeof(FastModeGetterPatch), nameof(FastModeGetterPatch.Prefix));
            harmony.Patch(getter, new HarmonyMethod(prefix));
        } catch (Exception ex) {
            LogDebug($"Could not patch FastMode getter: {ex}");
        }
    }

    public static void Toggle()
    {
        try {
            IsEnabled = !IsEnabled;
            LogDebug($"Toggle -> {IsEnabled}");
            _transitionLockUntil = 0;
            if (NGame.Instance != null)
            {
                NGame.Instance.AddChild(NFullscreenTextVfx.Create(IsEnabled ? "Instant Mode: ON" : "Instant Mode: OFF"));
            }
        } catch (Exception ex) {
            LogDebug($"Error during toggle: {ex}");
        }
    }
}

public static class FastModeGetterPatch
{
    [ThreadStatic]
    private static bool _isInsideGetter = false;

    public static bool Prefix(ref FastModeType __result)
    {
        if (!ModEntry.IsEnabled) return true;
        if (_isInsideGetter) return true;

        _isInsideGetter = true;
        try {
            // We use the transition lock to force "Fast" mode (returning the fades).
            if (ModEntry.IsInTransitionLock())
            {
                __result = FastModeType.Fast;
                return false;
            }
            
            if (!ModEntry.IsSafeForInstantSpeed())
            {
                __result = FastModeType.Fast;
                return false;
            }

            __result = FastModeType.Instant;
            return false;
        } finally {
            _isInsideGetter = false;
        }
    }
}

public partial class SpeedManager : Node
{
    public override void _Process(double delta)
    {
        try {
            if (!ModEntry.IsEnabled)
            {
                if (Engine.TimeScale != 1.0) Engine.TimeScale = 1.0;
                return;
            }

            bool isSafe = ModEntry.IsSafeForInstantSpeed();

            // Only apply high TimeScale if we are NOT in a transition lock.
            if (isSafe)
            {
                if (Engine.TimeScale != (double)ModEntry.FastSpeed)
                    Engine.TimeScale = (double)ModEntry.FastSpeed;
            }
            else
            {
                if (Engine.TimeScale != 1.0)
                    Engine.TimeScale = 1.0;
            }
        } catch {}
    }
}

[HarmonyPatch(typeof(NTransition))]
public static class TransitionPatch
{
    [HarmonyPatch(nameof(NTransition.FadeOut))]
    [HarmonyPrefix]
    static void FadeOutPrefix(ref float time)
    {
        if (ModEntry.IsEnabled)
        {
            // We use a slightly longer time (0.5s real) to ensure stability
            time = 0.5f; 
            ModEntry.StartTransitionLock(time);
        }
    }

    [HarmonyPatch(nameof(NTransition.FadeIn))]
    [HarmonyPrefix]
    static void FadeInPrefix(ref float time)
    {
        if (ModEntry.IsEnabled)
        {
            time = 0.5f;
            ModEntry.StartTransitionLock(time);
        }
    }
}

[HarmonyPatch(typeof(Cmd))]
public static class CmdWaitPatch
{
    [HarmonyPatch(nameof(Cmd.Wait), new Type[] { typeof(float), typeof(bool) })]
    [HarmonyPrefix]
    static void WaitPrefix(ref float seconds)
    {
        if (ModEntry.IsEnabled && seconds > 0f)
        {
            seconds = 0.01f;
        }
    }

    [HarmonyPatch(nameof(Cmd.CustomScaledWait))]
    [HarmonyPrefix]
    static void CustomWaitPrefix(ref float fastSeconds, ref float standardSeconds)
    {
        if (ModEntry.IsEnabled)
        {
            fastSeconds = 0.01f;
            standardSeconds = 0.01f;
        }
    }
}

[HarmonyPatch(typeof(Tween), nameof(Tween.SetParallel))]
public static class TweenSpeedPatch
{
    static void Postfix(Tween __result)
    {
        try {
            if (ModEntry.IsEnabled && __result != null)
            {
                if (ModEntry.IsSafeForInstantSpeed())
                {
                    __result.SetSpeedScale(ModEntry.FastSpeed);
                }
            }
        } catch {}
    }
}

[HarmonyPatch(typeof(NGame), "_Input")]
public static class InputPatch
{
    static void Postfix(InputEvent inputEvent)
    {
        try {
            if (inputEvent is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.IsEcho())
            {
                if (keyEvent.Keycode == Key.F8)
                {
                    ModEntry.Toggle();
                }
            }
        } catch {}
    }
}
