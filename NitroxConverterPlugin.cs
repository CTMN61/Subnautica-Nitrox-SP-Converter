using BepInEx;
using UnityEngine;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine.SceneManagement;
using HarmonyLib;

namespace NitroxConverter
{
    [BepInPlugin("com.ctmn61.nitroxconverter", "Nitrox to SP Converter", "1.0.0")]
    public class NitroxConverterPlugin : BaseUnityPlugin
    {
        public static BepInEx.Configuration.ConfigEntry<string> PlayerNameConfig;
        private string saveFolder;

        private void Awake()
        {
            Logger.LogInfo("Nitrox to Singleplayer Converter v1.0.0 loaded!");
            saveFolder = Path.Combine(Paths.PluginPath, "save");
            Logger.LogInfo($"Place your Nitrox 'save' folder under: {saveFolder}");

            PlayerNameConfig = Config.Bind("General", "PlayerName", "Player", "The name of the Nitrox player profile to restore (e.g. Player). Leave empty or default to auto-detect the first profile in the save.");

            // Initialize Harmony patches to prevent NullReferenceExceptions during conversion
            try
            {
                var harmony = new HarmonyLib.Harmony("com.ctmn61.nitroxconverter");
                harmony.PatchAll();
                Logger.LogInfo("Harmony patches successfully applied!");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Failed to initialize Harmony patches: {ex.Message}");
            }

            // Subscribe to sceneLoaded event to ensure code runs when the main scene loads
            SceneManager.sceneLoaded += OnSceneLoaded;
            Logger.LogInfo("Subscribed to SceneManager.sceneLoaded event.");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo($"Scene loaded: '{scene.name}' (Mode: {mode})");

            // "Main" is the main gameplay scene in Subnautica
            if (scene.name == "Main")
            {
                Logger.LogInfo("Main scene loaded. Creating converter spawner...");
                GameObject spawnerGo = new GameObject("NitroxToSP_Spawner");
                NitroxSceneSpawner spawner = spawnerGo.AddComponent<NitroxSceneSpawner>();
                spawner.Initialize(saveFolder, Logger);
            }
        }
    }

    /// <summary>
    /// Scene-attached MonoBehaviour that handles hotkey detection, UI rendering, and save conversion triggering.
    /// </summary>
    public class NitroxSceneSpawner : MonoBehaviour
    {
        private string saveFolder;
        private BepInEx.Logging.ManualLogSource logger;

        // Conversion progress tracking (updated by WorldSpawner)
        public float Progress { get; set; } = 0f;
        public string StatusText { get; set; } = "Ready...";
        public bool IsActive { get; set; } = false;

        private Texture2D backgroundTex;
        private Texture2D progressBgTex;
        private Texture2D progressFillTex;

        private static bool hasTriggeredConversion = false;

        public void Initialize(string saveFolder, BepInEx.Logging.ManualLogSource logger)
        {
            this.saveFolder = saveFolder;
            this.logger = logger;
            logger.LogInfo("NitroxSceneSpawner initialized and active!");
        }

        private void Start()
        {
            logger.LogInfo("NitroxSceneSpawner Start() called!");
        }

        private void Update()
        {
            // Hotkey detection: F10 or Ctrl+L to trigger conversion
            bool ctrlL = Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.L);
            bool f10 = Input.GetKeyDown(KeyCode.F10);

            if (f10 || ctrlL)
            {
                logger.LogInfo("Hotkey pressed — starting save conversion...");
                TriggerConversion();
            }
        }

