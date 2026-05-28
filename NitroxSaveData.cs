using System;
using System.Collections.Generic;

namespace NitroxConverter
{
    /// <summary>
    /// Wrapper containing all deserialized world, player, and entity data from a Nitrox multiplayer save.
    /// </summary>
    [Serializable]
    public class PersistedWorldData
    {
        /// <summary>
        /// Global game state such as story progression, PDA details, and batch cell indices.
        /// </summary>
        public WorldData WorldData { get; set; } = new WorldData();

        /// <summary>
        /// Profiles of all players that played on the Nitrox server.
        /// </summary>
        public List<PlayerData> PlayerData { get; set; } = new List<PlayerData>();

        /// <summary>
        /// Primary static base and vehicle entities in the Nitrox save (GlobalRootData.json).
        /// </summary>
        public List<GlobalEntityData> GlobalRootData { get; set; } = new List<GlobalEntityData>();

        /// <summary>
        /// Dynamic and secondary items, creatures, or tools spawned in the world (EntityData.json).
        /// </summary>
        public List<GlobalEntityData> EntityData { get; set; } = new List<GlobalEntityData>();
    }

    /// <summary>
    /// Holds the core metadata of the Nitrox world save.
    /// </summary>
    [Serializable]
    public class WorldData
    {
        /// <summary>
        /// Batch cells that were modified and active in the Nitrox world.
        /// </summary>
        public List<Vector3Data> ParsedBatchCells { get; set; } = new List<Vector3Data>();

        /// <summary>
        /// PDA and story progress information.
        /// </summary>
        public GameData GameData { get; set; } = new GameData();

        /// <summary>
        /// The map seed of the Subnautica world.
        /// </summary>
        public string Seed { get; set; }
    }

    /// <summary>
    /// Container for player-facing story and PDA metadata.
    /// </summary>
    [Serializable]
    public class GameData
    {
        /// <summary>
        /// The universal PDA entries, scans, blueprints, and logs.
        /// </summary>
        public NitroxPDAState PDAState { get; set; } = new NitroxPDAState();

        /// <summary>
        /// Set of story goals completed by players in the game.
        /// </summary>
        public NitroxStoryGoals StoryGoals { get; set; } = new NitroxStoryGoals();

        /// <summary>
        /// Global timer values for story events and countdowns.
        /// </summary>
        public NitroxStoryTiming StoryTiming { get; set; } = new NitroxStoryTiming();
    }

    /// <summary>
    /// Represents an entry in the PDA log.
    /// </summary>
    [Serializable]
    public class NitroxPdaLogEntry
    {
        /// <summary>
        /// The string key of the log entry.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// The in-game timestamp when the entry was added.
        /// </summary>
        public float Timestamp { get; set; }
    }

    /// <summary>
    /// Deserialized state of the PDA system (known blue prints, encyclopedia, scanner).
    /// </summary>
    [Serializable]
    public class NitroxPDAState
    {
        /// <summary>
        /// Blueprint TechTypes unlocked by players.
        /// </summary>
        public List<string> KnownTechTypes { get; set; } = new List<string>();

        /// <summary>
        /// Analyzed/fully studied TechTypes.
        /// </summary>
        public List<string> AnalyzedTechTypes { get; set; } = new List<string>();

        /// <summary>
        /// Unlocked encyclopedia keys/articles.
        /// </summary>
        public List<string> EncyclopediaEntries { get; set; } = new List<string>();

        /// <summary>
        /// TechTypes that have been scanned using the hand scanner.
        /// </summary>
        public List<string> ScannerComplete { get; set; } = new List<string>();

        /// <summary>
        /// List of audio logs, transcripts, and database entries collected.
        /// </summary>
        public List<NitroxPdaLogEntry> PdaLog { get; set; } = new List<NitroxPdaLogEntry>();
    }

    /// <summary>
    /// Completed story goals tracker.
    /// </summary>
    [Serializable]
    public class NitroxStoryGoals
    {
        /// <summary>
        /// Active/completed story event keys (e.g. Aurora explosions, precursor gates).
        /// </summary>
        public List<string> CompletedGoals { get; set; } = new List<string>();
    }

    /// <summary>
    /// Story timeline and timers tracking.
    /// </summary>
    [Serializable]
    public class NitroxStoryTiming
    {
        /// <summary>
        /// Total time elapsed in seconds.
        /// </summary>
        public float ElapsedSeconds { get; set; }

        /// <summary>
        /// Countdown time remaining for the Aurora explosion sequence.
        /// </summary>
        public float AuroraCountdownTime { get; set; }

        /// <summary>
        /// Last warning time index played for the Aurora explosion.
        /// </summary>
        public float AuroraWarningTime { get; set; }
    }

    /// <summary>
    /// Deserialized list of players found in PlayerData.json.
    /// </summary>
    [Serializable]
    public class NitroxPlayersData
    {
        /// <summary>
        /// Collection of individual player profile records.
        /// </summary>
        public List<PlayerData> Players { get; set; } = new List<PlayerData>();
    }

