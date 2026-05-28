# Changelog

All notable changes to the **Nitrox to Singleplayer Converter** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0] — 2026-05-28

### 🎉 Initial Release

#### Added

- **Nitrox Save Parsing** — Reads and deserializes `GlobalRootData.json`, `EntityData.json`, `PlayerData.json`, and `WorldData.json` from Nitrox multiplayer saves.
- **Base Spawning** — Reconstructs all player-built bases including structural pieces, interior modules, and placed equipment.
- **Vehicle Spawning** — Spawns Cyclops, Seamoth, and Prawn Suit (Exosuit) with:
  - Original custom names
  - Custom color schemes
  - Installed upgrade modules
- **Equipment & Decoration Restoration** — Restores placed decorative items and functional equipment within bases and vehicles.
- **Player Profile Restore** — Restores player position, health, food, water, and oxygen stats from the Nitrox save.
- **Inventory Sync** — Repopulates player inventory and equipment slots to match the Nitrox session.
- **PDA & Blueprint Sync** — Restores all unlocked PDA entries, encyclopedia articles, scanned fragments, and crafting blueprints.
- **Story Goal Restoration** — Re-triggers completed story goals, achievements, and radio messages to preserve progression state.
- **In-Game Progress UI** — Real-time on-screen overlay displaying current conversion task, step count, and percentage completion.
- **Hotkey Activation** — Conversion triggered via **F10** or **Ctrl+L** key combination.
- **BepInEx Configuration** — Configurable `PlayerName` setting to select which Nitrox player profile to restore.
- **Harmony Patches** — Targeted Harmony patches to prevent `NullReferenceException` errors during entity spawning and conversion.
- **Error Handling** — Graceful error handling with detailed log output for troubleshooting failed conversions.
