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
using System.Reflection;
using System.Diagnostics;

namespace InstantMode;

[ModInitializer("Init")]
public static class ModEntry
{
    private static bool _initialized = false;
    public const float FastSpeed = 10.0f;
    public static bool IsEnabled = true;
    private static bool _isRestrictedRoomActive = false;

    public static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        try {
            var harmony = new Harmony("com.instantmode.mod");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            
            TryPatchFastMode(harmony);
            
            var manager = new SpeedManager();
            manager.Name = "InstantModeSpeedManager";
            NGame.Instance?.CallDeferred(Node.MethodName.AddChild, manager);
        } catch (Exception ex) {
            Log.Error($"[InstantMode] FATAL INIT ERROR: {ex}");
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
        } catch {}
    }

    public static bool IsInTransition()
    {
        try {
            var stack = new StackTrace(false);
            string stackStr = stack.ToString();
            return stackStr.Contains("NTransition") || stackStr.Contains("Fade") || stackStr.Contains("RoomFade");
        } catch {
            return false;
        }
    }

    public static bool IsSafetyActive()
    {
        try {
            if (RunManager.Instance == null || !RunManager.Instance.IsInProgress || NGame.Instance == null) return true;
            if (IsInTransition()) return true;

            var state = RunManager.Instance.DebugOnlyGetState();
            if (state == null) return true;

            var room = state.CurrentRoom;
            if (room == null) return true;

            if (room is EventRoom er) {
                string eventId = er.ModelId.ToString();
                if (eventId.Contains("PUNCH_OFF") || eventId.Contains("TINKER_TIME")) {
                    _isRestrictedRoomActive = true;
                    return true;
                }
                return true; 
            }

            if (_isRestrictedRoomActive) {
                _isRestrictedRoomActive = false;
            }

            return false;
        } catch {
            return true; 
        }
    }

    public static void Toggle()
    {
        IsEnabled = !IsEnabled;
        if (NGame.Instance != null)
        {
            try {
                NGame.Instance.AddChild(NFullscreenTextVfx.Create(IsEnabled ? "Instant Mode: ON" : "Instant Mode: OFF"));
            } catch {}
        }
    }
}

public static class FastModeGetterPatch
{
    public static bool Prefix(ref FastModeType __result)
    {
        if (ModEntry.IsEnabled)
        {
            if (ModEntry.IsSafetyActive())
            {
                __result = FastModeType.Fast;
                return false;
            }

            __result = FastModeType.Instant;
            return false;
        }
        return true;
    }
}

public partial class SpeedManager : Node
{
    public override void _Process(double delta)
    {
        if (!ModEntry.IsEnabled)
        {
            if (Engine.TimeScale != 1.0) Engine.TimeScale = 1.0;
            return;
        }

        if (ModEntry.IsSafetyActive())
        {
            if (Engine.TimeScale != 1.0) Engine.TimeScale = 1.0;
            return;
        }

        var room = RunManager.Instance?.DebugOnlyGetState()?.CurrentRoom;
        if (room is CombatRoom) {
            if (NCombatRoom.Instance == null || NCombatRoom.Instance.IsQueuedForDeletion()) {
                if (Engine.TimeScale != 1.0) Engine.TimeScale = 1.0;
                return;
            }
        }

        if (Engine.TimeScale != (double)ModEntry.FastSpeed)
        {
            Engine.TimeScale = (double)ModEntry.FastSpeed;
        }
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
            time = 1.0f; 
        }
    }

    [HarmonyPatch(nameof(NTransition.FadeIn))]
    [HarmonyPrefix]
    static void FadeInPrefix(ref float time)
    {
        if (ModEntry.IsEnabled)
        {
            time = 1.0f;
        }
    }
}

[HarmonyPatch(typeof(Cmd), nameof(Cmd.Wait), new Type[] { typeof(float), typeof(bool) })]
public static class CmdWaitPatch
{
    static bool Prefix(ref float seconds)
    {
        if (ModEntry.IsEnabled)
        {
            if (ModEntry.IsSafetyActive()) return true;

            var stack = new StackTrace(false);
            string stackStr = stack.ToString();
            if (stackStr.Contains("PunchOff") || stackStr.Contains("PunchEachOther") || stackStr.Contains("TinkerTime") || stackStr.Contains("Core.Models.Events")) {
                return true; 
            }

            seconds = 0f;
        }
        return true;
    }
}

[HarmonyPatch(typeof(Tween), nameof(Tween.SetParallel))]
public static class TweenSpeedPatch
{
    static void Postfix(Tween __result)
    {
        if (ModEntry.IsEnabled && __result != null)
        {
            if (!ModEntry.IsSafetyActive())
            {
                __result.SetSpeedScale(ModEntry.FastSpeed);
            }
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
