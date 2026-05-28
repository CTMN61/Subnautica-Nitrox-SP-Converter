# Nitrox to Singleplayer Save Converter

A BepInEx plugin for Subnautica that converts Nitrox multiplayer save files into singleplayer saves.

---

Ever spent dozens of hours on a Nitrox multiplayer server and wished you could continue that world on your own? This mod reads your Nitrox save files and attempts to rebuild your bases, vehicles, inventory, and story progress directly into a singleplayer session.

> **Disclaimer & Warning:** This software is an experimental, community-made converter and is provided "as-is". Due to the complex nature of translating multiplayer network data into a singleplayer environment, unexpected behaviors and save data corruption can occur. **Always back up your singleplayer and Nitrox save files before proceeding.**

## Known Technical Limitations

Due to fundamental differences in how multiplayer network states and vanilla Subnautica save files are structured, the following limitations currently apply:

* **Cyclops Initialization**: The Cyclops submarine may occasionally spawn with misaligned physics, incorrect buoyancy, or incomplete interior structures.
* **Redundant Entities**: Unintended duplicate or misplaced items may occasionally spawn at incorrect coordinates due to differences in entity tracking between Nitrox and vanilla.
* **Interior Device Interaction**: Interior appliances (such as fabricators, storage lockers, or chargers) may occasionally lose their interaction triggers after spawning. Deconstructing (picking up) and rebuilding the affected device fully restores its functionality.
* **Exterior Lighting & Shaders**: Placed base objects on the outside of structures may occasionally display incorrect shaders or pitch-black lighting. Re-placing the affected object resolves the rendering issue.

## Requirements

* Subnautica (v2025 or newer recommended)
* BepInEx 5.x
* Newtonsoft.Json (included in release)

## Installation

1. Install BepInEx 5.x in your Subnautica game directory.
2. Copy `NitroxConverterMod.dll` into the `BepInEx/plugins/` directory.
3. Create a folder named `save` in `BepInEx/plugins/` and copy your Nitrox server save files there:
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
2. Leave Lifepod 5 (the status overlay in the top-right will turn green once you are in open water).
3. Press **F10** or **Ctrl+L** to begin the conversion.
4. Watch the progress overlay. Once complete, **save your game manually** to lock in the singleplayer world.

## Configuration

After the first launch, a config file is generated at `BepInEx/config/com.ctmn61.nitroxconverter.cfg`:

```ini
[General]
# The name of the Nitrox player profile to restore. 
# If left empty, the first profile in PlayerData.json is used.
PlayerName = Player
```

## License

This project is licensed under the MIT License.
