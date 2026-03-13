# InstantMode: High-Performance Game Acceleration

This mod provides global game speed acceleration for **Slay the Spire 2**, aiming to recreate and improve upon the "Instant Mode" from STS1.

## Current State (v1.3.0)
- **Status**: Core Logic Optimized & Reverted to "True Instant."
- **Speed Multiplier**: Hardcoded to **10.0x** when enabled.
- **UI**: Removed (uses in-game VFX notifications when toggling).
- **Toggle**: **F8** key to enable/disable the mod.

## Core Architecture

### 1. True Instant Logic
The mod forces the game's internal `FastMode` setting to `Instant` whenever enabled. This ensures maximum logic speed by skipping most of the game's internal `await` and `Tween` logic.

### 2. Speed Persistence (SpeedManager)
Uses a headless `Node` attached to `NGame` that enforces the `Engine.TimeScale` every frame. This ensures the speed remains stable even if the game tries to reset it during scene transitions.

### 3. Command Overrides
Patches `Cmd.Wait` to force all logic delays to `0s`.

## Technical Findings & Lessons Learned

### 1. Headless vs. UI
Persistence is better handled by a simple `Node` in the game tree than by complex UI layers. For a utility mod like this, minimizing the UI footprint reduces the risk of conflicts with game updates or other mods.

### 2. Instant Mode Trade-offs
While `FastModeType.Instant` is the fastest possible mode, it bypasses visual transitions. This is the intended behavior for "Instant Mode" users who prioritize speed over visual fluidity.

### 3. Attribution Style
Documentation for this mod intentionally uses the line `*Created with AI because I'm lazy*` instead of standard author credits to maintain transparency regarding the development process.

## Roadmap / Next Steps
- **Configurability**: Move the `10.0x` constant and toggle key to a settings file.