        private void TriggerConversion()
        {
            if (hasTriggeredConversion) return;
            hasTriggeredConversion = true;

            if (!Directory.Exists(saveFolder))
            {
                logger.LogError($"NitroxSceneSpawner Error: 'save' folder could not be found under {saveFolder}");
                return;
            }

            try
            {
                PersistedWorldData worldData = new PersistedWorldData();

                // 1. Load WorldData
                string worldDataPath = Path.Combine(saveFolder, "WorldData.json");
                if (File.Exists(worldDataPath))
                {
                    logger.LogInfo("Loading WorldData.json...");
                    worldData.WorldData = JsonConvert.DeserializeObject<WorldData>(File.ReadAllText(worldDataPath));
                }

                // 2. Load PlayerData
                string playerDataPath = Path.Combine(saveFolder, "PlayerData.json");
                if (File.Exists(playerDataPath))
                {
                    logger.LogInfo("Loading PlayerData.json...");
                    var playersWrapper = JsonConvert.DeserializeObject<NitroxPlayersData>(File.ReadAllText(playerDataPath));
                    worldData.PlayerData = playersWrapper?.Players ?? new List<PlayerData>();
                    logger.LogInfo($"Found {worldData.PlayerData.Count} player(s) in save.");
                }

                // 3. Load GlobalRootData (bases, world objects)
                string globalRootPath = Path.Combine(saveFolder, "GlobalRootData.json");
                if (File.Exists(globalRootPath))
                {
                    logger.LogInfo("Loading GlobalRootData.json...");
                    var globalWrapper = JsonConvert.DeserializeObject<NitroxEntitiesData>(File.ReadAllText(globalRootPath));
                    worldData.GlobalRootData = globalWrapper?.Entities ?? new List<GlobalEntityData>();
                    logger.LogInfo($"Found {worldData.GlobalRootData.Count} global root object(s).");
                }

                // 4. Load EntityData (world entities)
                string entityDataPath = Path.Combine(saveFolder, "EntityData.json");
                if (File.Exists(entityDataPath))
                {
                    logger.LogInfo("Loading EntityData.json...");
                    var entityWrapper = JsonConvert.DeserializeObject<NitroxEntitiesData>(File.ReadAllText(entityDataPath));
                    worldData.EntityData = entityWrapper?.Entities ?? new List<GlobalEntityData>();
                    logger.LogInfo($"Found {worldData.EntityData.Count} world entit(ies).");
                }

                logger.LogInfo("All Nitrox JSON files loaded. Starting world conversion...");
                WorldSpawner.SpawnWorld(worldData, logger, this);
            }
            catch (System.Exception ex)
            {
                logger.LogError($"NitroxSceneSpawner Error during conversion: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void OnGUI()
        {
            if (!hasTriggeredConversion)
            {
                // Draw a beautiful premium UI element in the top-right corner to show mod status
                int readyWidth = 350;
                int readyHeight = 90;
                int readyPadding = 20;
                Rect readyBoxRect = new Rect(Screen.width - readyWidth - readyPadding, readyPadding, readyWidth, readyHeight);

                // Lazy initialize background texture reference to avoid GC / recreating each frame
                if (backgroundTex == null)
                {
                    backgroundTex = new Texture2D(1, 1);
                    backgroundTex.SetPixel(0, 0, new Color(0.08f, 0.12f, 0.20f, 0.85f)); // Deep dark blue-grey semi-transparent
                    backgroundTex.Apply();
                }

                GUIStyle readyBoxStyle = new GUIStyle(GUI.skin.box);
                readyBoxStyle.normal.background = backgroundTex;
                readyBoxStyle.border = new RectOffset(4, 4, 4, 4);

                GUI.Box(readyBoxRect, GUIContent.none, readyBoxStyle);

                // Add inner container padding
                GUILayout.BeginArea(new Rect(readyBoxRect.x + 15, readyBoxRect.y + 12, readyBoxRect.width - 30, readyBoxRect.height - 24));

                // Modern premium title with custom font styling
                GUIStyle readyTitleStyle = new GUIStyle(GUI.skin.label);
                readyTitleStyle.fontStyle = FontStyle.Bold;
                readyTitleStyle.fontSize = 13;
                readyTitleStyle.normal.textColor = new Color(0.0f, 0.8f, 1f, 1f); // Sleek glowing cyan
                GUILayout.Label("Nitrox Singleplayer Converter", readyTitleStyle);

                GUILayout.Space(2);

                // Check if player is still inside the starting Lifepod
                bool insidePod = Player.main != null && Player.main.currentEscapePod != null;
                float pulse = 0.5f + Mathf.PingPong(Time.time * 1.5f, 0.5f);
                GUIStyle readyStatusStyle = new GUIStyle(GUI.skin.label);
                readyStatusStyle.fontSize = 11;
                readyStatusStyle.fontStyle = FontStyle.Bold;

                if (insidePod)
                {
                    // Player still in Lifepod — show yellow/orange waiting status
                    readyStatusStyle.normal.textColor = new Color(1f, 0.75f, 0.1f, pulse); // Amber pulsing glow
                    GUILayout.Label("● Waiting... Leave the Lifepod first!", readyStatusStyle);
                }
                else
                {
                    // Player has left the Lifepod — show green active status
                    readyStatusStyle.normal.textColor = new Color(0.0f, 1f, 0.5f, pulse); // Emerald green pulsing glow
                    GUILayout.Label("● Mod Status: Active & Ready", readyStatusStyle);
                }

                GUILayout.Space(4);

                // Action instruction
                GUIStyle readyInstructionStyle = new GUIStyle(GUI.skin.label);
                readyInstructionStyle.fontSize = 10;
                if (insidePod)
                {
                    readyInstructionStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1f); // Greyed out
                    GUILayout.Label("Leave the Lifepod to enable conversion.", readyInstructionStyle);
                }
                else
                {
                    readyInstructionStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 1f); // Off-white
                    GUILayout.Label("Press [F10] or [Ctrl + L] to start save conversion.", readyInstructionStyle);
                }

                GUILayout.EndArea();
                return;
            }

            if (!IsActive) return;

            // Draw a high-tech glassmorphic progress box in the top-right corner
            int width = 350;
            int height = 100;
            int padding = 20;
            Rect boxRect = new Rect(Screen.width - width - padding, padding, width, height);

            // Lazy initialize texture references to avoid GC / recreating each frame
            if (backgroundTex == null)
            {
                backgroundTex = new Texture2D(1, 1);
                backgroundTex.SetPixel(0, 0, new Color(0.08f, 0.12f, 0.20f, 0.85f)); // Deep dark blue-grey semi-transparent
                backgroundTex.Apply();
            }

            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = backgroundTex;
            boxStyle.border = new RectOffset(4, 4, 4, 4);

            GUI.Box(boxRect, GUIContent.none, boxStyle);

            // Add inner container padding
            GUILayout.BeginArea(new Rect(boxRect.x + 15, boxRect.y + 10, boxRect.width - 30, boxRect.height - 20));

            // Modern premium title with custom font styling
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.fontSize = 13;
            titleStyle.normal.textColor = new Color(0.0f, 0.8f, 1f, 1f); // Sleek glowing cyan
            GUILayout.Label("Nitrox Singleplayer Converter", titleStyle);

            // Status label
            GUIStyle statusStyle = new GUIStyle(GUI.skin.label);
            statusStyle.fontSize = 11;
            statusStyle.normal.textColor = Color.white;
            GUILayout.Label(StatusText, statusStyle);

            GUILayout.Space(5);

            // Progress bar container rect
            Rect progressRect = GUILayoutUtility.GetRect(width - 30, 16);
            
            // Draw progress bar background (dark sleek line)
            if (progressBgTex == null)
            {
                progressBgTex = new Texture2D(1, 1);
                progressBgTex.SetPixel(0, 0, new Color(0.05f, 0.08f, 0.12f, 0.9f));
                progressBgTex.Apply();
            }

            GUIStyle progressBgStyle = new GUIStyle();
            progressBgStyle.normal.background = progressBgTex;
            GUI.Box(progressRect, GUIContent.none, progressBgStyle);

            // Draw progress bar fill (vibrant glowing cyan gradient look)
            float fillWidth = progressRect.width * Mathf.Clamp01(Progress);
            if (fillWidth > 0)
            {
                Rect fillRect = new Rect(progressRect.x, progressRect.y, fillWidth, progressRect.height);
                if (progressFillTex == null)
                {
                    progressFillTex = new Texture2D(1, 1);
                    progressFillTex.SetPixel(0, 0, new Color(0f, 0.75f, 1f, 0.9f)); // Glowing cyan
                    progressFillTex.Apply();
                }

                GUIStyle progressFillStyle = new GUIStyle();
                progressFillStyle.normal.background = progressFillTex;
                GUI.Box(fillRect, GUIContent.none, progressFillStyle);
            }

            // Draw percentage label in the middle of progress bar
            GUIStyle percentStyle = new GUIStyle(GUI.skin.label);
            percentStyle.alignment = TextAnchor.MiddleCenter;
            percentStyle.fontStyle = FontStyle.Bold;
            percentStyle.fontSize = 9;
            percentStyle.normal.textColor = Color.white;
            GUI.Label(progressRect, $"{(Mathf.Clamp01(Progress) * 100f):F0}%", percentStyle);

            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            // Clean up dynamically allocated textures to prevent memory leaks in Unity
            if (backgroundTex != null) Destroy(backgroundTex);
            if (progressBgTex != null) Destroy(progressBgTex);
            if (progressFillTex != null) Destroy(progressFillTex);
        }
    }