    /// <summary>
    /// Information describing a single Nitrox player's position, statistics, and items.
    /// </summary>
    [Serializable]
    public class PlayerData
    {
        /// <summary>
        /// Display name of the Nitrox player (e.g. "Player").
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Unique Nitrox ID assigned to this player.
        /// </summary>
        public string NitroxId { get; set; }

        /// <summary>
        /// Last recorded coordinate of the player in 3D space.
        /// </summary>
        public Vector3Data SpawnPosition { get; set; } = new Vector3Data();

        /// <summary>
        /// Orientation coordinates (quaternion) of the player's view/body.
        /// </summary>
        public Vector4Data SpawnRotation { get; set; } = new Vector4Data();

        /// <summary>
        /// Inventory items currently held in the player's backpack.
        /// </summary>
        public List<string> UsedItems { get; set; } = new List<string>();

        /// <summary>
        /// Key-value pairs representing slots (e.g., "Suit", "Gloves") and the ClassId/TechType of equipped items.
        /// </summary>
        public List<KeyValuePair<string, string>> EquippedItems { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Basic survival statistics (e.g., "health", "water", "food", "oxygen").
        /// </summary>
        public Dictionary<string, float> CurrentStats { get; set; } = new Dictionary<string, float>();
    }

    /// <summary>
    /// Deserialized list of entity nodes from global save files.
    /// </summary>
    [Serializable]
    public class NitroxEntitiesData
    {
        /// <summary>
        /// List of serialized entity objects.
        /// </summary>
        public List<GlobalEntityData> Entities { get; set; } = new List<GlobalEntityData>();
    }

    /// <summary>
    /// Standard model for any spawned game entity (bases, lockers, vehicles, flora, components).
    /// </summary>
    [Serializable]
    public class GlobalEntityData
    {
        /// <summary>
        /// Subnautica Unity ClassId (used to fetch the prefab).
        /// </summary>
        public string ClassId { get; set; }

        /// <summary>
        /// Friendly TechType name of the object.
        /// </summary>
        public string TechType { get; set; }

        /// <summary>
        /// Unique identifier for the object instance.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Slot name if this entity is slotted/equipped in a parent container (e.g., vehicle upgrades).
        /// </summary>
        public string Slot { get; set; }

        /// <summary>
        /// Dynamic metadata including health, charge, battery levels, colors, names, etc.
        /// </summary>
        public Newtonsoft.Json.Linq.JObject Metadata { get; set; }

        /// <summary>
        /// World coordinates and rotation.
        /// </summary>
        public TransformData Transform { get; set; } = new TransformData();

        /// <summary>
        /// Sub-entities contained within this entity (e.g., upgrades, lockers inside bases, base upgrades).
        /// </summary>
        public List<GlobalEntityData> ChildEntities { get; set; } = new List<GlobalEntityData>();

        /// <summary>
        /// Specialized structure present only if the entity is a base grid.
        /// </summary>
        public NitroxBaseData BaseData { get; set; }
    }

    /// <summary>
    /// Holds Nitrox base building grid data including cells, linkages, masks, and glass properties.
    /// </summary>
    [Serializable]
    public class NitroxBaseData
    {
        /// <summary>
        /// Outer shape dimensions of the constructed base.
        /// </summary>
        public Vector3Data BaseShape { get; set; }

        /// <summary>
        /// Offset relative to the anchor node in the base grid.
        /// </summary>
        public Vector3Data CellOffset { get; set; }

        /// <summary>
        /// Anchor position in the grid coordinate system.
        /// </summary>
        public Vector3Data Anchor { get; set; }

        /// <summary>
        /// Size of the raw base data before compression/serialization.
        /// </summary>
        public int PreCompressionSize { get; set; }

        /// <summary>
        /// Base64 encoded byte array representing face properties of base modules.
        /// </summary>
        public string Faces { get; set; }

        /// <summary>
        /// Base64 encoded byte array representing cell layout of base modules.
        /// </summary>
        public string Cells { get; set; }

        /// <summary>
        /// Base64 encoded byte array representing links/connections.
        /// </summary>
        public string Links { get; set; }

        /// <summary>
        /// Base64 encoded byte array representing geometry masks (e.g. reinforcement doors, windows).
        /// </summary>
        public string Masks { get; set; }

        /// <summary>
        /// Base64 encoded byte array representing glass status of module panels.
        /// </summary>
        public string IsGlass { get; set; }
    }

    /// <summary>
    /// Position and rotation description.
    /// </summary>
    [Serializable]
    public class TransformData
    {
        /// <summary>
        /// Local coordinate offsets.
        /// </summary>
        public Vector3Data LocalPosition { get; set; } = new Vector3Data();

        /// <summary>
        /// Quaternion rotation factors.
        /// </summary>
        public Vector4Data LocalRotation { get; set; } = new Vector4Data();
    }

    /// <summary>
    /// Serialized float-based 3D coordinates.
    /// </summary>
    [Serializable]
    public class Vector3Data
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }

    /// <summary>
    /// Serialized float-based 4D coordinates (typically quaternions).
    /// </summary>
    [Serializable]
    public class Vector4Data
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }
    }
}
