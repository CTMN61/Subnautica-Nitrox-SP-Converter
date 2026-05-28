# Nitrox to Singleplayer Save Converter

A BepInEx plugin for Subnautica that converts Nitrox multiplayer save files into fully working singleplayer saves.

---

This mod reads the serialized JSON save data from a Nitrox multiplayer server and reconstructs the game world—including base structures, power relays, custom vehicles, container inventories, PDA data, and story progression—directly into a singleplayer Subnautica session.

## Core Mechanics

Converting a multiplayer world to singleplayer requires a carefully timed, multi-pass asynchronous workflow (Unity Coroutines) to prevent physics glitches and ensure game engine systems initialize correctly.

### Pass 1: Base Geometry Reconstruction
Nitrox saves bases as compressed grid data in Base64 formats. The mod decompresses these arrays (Faces, Cells, Links, Masks, IsGlass) using a custom Run-Length Encoding (RLE) Deflate helper. It then populates the private fields of the native Subnautica Base component using reflection and triggers the game's native FinishDeserialization routine. This reconstructs the physical base structures safely.

### Pass 2: Equipment, Decorations, and Containers
Once the base geometry is physical, the mod spawns interior items (fabricators, chargers, lockers, posters) at their relative positions.
* Reparenting: Interior elements are registered as children of the corresponding base modules to prevent shifting.
* Sign Customization: Locker labels are restored via uGUI_SignInput.
* Containers: A reflection-based lookup scans components for any ItemsContainer fields or properties (e.g. in lockers, reactors, and filtration machines) and populates them with original items.

### Pass 3: Vehicle Restoration
Vehicles are spawned with original names, colors, upgrades, and energy stats:
* Cyclops Stability: The Cyclops is temporarily frozen (isKinematic = true) and leveled to prevent physics glitches while spawning its internal items.
* Cosmetics: Names and colors are applied directly to the SubName script.
* Upgrades: Modules are instantiated and slotted into the vehicle's equipment grid.
* Docking: The mod checks for nearby Moonpools or Cyclops docking bays and docks vehicles automatically.

### Player Profile and Inventory Sync
The player's state is fully restored:
* Teleportation: To prevent falling or suffocating during load, the mod temporarily disables the CharacterController, sets the position, and enables short-term cheats.
* Stats: Food, water, and health are synchronized.
* Inventory: Starting items are cleared, and the original Nitrox inventory items are spawned and equipped into the correct slots.

### PDA and Story Progress
* Blueprints and Encyclopedia: Unlocked blueprints are registered via KnownTech, and encyclopedia articles are added via PDAEncyclopedia.
* PDA Logs: Audio and radio logs are restored with their original timestamps.
* Story Triggers: Completed goals are re-triggered using StoryGoalManager.main.OnGoalComplete to fire associated scripting events.
* Signal Visibility: Lifepod and signal markers are forced visible on the HUD.

### Delayed Power Registration
To ensure nuclear reactors, bioreactors, and filtration machines initialize properly, a delayed two-pass background process runs 8 and 13 seconds post-conversion. This scans the scene and links these devices to the power relays of their closest bases, establishing oxygen and electricity.

## Harmony Patches

The mod applies Harmony patches to bypass Subnautica engine bugs and avoid NullReferenceExceptions during raw data injection:
* BaseFiltrationMachineGeometry: Prevents exceptions in filtration machine visuals when grid links are missing, falling back to a distance-based search.
* Component Initialization: Postfix patches on Start methods ensure filtration machines, bioreactors, and nuclear reactors receive valid base and power relay references immediately.

## Save Structure

The converter parses the following Nitrox server files:

| File | Description | Mod Struct |
|---|---|---|
| WorldData.json | World seed, PDA scans, encyclopedia, and story progression. | WorldData / GameData |
| PlayerData.json | Player profiles including equipment, position, and survival stats. | List<PlayerData> |
| GlobalRootData.json | Primary world entities (base structures and vehicles). | List<GlobalEntityData> |
| EntityData.json | Secondary and dynamic world objects (dropped items, etc.). | List<GlobalEntityData> |

## Requirements

* Subnautica (v2025 or newer recommended)
* BepInEx 5.x
* Newtonsoft.Json (included in release)

## Installation

1. Install BepInEx 5.x in your Subnautica game directory.
2. Copy NitroxConverterMod.dll into the BepInEx/plugins/ directory.
3. Create a folder named save in BepInEx/plugins/ and copy your Nitrox server save files there:
   ```
   Subnautica/
   └── BepInEx/
       └── plugins/
           └── save/
               ├── GlobalRootData.json
               ├── EntityData.json
               ├── PlayerData.json
               └── WorldData.json
   ```

## Usage

1. Start Subnautica and load or create any survival save.
2. Leave Lifepod 5 (the status overlay in the top-right will turn green once ready).
3. Press F10 or Ctrl+L to begin the conversion.
4. Monitor progress via the UI overlay.
5. Save your game manually once the process is complete.

## Configuration

After the first launch, a config file is generated at BepInEx/config/com.ctmn61.nitroxconverter.cfg:

```ini
[General]
# The name of the Nitrox player profile to restore. 
# If left empty, the first profile in PlayerData.json is used.
PlayerName = Player
```

## License

This project is licensed under the MIT License.