    // Harmony patch: Prevent NullReferenceException in BaseFiltrationMachineGeometry.UpdateVisuals()
    // Root cause: GetModule() returns null when FiltrationMachine._moduleFace doesn't match
    // the geometry face coordinates in the Base grid system after conversion.
    [HarmonyPatch(typeof(BaseFiltrationMachineGeometry), "UpdateVisuals")]
    public static class BaseFiltrationMachineGeometry_UpdateVisuals_Patch
    {
        private static readonly FieldInfo s_moduleField = typeof(BaseFiltrationMachineGeometry)
            .GetField("module", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo s_initializedField = typeof(BaseFiltrationMachineGeometry)
            .GetField("initialized", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPrefix]
        public static bool Prefix(BaseFiltrationMachineGeometry __instance)
        {
            if (__instance == null) return false;

            // Fast path: if module is already linked and valid, let the original run
            if (s_moduleField != null)
            {
                var fm = s_moduleField.GetValue(__instance) as FiltrationMachine;
                if (fm != null && fm.storageContainer != null && fm.storageContainer.container != null)
                {
                    return true; // Module is valid, original method is safe
                }

                // Module is null - try to resolve it
                if (fm == null)
                {
                    // Method 1: Use Base.GetModule(face) - the game's own lookup
                    var baseComp = __instance.GetComponentInParent<Base>();
                    if (baseComp != null)
                    {
                        try
                        {
                            var face = __instance.geometryFace;
                            var resolved = baseComp.GetModule(face) as FiltrationMachine;
                            if (resolved != null)
                            {
                                s_moduleField.SetValue(__instance, resolved);
                                fm = resolved;
                            }
                        }
                        catch { }
                    }

                    // Method 2: Find nearest FiltrationMachine by distance
                    if (fm == null)
                    {
                        float minDist = float.MaxValue;
                        FiltrationMachine closest = null;
                        foreach (var candidate in Object.FindObjectsOfType<FiltrationMachine>())
                        {
                            if (candidate == null || candidate.storageContainer == null) continue;
                            float d = Vector3.Distance(__instance.transform.position, candidate.transform.position);
                            if (d < 6f && d < minDist)
                            {
                                minDist = d;
                                closest = candidate;
                            }
                        }
                        if (closest != null)
                        {
                            s_moduleField.SetValue(__instance, closest);
                            fm = closest;
                        }
                    }
                }

                // Final safety check: if module is STILL null or storage is bad, skip entirely
                if (fm == null || fm.storageContainer == null || fm.storageContainer.container == null)
                {
                    // Mark as initialized to prevent the method from being called on non-dirty frames
                    if (s_initializedField != null)
                    {
                        s_initializedField.SetValue(__instance, true);
                    }
                    return false; // Skip original - prevents NRE
                }
            }
            else
            {
                return false; // Fail-safe
            }

            return true;
        }
    }

