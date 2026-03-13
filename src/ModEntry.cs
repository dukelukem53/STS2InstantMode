using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Extensions;
using Godot;
using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;

namespace InstantMode;

[ModInitializer("Init")]
public static class ModEntry
{
    private static bool _initialized = false;
    private static bool _logCleared = false;
    public const float FastSpeed = 10.0f;
    public static bool IsEnabled = true;

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

        LogDebug("REVERTING TO SMART-FASTMODE + INTENSE LOGGING...");

        try {
            var harmony = new Harmony("com.instantmode.mod");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            
            TryPatchFastMode(harmony);
            
            var manager = new SpeedManager();
            manager.Name = "InstantModeSpeedManager";
            NGame.Instance?.CallDeferred(Node.MethodName.AddChild, manager);

            LogDebug("Init complete. Monitoring StackTrace behavior.");
        } catch (Exception ex) {
            LogDebug($"FATAL INIT ERROR: {ex}");
        }
    }

    private static void TryPatchFastMode(Harmony harmony)
    {
        try {
            var saveManagerType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Saves.SaveManager");
            var prefsSaveProp = AccessTools.Property(saveManagerType, "PrefsSave");
            var prefsSaveType = prefsSaveProp.PropertyType;
            var fastModeProp = AccessTools.Property(prefsSaveType, "FastMode");
            var getter = fastModeProp.GetGetMethod();
            var prefix = AccessTools.Method(typeof(FastModeGetterPatch), nameof(FastModeGetterPatch.Prefix));
            harmony.Patch(getter, new HarmonyMethod(prefix));
            LogDebug("FastMode StackTrace Patch Active.");
        } catch (Exception ex) {
            LogDebug($"Could not patch FastMode getter: {ex}");
        }
    }

    public static void Toggle()
    {
        try {
            IsEnabled = !IsEnabled;
            LogDebug($"Toggle -> {IsEnabled}");
            
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
    private static int _callCount = 0;

    public static bool Prefix(ref FastModeType __result)
    {
        if (!ModEntry.IsEnabled) return true;

        _callCount++;
        try {
            var stack = new StackTrace(false); // Reduced overhead slightly by disabling file info
            string stackStr = stack.ToString();
            
            bool isTransition = stackStr.Contains("NTransition") || 
                               stackStr.Contains("Fade") || 
                               stackStr.Contains("RoomFade") ||
                               stackStr.Contains("Transition");

            if (isTransition)
            {
                // We log every transition-triggered request to see if it causes loops
                ModEntry.LogDebug($"[TRACE] Transition detected in Stack (Call #{_callCount}). Returning Fast.");
                __result = FastModeType.Fast;
                return false;
            }

            // Periodically log standard calls to prove the getter is still alive
            if (_callCount % 100 == 0) {
                ModEntry.LogDebug($"[TRACE] Still alive. Getter call count: {_callCount}");
            }

            __result = FastModeType.Instant;
            return false;
        } catch (Exception ex) {
            // If the StackTrace itself crashes (e.g. recursion), we catch it here
            ModEntry.LogDebug($"CRITICAL STACKTRACE ERROR: {ex.Message}");
            return true; 
        }
    }
}

public partial class SpeedManager : Node
{
    private int _frames = 0;

    public override void _Process(double delta)
    {
        _frames++;
        if (_frames % 120 == 0) {
            // ModEntry.LogDebug($"[TRACE] SpeedManager heartbeat (Frame {_frames})");
        }

        try {
            if (!ModEntry.IsEnabled)
            {
                if (Engine.TimeScale != 1.0) Engine.TimeScale = 1.0;
                return;
            }

            if (Engine.TimeScale != (double)ModEntry.FastSpeed)
            {
                Engine.TimeScale = (double)ModEntry.FastSpeed;
            }
        } catch (Exception ex) {
            ModEntry.LogDebug($"SpeedManager Process Error: {ex}");
        }
    }
}

[HarmonyPatch(typeof(NTransition))]
public static class TransitionPatch
{
    [HarmonyPatch(nameof(NTransition.FadeOut))]
    [HarmonyPrefix]
    static void FadeOutPrefix(float time)
    {
        if (ModEntry.IsEnabled)
        {
            ModEntry.LogDebug($"[TRACE] NTransition.FadeOut called. Target time: {time}");
        }
    }

    [HarmonyPatch(nameof(NTransition.FadeIn))]
    [HarmonyPrefix]
    static void FadeInPrefix(float time)
    {
        if (ModEntry.IsEnabled)
        {
            ModEntry.LogDebug($"[TRACE] NTransition.FadeIn called. Target time: {time}");
        }
    }
}

[HarmonyPatch(typeof(Cmd), nameof(Cmd.Wait), new Type[] { typeof(float), typeof(bool) })]
public static class CmdWaitPatch
{
    static bool Prefix(float seconds)
    {
        if (ModEntry.IsEnabled)
        {
            if (seconds > 0.1f) {
                // ModEntry.LogDebug($"[TRACE] Cmd.Wait(float, bool) bypassing {seconds}s wait.");
            }
        }
        return true; // We let the original run because FastMode=Instant handles it
    }
}

[HarmonyPatch(typeof(Tween), nameof(Tween.SetParallel))]
public static class TweenSpeedPatch
{
    static void Postfix(Tween __result)
    {
        if (ModEntry.IsEnabled && __result != null)
        {
            // ModEntry.LogDebug("[TRACE] Tween.SetParallel patched.");
            __result.SetSpeedScale(ModEntry.FastSpeed);
        }
    }
}

[HarmonyPatch(typeof(NGame), "_Input")]
public static class InputPatch
{
    static void Postfix(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.IsEcho())
        {
            if (keyEvent.Keycode == Key.F8)
            {
                ModEntry.Toggle();
            }
        }
    }
}
