# Instant Mode for Slay the Spire 2

A mod for **Slay the Spire 2** which increases the speed of almost everything to be practically instant.

## Controls

- **F8**: Toggle Instant Mode On/Off. A notice will appear with the current status.

## Installation

1. Ensure you have a mod loader installed for Slay the Spire 2.
2. Download the latest `InstantMode.zip` and extract it.
3. Place the `InstantMode.dll` and `InstantMode.pck` files in `<Game install path>\Slay the Spire 2\mods\InstantMode\` (Create the mods & InstantMode folders if necessary)
4. Launch the game and accept using mods if you haven't previously.

## How it Works (Technical)

This mod performs surgical patches using **Harmony**:

1. **Forced Instant Mode**: Patches the game's internal preference getter to always return `FastModeType.Instant`.
2. **Global TimeScale**: Maintains a consistent 10.0x multiplier via a background engine node.
3. **Command Overrides**: Divides all remaining wait times by the speed multiplier to ensure consistent performance.
4. **VFX Notifications**: Uses the game's internal `NFullscreenTextVfx` for toggle feedback.

## Credits

- This mod was built using the [sts2_ExampleMod](https://github.com/customjack/sts2_ExampleMod) template by **customjack**.

---
*Created with AI because I'm lazy*
