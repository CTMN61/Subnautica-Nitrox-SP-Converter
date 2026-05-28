# Nitrox to Singleplayer Save Converter

A BepInEx plugin for Subnautica that converts Nitrox multiplayer save files into singleplayer saves.

---

Ever spent dozens of hours on a Nitrox multiplayer server and wished you could continue that world on your own? This mod reads your Nitrox save files and attempts to rebuild your bases, vehicles, inventory, and story progress directly into a singleplayer session.

> **Disclaimer:** This entire project was **vibe-coded**. It is held together by hope, reflection, and duct tape. Bugs, glitches, and weird behaviors are not just possible—they are guaranteed. Use with caution (and backup your saves!).

## Known Limitations & Weird Quirks

Because multiplayer sync data is completely different from how vanilla Subnautica handles things, you will encounter the following quirks:

* **Cyclops Issues**: The Cyclops might spawn bugged, tilted, empty, or doing backflips in the water.
* **Ghost Items**: Random items might spawn in places where they definitely shouldn't be.
* **Unusable Interior Devices**: Fabricators, lockers, or chargers placed inside bases might be non-interactive at first. You will likely need to deconstruct (pick them up) and place them down again to make them usable.
* **Weird Exterior Lighting**: Placed base objects on the outside might have pitch-black or broken lighting/shaders. Re-placing them fixes their look.

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