    // Harmony patch: Safety net for BaseFiltrationMachineGeometry.OnUpdate() → UpdateVisuals() calls
    [HarmonyPatch(typeof(BaseFiltrationMachineGeometry), "OnUpdate")]
    public static class BaseFiltrationMachineGeometry_OnUpdate_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseFiltrationMachineGeometry __instance)
        {
            if (__instance == null) return false;
            return true;
        }

        [HarmonyFinalizer]
        public static System.Exception Finalizer(System.Exception __exception)
        {
            // Swallow any NRE that slips through
            if (__exception is System.NullReferenceException)
            {
                return null;
            }
            return __exception;
        }
    }

    // Harmony patch: Ensure FiltrationMachine has valid base and power relay references after Start()
    [HarmonyPatch(typeof(FiltrationMachine), "Start")]
    public static class FiltrationMachine_Start_Patch
    {
        private static readonly FieldInfo s_baseCompField = typeof(FiltrationMachine)
            .GetField("baseComp", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo s_powerRelayField = typeof(FiltrationMachine)
            .GetField("powerRelay", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPostfix]
        public static void Postfix(FiltrationMachine __instance)
        {
            if (__instance == null) return;

            if (s_baseCompField != null && s_powerRelayField != null)
            {
                var baseComp = s_baseCompField.GetValue(__instance) as Base;
                var powerRelay = s_powerRelayField.GetValue(__instance) as PowerRelay;

                if (baseComp == null || powerRelay == null)
                {
                    var sub = WorldSpawner.GetClosestSubRoot(__instance.transform.position, true);
                    if (sub != null)
                    {
                        if (baseComp == null)
                        {
                            baseComp = sub.GetComponent<Base>() ?? sub.GetComponentInChildren<Base>(true);
                            s_baseCompField.SetValue(__instance, baseComp);
                        }
                        if (powerRelay == null)
                        {
                            powerRelay = sub.powerRelay ?? sub.GetComponent<PowerRelay>() ?? sub.GetComponentInChildren<PowerRelay>(true);
                            s_powerRelayField.SetValue(__instance, powerRelay);
                        }
                    }
                }
            }
        }
    }

    // Harmony patch: Ensure BaseNuclearReactor has a valid power relay and source after Start()
    [HarmonyPatch(typeof(BaseNuclearReactor), "Start")]
    public static class BaseNuclearReactor_Start_Patch
    {
        private static readonly FieldInfo s_powerRelayField = typeof(BaseNuclearReactor)
            .GetField("_powerRelay", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo s_powerSourceField = typeof(BaseNuclearReactor)
            .GetField("_powerSource", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPostfix]
        public static void Postfix(BaseNuclearReactor __instance)
        {
            if (__instance == null) return;

            if (s_powerRelayField != null && s_powerSourceField != null)
            {
                var powerRelay = s_powerRelayField.GetValue(__instance) as PowerRelay;
                if (powerRelay == null)
                {
                    var sub = WorldSpawner.GetClosestSubRoot(__instance.transform.position, true);
                    if (sub != null)
                    {
                        powerRelay = sub.powerRelay ?? sub.GetComponent<PowerRelay>() ?? sub.GetComponentInChildren<PowerRelay>(true);
                        s_powerRelayField.SetValue(__instance, powerRelay);
                        
                        var ps = s_powerSourceField.GetValue(__instance) as PowerSource;
                        if (ps == null)
                        {
                            ps = __instance.GetComponent<PowerSource>();
                            s_powerSourceField.SetValue(__instance, ps);
                        }
                        if (ps != null && powerRelay != null)
                        {
                            if (ps.maxPower <= 0f) ps.maxPower = 2500f;
                            powerRelay.AddInboundPower(ps);
                        }
                    }
                }
            }
        }
    }

    // Harmony patch: Ensure BaseBioReactor has a valid power relay and source after Start()
    [HarmonyPatch(typeof(BaseBioReactor), "Start")]
    public static class BaseBioReactor_Start_Patch
    {
        private static readonly FieldInfo s_powerRelayField = typeof(BaseBioReactor)
            .GetField("_powerRelay", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo s_powerSourceField = typeof(BaseBioReactor)
            .GetField("_powerSource", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPostfix]
        public static void Postfix(BaseBioReactor __instance)
        {
            if (__instance == null) return;

            if (s_powerRelayField != null && s_powerSourceField != null)
            {
                var powerRelay = s_powerRelayField.GetValue(__instance) as PowerRelay;
                if (powerRelay == null)
                {
                    var sub = WorldSpawner.GetClosestSubRoot(__instance.transform.position, true);
                    if (sub != null)
                    {
                        powerRelay = sub.powerRelay ?? sub.GetComponent<PowerRelay>() ?? sub.GetComponentInChildren<PowerRelay>(true);
                        s_powerRelayField.SetValue(__instance, powerRelay);
                        
                        var ps = s_powerSourceField.GetValue(__instance) as PowerSource;
                        if (ps == null)
                        {
                            ps = __instance.GetComponent<PowerSource>();
                            s_powerSourceField.SetValue(__instance, ps);
                        }
                        if (ps != null && powerRelay != null)
                        {
                            if (ps.maxPower <= 0f) ps.maxPower = 500f;
                            powerRelay.AddInboundPower(ps);
                        }
                    }
                }
            }
        }
    }
}
