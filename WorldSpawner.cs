using System;
using System.IO;
using System.IO.Compression;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using BepInEx.Logging;

namespace NitroxConverter
{
    public static class WorldSpawner
    {
        private static readonly Dictionary<string, GameObject> spawnedBases = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, GameObject> spawnedVehicles = new Dictionary<string, GameObject>();

        public static void SpawnWorld(PersistedWorldData data, ManualLogSource logger, MonoBehaviour coroutineRunner)
        {
            if (data == null)
            {
                logger.LogError("World data is Null!");
                return;
            }

            coroutineRunner.StartCoroutine(SpawnRoutine(data, logger, coroutineRunner));
        }

        private static IEnumerator SpawnRoutine(PersistedWorldData data, ManualLogSource logger, MonoBehaviour runner)
        {
            logger.LogInfo("=== Conversion: Waiting for game world to fully initialize... ===");
            
            NitroxSceneSpawner spawnerInfo = runner as NitroxSceneSpawner;
            if (spawnerInfo != null)
            {
                spawnerInfo.IsActive = true;
                spawnerInfo.Progress = 0f;
                spawnerInfo.StatusText = "Initializing save conversion...";
            }

            // 1. Wait until player character exists and is active
            while (Player.main == null || !Player.main.gameObject.activeInHierarchy)
            {
                yield return new WaitForSeconds(1f);
            }

            // Temporarily activate cheats
            logger.LogInfo("Temporarily activating oxygen and invulnerability cheats for conversion...");
            try
            {
                GameModeUtils.ActivateCheat(GameModeOption.NoOxygen);
                if (NoDamageConsoleCommand.main != null)
                {
                    NoDamageConsoleCommand.main.SetNoDamageCheat(true);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Could not activate cheats: {ex.Message}");
            }

            // 2. Wait for terrain
            logger.LogInfo("Player character loaded! Waiting 10 seconds for voxel streamer and collisions...");
            if (spawnerInfo != null)
            {
                spawnerInfo.Progress = 0.02f;
                spawnerInfo.StatusText = "Waiting for voxel streamer & terrain (10s)...";
            }
            yield return new WaitForSeconds(10f);

            // 3. PASS 1: Spawn only bases (geometry build)
            spawnedBases.Clear();
            spawnedVehicles.Clear();
            if (data.GlobalRootData != null)
            {
            logger.LogInfo("=== PASS 1: Spawning base geometries... ===");
                foreach (var globalObj in data.GlobalRootData)
                {
                    if (globalObj.BaseData != null)
                    {
                        if (spawnerInfo != null)
                        {
                            spawnerInfo.StatusText = "Spawning base geometry...";
                        }
                        runner.StartCoroutine(SpawnEntityRecursive(globalObj, null, runner, logger, data, true));
                    }
                }
            }

            // Wait for base collisions
            logger.LogInfo("Waiting 5 seconds for base collision initialization...");
            if (spawnerInfo != null)
            {
                spawnerInfo.Progress = 0.1f;
                spawnerInfo.StatusText = "Initializing base collisions (5s)...";
            }
            yield return new WaitForSeconds(5f);

            // 4. PASS 2: Spawn all non-vehicle root objects and base children
            if (data.GlobalRootData != null)
            {
            logger.LogInfo("=== PASS 2: Spawning equipment, decorations, and base contents... ===");
                int totalObjects = data.GlobalRootData.Count;
                int currentCount = 0;
                foreach (var globalObj in data.GlobalRootData)
                {
                    if (spawnerInfo != null)
                    {
                        spawnerInfo.Progress = 0.1f + ((float)currentCount / totalObjects) * 0.4f;
                        string objName = globalObj.TechType != "None" ? globalObj.TechType : (globalObj.BaseData != null ? "Base Contents" : "World Object");
                        spawnerInfo.StatusText = $"Spawning {objName} ({currentCount + 1}/{totalObjects})...";
                    }

                    // SKIP vehicles in Pass 2 - they come in Pass 3!
                    string tt = globalObj.TechType ?? "None";
                    if (tt == "Cyclops" || tt == "Seamoth" || tt == "Exosuit")
                    {
                        currentCount++;
                        continue;
                    }

                    if (globalObj.BaseData != null)
                    {
                        // Spawn base children in Pass 2 (WITHOUT vehicles!)
                        if (spawnedBases.TryGetValue(globalObj.Id, out GameObject baseGo) && baseGo != null)
                        {
                            if (globalObj.ChildEntities != null)
                            {
                                foreach (var child in globalObj.ChildEntities)
                                {
                            // Skip vehicles in base children (e.g. Seamoth in Moonpool)
                                    string childTech = child.TechType ?? "None";
                                    if (childTech == "Cyclops" || childTech == "Seamoth" || childTech == "Exosuit")
                                    {
                                        continue;
                                    }
                                    // Traverse children and skip vehicles deeper in the tree
                                    runner.StartCoroutine(SpawnEntityRecursive(child, baseGo.transform, runner, logger, data, false));
                                }
                            }
                        }
                    }
                    else
                    {
                        runner.StartCoroutine(SpawnEntityRecursive(globalObj, null, runner, logger, data, false));
                    }
                    
                    currentCount++;
                    yield return new WaitForSeconds(0.1f);
                }
            }

            // Wait 3 seconds so base contents are fully initialized
            yield return new WaitForSeconds(3f);

            // 5. PASS 3: Spawn all vehicles dedicated and correctly
            logger.LogInfo("=== PASS 3: Spawning vehicles with names, colors, upgrades... ===");
            if (spawnerInfo != null)
            {
                spawnerInfo.Progress = 0.55f;
                spawnerInfo.StatusText = "Spawning vehicles...";
            }

            List<GlobalEntityData> allVehicles = new List<GlobalEntityData>();
            CollectVehicleEntities(data.GlobalRootData, allVehicles);

            for (int i = 0; i < allVehicles.Count; i++)
            {
                var vehicleData = allVehicles[i];
                if (spawnerInfo != null)
                {
                    string vName = vehicleData.Metadata?.Value<string>("Name") ?? vehicleData.TechType;
                    spawnerInfo.Progress = 0.55f + ((float)i / allVehicles.Count) * 0.25f;
                    spawnerInfo.StatusText = $"Spawning vehicle: {vName} ({i + 1}/{allVehicles.Count})...";
                }
                yield return runner.StartCoroutine(SpawnVehicle(vehicleData, runner, logger, data));
                yield return new WaitForSeconds(0.5f);
            }

            // Wait 2 seconds for physics to settle down
            yield return new WaitForSeconds(2f);

            // 6. Restore player profile, equipment & position
            string configPlayerName = NitroxConverterPlugin.PlayerNameConfig.Value;
            if (string.IsNullOrEmpty(configPlayerName) || 
                configPlayerName.Equals("Player", StringComparison.OrdinalIgnoreCase))
            {
                if (data.PlayerData != null && data.PlayerData.Count > 0)
                {
                    configPlayerName = data.PlayerData[0].Name;
                    logger.LogInfo($"[RestorePlayer] No specific player name configured or default value detected. Automatically using first profile from save: {configPlayerName}");
                }
                else
                {
                    configPlayerName = "Player";
                }
            }

            if (spawnerInfo != null)
            {
                spawnerInfo.Progress = 0.85f;
                spawnerInfo.StatusText = $"Loading player profile: {configPlayerName}...";
            }
            yield return runner.StartCoroutine(RestorePlayerProfile(data, configPlayerName, logger));

            yield return new WaitForSeconds(1f);

            // 7. Blueprints, encyclopedia & story goals
            if (data.WorldData?.GameData != null)
            {
                if (spawnerInfo != null)
                {
                    spawnerInfo.Progress = 0.95f;
                    spawnerInfo.StatusText = "Synchronizing PDA, blueprints & story goals...";
                }
                var gameData = data.WorldData.GameData;

                // Blueprints
                if (gameData.PDAState?.KnownTechTypes != null)
                {
                    logger.LogInfo($"Unlocking {gameData.PDAState.KnownTechTypes.Count} known blueprints...");
                    foreach (string techStr in gameData.PDAState.KnownTechTypes)
                    {
                        if (Enum.TryParse(techStr, true, out TechType techType))
                        {
                            try { KnownTech.Add(techType, false); }
                            catch (Exception ex) { logger.LogWarning($"Error unlocking blueprint {techType}: {ex.Message}"); }
                        }
                    }
                }

                // Encyclopedia
                if (gameData.PDAState?.EncyclopediaEntries != null)
                {
                    logger.LogInfo($"Unlocking {gameData.PDAState.EncyclopediaEntries.Count} encyclopedia entries...");
                    foreach (string encKey in gameData.PDAState.EncyclopediaEntries)
                    {
                        try { PDAEncyclopedia.Add(encKey, false); }
                        catch (Exception ex) { logger.LogWarning($"Error unlocking encyclopedia entry {encKey}: {ex.Message}"); }
                    }
                }

                // PDA Logs (Voice Logs, Audio Logs, Radio Logs)
                if (gameData.PDAState?.PdaLog != null)
                {
                    logger.LogInfo($"Unlocking {gameData.PDAState.PdaLog.Count} PDA logs/audio logs...");
                    foreach (var logEntry in gameData.PDAState.PdaLog)
                    {
                        if (logEntry != null && !string.IsNullOrEmpty(logEntry.Key))
                        {
                            try
                            {
                                // PDALog.Add registers the entry in the static PDALog.entries list
                                PDALog.Add(logEntry.Key, false);

                                // Update the entry's timestamp using reflection
                                var entriesField = typeof(PDALog).GetField("entries", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                                if (entriesField != null)
                                {
                                    var entriesDict = entriesField.GetValue(null) as System.Collections.IDictionary;
                                    if (entriesDict != null && entriesDict.Contains(logEntry.Key))
                                    {
                                        var entry = entriesDict[logEntry.Key];
                                        if (entry != null)
                                        {
                                            var timestampField = entry.GetType().GetField("timestamp", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                            if (timestampField != null)
                                            {
                                                timestampField.SetValue(entry, logEntry.Timestamp);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning($"Error unlocking PDA log entry '{logEntry.Key}': {ex.Message}");
                            }
                        }
                    }
                }

                // Story-Goals
                if (gameData.StoryGoals?.CompletedGoals != null && Story.StoryGoalManager.main != null)
                {
                    logger.LogInfo($"Registering {gameData.StoryGoals.CompletedGoals.Count} story goals...");
                    try
                    {
                        var completedGoalsField = typeof(Story.StoryGoalManager).GetField("completedGoals", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (completedGoalsField != null)
                        {
                            var completedGoalsSet = completedGoalsField.GetValue(Story.StoryGoalManager.main) as HashSet<string>;
                            if (completedGoalsSet != null)
                            {
                                foreach (string goalKey in gameData.StoryGoals.CompletedGoals)
                                {
                                    try
                                    {
                                        // OnGoalComplete registers the goal and fires all native events (like signal/ping triggers)
                                        Story.StoryGoalManager.main.OnGoalComplete(goalKey);
                                    }
                                    catch (Exception eGoal)
                                    {
                                        logger.LogWarning($"[Story] Error during OnGoalComplete for '{goalKey}': {eGoal.Message}");
                                        // Fallback: Directly add to the set if the engine fails
                                        completedGoalsSet.Add(goalKey);
                                    }
                                }

                                // Forced visibility protect loop for all lifepods/signals in the world
                                try
                                {
                                    var pings = GameObject.FindObjectsOfType<PingInstance>();
                                    foreach (var ping in pings)
                                    {
                                        if (ping != null)
                                        {
                                            string lbl = ping.GetLabel() ?? "";
                                            string pingTypeStr = ping.pingType.ToString();
                                            if (lbl.Contains("Lifepod") || lbl.Contains("Rettungskapsel") || lbl.Contains("Kapsel") || pingTypeStr.Contains("Signal") || pingTypeStr.Contains("Lifepod"))
                                            {
                                                ping.SetVisible(true);
                                                logger.LogInfo($"[PingRestore] Signal made visible: {lbl} (Type: {pingTypeStr})");
                                            }
                                        }
                                    }
                                }
                                catch (Exception ePing)
                                {
                                    logger.LogWarning($"[PingRestore] Error making signals visible: {ePing.Message}");
                                }

                                logger.LogInfo("Story goals successfully registered and triggered!");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError($"Error during story goals building: {ex.Message}");
                    }
                }             

                // Game Time
                if (DayNightCycle.main != null)
                {
                    DayNightCycle.main.timePassedAsDouble = (double)gameData.StoryTiming.ElapsedSeconds;
                }
            }

            // 8. Switch to Survival mode
            logger.LogInfo("Switching to Survival mode...");
            try
            {
                GameModeUtils.SetGameMode(GameModeOption.Survival, GameModeOption.None);
                if (SaveLoadManager.main != null)
                {
                    var gameModeField = typeof(SaveLoadManager).GetField("gameMode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (gameModeField != null)
                    {
                        gameModeField.SetValue(SaveLoadManager.main, Enum.ToObject(gameModeField.FieldType, 0));
                    }
                }
                logger.LogInfo("Game mode successfully switched to Survival!");
            }
            catch (Exception ex)
            {
                logger.LogError($"Error switching to Survival mode: {ex.Message}");
            }

            // Register base power devices with a DELAY to allow base Start() methods to fully complete!
            // Nuclear reactors and filtration machines need their Start() coroutines to finish first.
            logger.LogInfo("Starting DELAYED base power device registration (waiting 8 seconds for base initialization)...");
            runner.StartCoroutine(DelayedRegisterAllBasePowerDevices(logger, 8f));

            // Disable spawner freezing loop to allow vehicles to move and physics to run!
            if (spawnerInfo != null)
            {
                spawnerInfo.IsActive = false;
                logger.LogInfo("Disabled temporary spawner physics freeze.");
            }

            // Unfreeze Seamoth/Exosuit vehicles and Cyclops
            logger.LogInfo("Unfreezing Seamoth/Exosuit and Cyclops vehicles...");
            try
            {
                foreach (var v in GameObject.FindObjectsOfType<Vehicle>())
                {
                    if (v != null)
                    {
                        var vRb = v.GetComponent<Rigidbody>();
                        if (vRb != null && vRb.isKinematic)
                        {
                            vRb.isKinematic = false;
                            logger.LogInfo($"[Physics] Unfroze Vehicle: {v.gameObject.name}");
                        }
                    }
                }
                
                // Unfreeze and level all Cyclops submarines
                foreach (var sub in GameObject.FindObjectsOfType<SubRoot>())
                {
                    if (sub != null && sub.isCyclops)
                    {
                        var subRb = sub.GetComponent<Rigidbody>();
                        if (subRb != null)
                        {
                            subRb.isKinematic = false;
                            subRb.velocity = Vector3.zero;
                            subRb.angularVelocity = Vector3.zero;
                            logger.LogInfo($"[Physics] Unfroze Cyclops: {sub.gameObject.name}");
                        }
                        
                        // Force perfectly level rotation at the end to guarantee it's stable and level
                        Vector3 euler = sub.transform.rotation.eulerAngles;
                        sub.transform.rotation = Quaternion.Euler(0f, euler.y, 0f);
                        logger.LogInfo($"[Physics] Finalized Cyclops leveling (Yaw: {euler.y:F1})");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error restoring vehicle physics: {ex.Message}");
            }

            if (spawnerInfo != null)
            {
                spawnerInfo.Progress = 1.0f;
                spawnerInfo.StatusText = "Success! Cheats active for 15s...";
            }

            logger.LogInfo("=== Conversion complete! Please save the game manually now! ===");
            
            yield return new WaitForSeconds(15f);

            logger.LogInfo("Deactivating temporary cheats...");
            try
            {
                GameModeUtils.DeactivateCheat(GameModeOption.NoOxygen);
                if (NoDamageConsoleCommand.main != null)
                {
                    NoDamageConsoleCommand.main.SetNoDamageCheat(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Could not deactivate cheats: {ex.Message}");
            }

            logger.LogInfo("Destroying converter helper GameObject. Conversion fully complete!");
            GameObject.Destroy(runner.gameObject);
        }

        // ===========================================================================================
        // VEHICLES: Collect all vehicle entities from the entire entity tree
        // ===========================================================================================
        private static void CollectVehicleEntities(List<GlobalEntityData> entities, List<GlobalEntityData> result)
        {
            if (entities == null) return;
            foreach (var e in entities)
            {
                if (e == null) continue;

                string tt = e.TechType ?? "None";
                if (tt == "Cyclops" || tt == "Seamoth" || tt == "Exosuit")
                {
                    result.Add(e);
                }
                // Also search in children (e.g. Exosuit in Cyclops docking bay)
                CollectVehicleEntities(e.ChildEntities, result);
            }
        }

        // ===========================================================================================
        // VEHICLES: Dedicated spawn method with name, colors, upgrades, health, power cells
        // ===========================================================================================
        private static IEnumerator SpawnVehicle(GlobalEntityData vehicleData, MonoBehaviour runner, ManualLogSource logger, PersistedWorldData data)
        {
            if (vehicleData == null) yield break;

            Enum.TryParse(vehicleData.TechType, true, out TechType techType);
            if (techType == TechType.None) yield break;

            // Vehicles in Nitrox saves always use absolute world coordinates
            Vector3 worldPos = ConvertVector3(vehicleData.Transform.LocalPosition);
            Quaternion worldRot = ConvertQuaternion(vehicleData.Transform.LocalRotation);

            // For Cyclops, force level rotation (pitch = 0, roll = 0) so the player can walk inside properly!
            if (techType == TechType.Cyclops)
            {
                Vector3 euler = worldRot.eulerAngles;
                worldRot = Quaternion.Euler(0f, euler.y, 0f);
            }

            logger.LogInfo($"[SpawnVehicle] Spawning {techType} at position {worldPos}...");

            GameObject go = null;

            if (techType == TechType.Cyclops)
            {
                // Load Cyclops scene prefab directly
                GameObject prefab = null;
                bool isLoaded = false;
                
                LightmappedPrefabs.main.RequestScenePrefab("cyclops", (GameObject p) => {
                    prefab = p;
                    isLoaded = true;
                });
                
                while (!isLoaded)
                {
                    yield return null;
                }
                
                if (prefab != null)
                {
                    // Level the rotation immediately at birth (pitch=0, roll=0)
                    Vector3 eulerRot = worldRot.eulerAngles;
                    Quaternion levelRot = Quaternion.Euler(0f, eulerRot.y, 0f);
                    
                    go = Utils.SpawnPrefabAt(prefab, null, worldPos);
                    go.transform.rotation = levelRot;
                    go.SetActive(true);
                    
                    // CRITICAL: Keep it kinematic frozen while 61 interior items are being spawned!
                    var cyclopsRb = go.GetComponent<Rigidbody>();
                    if (cyclopsRb != null)
                    {
                        cyclopsRb.isKinematic = true;
                        cyclopsRb.velocity = Vector3.zero;
                        cyclopsRb.angularVelocity = Vector3.zero;
                    }
                    
                    // Trigger the game's native initialization messages
                    go.SendMessage("StartConstruction", SendMessageOptions.DontRequireReceiver);
                    LargeWorldEntity.Register(go);
                    CrafterLogic.NotifyCraftEnd(go, TechType.Cyclops);
                    
                    logger.LogInfo($"[SpawnVehicle] Cyclops successfully spawned, leveled, and kinematic-frozen for item placement! Position: {worldPos}, Yaw: {eulerRot.y}");
                }
                
                if (go == null)
                {
                    logger.LogError("[SpawnVehicle] Cyclops scene prefab could not be loaded or instantiated!");
                    yield break;
                }
            }
            else
            {
                // Seamoth / Exosuit: Spawn via standard prefab system
                var request = CraftData.GetPrefabForTechTypeAsync(techType, false);
                yield return request;
                GameObject prefab = request.GetResult();
                
                if (prefab == null)
                {
                    yield return new WaitForSeconds(2f);
                    request = CraftData.GetPrefabForTechTypeAsync(techType, false);
                    yield return request;
                    prefab = request.GetResult();
                }

                if (prefab == null)
                {
                    logger.LogWarning($"[SpawnVehicle] Could not load prefab for {techType}!");
                    yield break;
                }

                go = GameObject.Instantiate(prefab, worldPos, worldRot);
            }

            // Ensure correct position and rotation (for non-Cyclops only, Cyclops is handled above)
            if (techType != TechType.Cyclops)
            {
                go.transform.position = worldPos;
                go.transform.rotation = worldRot;
            }

            // Only freeze Seamoth/Exosuit kinematic temporarily - NOT the Cyclops!
            // Freezing the Cyclops kinematic prevents WorldForces/buoyancy from self-righting it.
            if (techType != TechType.Cyclops)
            {
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }

            // Finish constructable
            var constructable = go.GetComponent<Constructable>();
            if (constructable != null)
            {
                constructable.SetState(true, true);
            }

            // Store reference for later use
            if (vehicleData.Id != null)
            {
                spawnedVehicles[vehicleData.Id] = go;
            }

            // ============ NAME & COLORS ============
            yield return new WaitForSeconds(0.2f); // Wait for SubName to initialize

            var subName = go.GetComponent<SubName>() ?? go.GetComponentInChildren<SubName>(true);
            if (subName != null && vehicleData.Metadata != null)
            {
                // Seamoth and Exosuit have Name/Colors directly in their Metadata
                ApplyNameAndColors(subName, vehicleData.Metadata, logger);
            }

            // Cyclops: Name and colors are in a child entity (SubNameInput)
            if (techType == TechType.Cyclops && vehicleData.ChildEntities != null)
            {
                foreach (var child in vehicleData.ChildEntities)
                {
                    if (child.Metadata != null)
                    {
                        string metaType = child.Metadata["$type"]?.ToString() ?? "";
                        if (metaType.Contains("SubNameInputMetadata"))
                        {
                            logger.LogInfo($"[SpawnVehicle] Found Cyclops Name/Colors in SubNameInput child...");
                            if (subName != null)
                            {
                                ApplyNameAndColors(subName, child.Metadata, logger);
                            }
                            break;
                        }
                    }
                }
            }

            // ============ HEALTH (LiveMixin) ============
            if (vehicleData.Metadata != null)
            {
                try
                {
                    var healthToken = vehicleData.Metadata["Health"];
                    if (healthToken != null)
                    {
                        float health = (float)healthToken;
                        var liveMixin = go.GetComponent<LiveMixin>();
                        if (liveMixin != null)
                        {
                            liveMixin.health = health;
                            logger.LogInfo($"[SpawnVehicle] Health set to {health}.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"[SpawnVehicle] Could not set health: {ex.Message}");
                }
            }

            // ============ UPGRADE MODULES & POWER CELLS ============
            var vehicle = go.GetComponent<Vehicle>();
            if (vehicle != null && vehicleData.ChildEntities != null)
            {
                logger.LogInfo($"[SpawnVehicle] Processing {vehicleData.ChildEntities.Count} children for {techType}...");
                foreach (var child in vehicleData.ChildEntities)
                {
                    if (child == null) continue;
                    string childTech = child.TechType ?? "None";

                    // PowerCells are skipped (loaded automatically by the vehicle)
                    if (childTech == "PowerCell" || childTech == "Battery" || childTech == "None")
                    {
                        continue;
                    }

                    // Upgrade modules with slot assignment
                    if (!string.IsNullOrEmpty(child.Slot))
                    {
                        if (Enum.TryParse(childTech, true, out TechType modTechType))
                        {
                            logger.LogInfo($"[SpawnVehicle] Installing module {modTechType} in slot {child.Slot}...");
                            var modRequest = CraftData.GetPrefabForTechTypeAsync(modTechType, false);
                            yield return modRequest;
                            GameObject modPrefab = modRequest.GetResult();
                            
                            if (modPrefab != null)
                            {
                                try
                                {
                                    GameObject modGo = GameObject.Instantiate(modPrefab);
                                    modGo.SetActive(false);
                                    
                                    var em = modGo.GetComponent<EnergyMixin>();
                                    if (em != null) em.OnCraftEnd(modTechType);
                                    
                                    var pickupable = modGo.GetComponent<Pickupable>();
                                    if (pickupable != null)
                                    {
                                        InventoryItem modItem = new InventoryItem(pickupable);
                                        vehicle.modules.AddItem(child.Slot, modItem, true);
                                        logger.LogInfo($"[SpawnVehicle] Module {modTechType} installed in slot {child.Slot}.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.LogWarning($"[SpawnVehicle] Error installing module {modTechType}: {ex.Message}");
                                }
                            }
                        }
                        yield return new WaitForSeconds(0.05f);
                    }
                }
            }

            // Cyclops: Install UpgradeConsole modules (Cyclops uses its own upgrade system)
            if (techType == TechType.Cyclops && vehicleData.ChildEntities != null)
            {
                var upgradeConsole = go.GetComponentInChildren<UpgradeConsole>(true);
                if (upgradeConsole != null)
                {
                    foreach (var child in vehicleData.ChildEntities)
                    {
                        if (child == null || string.IsNullOrEmpty(child.Slot)) continue;
                        string childTech = child.TechType ?? "None";
                        if (childTech == "None" || childTech == "PowerCell" || childTech == "Battery") continue;

                        if (Enum.TryParse(childTech, true, out TechType modTechType))
                        {
                            logger.LogInfo($"[SpawnVehicle] Cyclops UpgradeConsole: Installing {modTechType} in slot {child.Slot}...");
                            var modRequest = CraftData.GetPrefabForTechTypeAsync(modTechType, false);
                            yield return modRequest;
                            GameObject modPrefab = modRequest.GetResult();
                            
                            if (modPrefab != null)
                            {
                                try
                                {
                                    GameObject modGo = GameObject.Instantiate(modPrefab);
                                    modGo.SetActive(false);
                                    var pickupable = modGo.GetComponent<Pickupable>();
                                    if (pickupable != null)
                                    {
                                        InventoryItem modItem = new InventoryItem(pickupable);
                                        upgradeConsole.modules.AddItem(child.Slot, modItem, true);
                                        logger.LogInfo($"[SpawnVehicle] Cyclops module {modTechType} installed.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.LogWarning($"[SpawnVehicle] Cyclops module installation error: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }

            // ============ CYCLOPS INTERIOR: Lockers, fabricator, etc. ============
            if (techType == TechType.Cyclops && vehicleData.ChildEntities != null)
            {
                logger.LogInfo($"[SpawnVehicle] Spawning {vehicleData.ChildEntities.Count} Cyclops interior objects...");
                foreach (var child in vehicleData.ChildEntities)
                {
                    if (child == null) continue;
                    string childTech = child.TechType ?? "None";
                    
                    if (childTech == "Cyclops" || childTech == "Seamoth" || childTech == "Exosuit") continue;
                    if (childTech == "PowerCell" || childTech == "Battery") continue;
                    if (!string.IsNullOrEmpty(child.Slot)) continue; // Upgrade modules already installed
                    if (childTech == "None") continue; // PathBasedChildEntity (SubName etc.)

                    // Spawn as child of Cyclops (lockers, fabricator, posters, radio, etc.)
                    runner.StartCoroutine(SpawnEntityRecursive(child, go.transform, runner, logger, data, false));
                    yield return new WaitForSeconds(0.05f);
                }
            }

            // ============ DOCKING ============
            // Check if vehicle should be docked in a docking bay
            {
                // Find the closest matching docking bay
                if (techType == TechType.Seamoth || techType == TechType.Exosuit)
                {
                    VehicleDockingBay closestBay = FindClosestDockingBay(worldPos, techType, logger);
                    if (closestBay != null && vehicle != null)
                    {
                        float dist = Vector3.Distance(worldPos, closestBay.transform.position);
                        if (dist < 25f) // Only dock if vehicle is close enough to a bay
                        {
                            logger.LogInfo($"[SpawnVehicle] Docking {techType} in {closestBay.gameObject.name} (distance: {dist:F1}m)...");
                            try
                            {
                                closestBay.DockVehicle(vehicle, true);
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning($"[SpawnVehicle] Docking failed: {ex.Message}");
                            }
                        }
                    }
                }
            }

            // ============ BATTERIES / ENERGY CELLS ============
            var energyMixins = go.GetComponentsInChildren<EnergyMixin>(true);
            logger.LogInfo($"[SpawnVehicle] Found {energyMixins.Length} EnergyMixins on {techType}");
            foreach (var em in energyMixins)
            {
                if (em != null)
                {
                    try
                    {
                        if (em.GetBattery() == null)
                        {
                            logger.LogInfo($"[SpawnVehicle] Spawning default battery for {go.name} - EnergyMixin ({em.gameObject.name})...");
                            em.StartCoroutine(SpawnDefaultBatteryForEnergyMixin(em, logger));
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning($"[SpawnVehicle] Failed to spawn default battery for EnergyMixin: {ex.Message}");
                    }

                    // Delayed active battery charging to guarantee vehicle has full power
                    runner.StartCoroutine(ChargeEnergyMixinDelayed(em, logger));
                }
            }

            logger.LogInfo($"[SpawnVehicle] {techType} spawned successfully!");
        }

        private static IEnumerator SpawnDefaultBatteryForEnergyMixin(EnergyMixin em, ManualLogSource logger)
        {
            IEnumerator spawnRoutine = null;
            try
            {
                var spawnMethod = em.GetType().GetMethod("SpawnDefaultBatteryAsync", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (spawnMethod != null)
                {
                    spawnRoutine = (IEnumerator)spawnMethod.Invoke(em, null);
                }
                else
                {
                    var spawnMethodAsync = em.GetType().GetMethod("SpawnDefaultAsync", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (spawnMethodAsync != null)
                    {
                        spawnRoutine = (IEnumerator)spawnMethodAsync.Invoke(em, new object[] { 1f, null });
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"[EnergyMixinHelper] Failed to invoke battery spawn: {ex.Message}");
            }

            if (spawnRoutine != null)
            {
                yield return em.StartCoroutine(spawnRoutine);
            }
        }

        private static IEnumerator ChargeEnergyMixinDelayed(EnergyMixin em, ManualLogSource logger)
        {
            // Wait 3 seconds to ensure any asynchronous battery spawning or prefab initialization is 100% complete
            yield return new WaitForSeconds(3f);
            if (em == null) yield break;
            
            logger.LogInfo($"[ChargeEnergyMixin] Processing {em.gameObject.name} on {em.transform.root.gameObject.name}...");
            
            bool needsSpawn = false;
            try
            {
                needsSpawn = (em.GetBattery() == null);
            }
            catch (Exception ex)
            {
                logger.LogWarning($"[ChargeEnergyMixin] Failed to check battery: {ex.Message}");
            }

            if (needsSpawn)
            {
                logger.LogInfo($"[ChargeEnergyMixin] Battery is still missing on {em.gameObject.name}, forcing SpawnDefaultBatteryAsync...");
                
                yield return em.StartCoroutine(SpawnDefaultBatteryForEnergyMixin(em, logger));
                // Wait another short moment for the battery slot to be populated
                yield return new WaitForSeconds(0.5f);
            }

            try
            {
                var battery = em.GetBattery();
                if (battery != null)
                {
                    float maxCap = battery.capacity;
                    if (maxCap <= 0f) maxCap = em.capacity;
                    if (maxCap <= 0f) maxCap = em.maxEnergy;
                    if (maxCap <= 0f) maxCap = 100f; // Default fallback
                    
                    battery.charge = maxCap;
                    em.AddEnergy(maxCap);
                    
                    var energyField = em.GetType().GetField("energy", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (energyField != null)
                    {
                        energyField.SetValue(em, maxCap);
                    }
                    
                    logger.LogInfo($"[ChargeEnergyMixin] Battery charge set to {battery.charge}/{battery.capacity}. EnergyMixin charge: {em.charge}");
                }
                else
                {
                    logger.LogWarning($"[ChargeEnergyMixin] Battery remains null for {em.gameObject.name} even after spawning attempts.");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"[ChargeEnergyMixin] Failed to charge EnergyMixin: {ex.Message}");
            }
        }

        private static VehicleDockingBay FindClosestDockingBay(Vector3 pos, TechType vehicleType, ManualLogSource logger)
        {
            VehicleDockingBay closest = null;
            float minDist = float.MaxValue;

            foreach (var bay in GameObject.FindObjectsOfType<VehicleDockingBay>())
            {
                float dist = Vector3.Distance(pos, bay.transform.position);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = bay;
                }
            }

            return closest;
        }

        // ===========================================================================================
        // RESTORE PLAYER PROFILE
        // ===========================================================================================
        private static IEnumerator RestorePlayerProfile(PersistedWorldData data, string playerName, ManualLogSource logger)
        {
            if (data.PlayerData == null || data.PlayerData.Count == 0 || Inventory.main == null) yield break;

            PlayerData targetPlayer = data.PlayerData.Find(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));
            if (targetPlayer == null)
            {
                logger.LogWarning($"Player profile '{playerName}' not found. Using fallback to the first player.");
                targetPlayer = data.PlayerData[0];
            }

            logger.LogInfo($"Loading player profile: {targetPlayer.Name} (NitroxId: {targetPlayer.NitroxId})...");

            // 1. Teleport player to their saved position
            Vector3 targetPos = ConvertVector3(targetPlayer.SpawnPosition);
            Quaternion targetRot = new Quaternion(targetPlayer.SpawnRotation.X, targetPlayer.SpawnRotation.Y, targetPlayer.SpawnRotation.Z, targetPlayer.SpawnRotation.W);

            logger.LogInfo($"[RestorePlayer] Teleporting player to saved position: {targetPos}");
            SafeTeleport(targetPos, targetPlayer.SpawnRotation, logger);
            yield return new WaitForSeconds(0.5f);

            // 1b. Restore player statistics
            if (targetPlayer.CurrentStats != null)
            {
                try
                {
                    logger.LogInfo("[RestorePlayer] Setting player statistics (Health, Food, Water)...");
                    var survival = Player.main.GetComponent<Survival>();
                    if (survival != null)
                    {
                        if (targetPlayer.CurrentStats.TryGetValue("Food", out float food))
                        {
                            survival.food = food;
                        }
                        if (targetPlayer.CurrentStats.TryGetValue("Water", out float water))
                        {
                            survival.water = water;
                        }
                    }

                    if (targetPlayer.CurrentStats.TryGetValue("Health", out float health) && Player.main.liveMixin != null)
                    {
                        Player.main.liveMixin.health = health;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"[RestorePlayer] Error loading player stats: {ex.Message}");
                }
            }
            
            // Set player currentSub to the closest base/sub
            try
            {
                var closestSub = GetClosestSubRoot(targetPos);
                if (closestSub != null)
                {
                    logger.LogInfo($"Setting current sub of player to: {closestSub.gameObject.name} (isBase: {closestSub.isBase})");
                    Player.main.SetCurrentSub(closestSub);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Error setting player sub: {ex.Message}");
            }

            // 2. Prepare equipment slots (Key -> SlotName, Value -> Item ID)
            Dictionary<string, string> equippedMapping = new Dictionary<string, string>();
            if (targetPlayer.EquippedItems != null)
            {
                foreach (var kvp in targetPlayer.EquippedItems)
                {
                    equippedMapping[kvp.Value] = kvp.Key;
                }
            }

            // 3. Search for PlayerEntity in GlobalRootData or EntityData
            GlobalEntityData playerEntity = FindPlayerEntity(data.GlobalRootData, targetPlayer.NitroxId);
            if (playerEntity == null)
            {
                playerEntity = FindPlayerEntity(data.EntityData, targetPlayer.NitroxId);
            }

            if (playerEntity != null && playerEntity.ChildEntities != null)
            {
                logger.LogInfo($"Found PlayerEntity with {playerEntity.ChildEntities.Count} items in inventory.");
                
                // Clear default starting inventory
                Inventory.main.container.Clear();

                foreach (var childItem in playerEntity.ChildEntities)
                {
                    if (Enum.TryParse(childItem.TechType, true, out TechType techType))
                    {
                        var request = CraftData.GetPrefabForTechTypeAsync(techType, false);
                        yield return request;

                        GameObject prefab = request.GetResult();
                        
                        if (prefab == null)
                        {
                            yield return new WaitForSeconds(1f);
                            request = CraftData.GetPrefabForTechTypeAsync(techType, false);
                            yield return request;
                            prefab = request.GetResult();
                        }

                        if (prefab == null)
                        {
                            logger.LogWarning($"Prefab for {techType} could not be loaded.");
                            continue;
                        }

                        try
                        {
                            GameObject itemGo = GameObject.Instantiate(prefab);
                            if (itemGo == null) continue;
                            
                            // Load battery for tools
                            var energyMixin = itemGo.GetComponent<EnergyMixin>();
                            if (energyMixin != null)
                            {
                                logger.LogInfo($"Loading battery for tool {techType}...");
                                energyMixin.OnCraftEnd(techType);
                            }
                            
                            var pickupable = itemGo.GetComponent<Pickupable>();
                            if (pickupable != null)
                            {
                                itemGo.SetActive(false);
                                var itemRb = itemGo.GetComponent<Rigidbody>();
                                if (itemRb != null) itemRb.isKinematic = true;

                                InventoryItem invItem = new InventoryItem(pickupable);
                                if (equippedMapping.TryGetValue(childItem.Id, out string slotName))
                                {
                                    logger.LogInfo($"Equipping item: {techType} in slot {slotName}");
                                    try
                                    {
                                        InventoryItem existingItem = Inventory.main.equipment.GetItemInSlot(slotName);
                                        if (existingItem != null && existingItem.item != null)
                                        {
                                            Inventory.main.equipment.RemoveItem(existingItem.item);
                                        }
                                        Inventory.main.equipment.AddItem(slotName, invItem, true);
                                    }
                                    catch (Exception slotEx)
                                    {
                                        logger.LogWarning($"Could not equip {techType} in slot {slotName} ({slotEx.Message}). Adding to inventory.");
                                        Inventory.main.container.UnsafeAdd(invItem);
                                    }
                                }
                                else
                                {
                                    Inventory.main.container.UnsafeAdd(invItem);
                                }
                            }
                        }
                        catch (Exception itemEx)
                        {
                            logger.LogError($"Error loading inventory item {childItem.TechType}: {itemEx.Message}");
                        }

                        yield return new WaitForSeconds(0.02f);
                    }
                }
            }
        }

        // ===========================================================================================
        // GENERIC ENTITY SPAWNING (for bases, devices, lockers, decoration)
        // ===========================================================================================
        private static TechType GetTechTypeFromClassId(string classId)
        {
            if (string.IsNullOrEmpty(classId)) return TechType.None;

            // Hardcoded fallbacks for known base devices
            if (classId == "864f7780-a4c3-4bf2-b9c7-f4296388b70f") return TechType.BaseNuclearReactor;
            if (classId == "2f2d8419-c55b-49ac-9698-ecb431fffed2") return TechType.BaseFiltrationMachine;
            if (classId == "769f9f44-30f6-46ed-aaf6-fbba358e1676") return TechType.BaseBioReactor;
            if (classId == "4e8c0174-777f-4e66-8e0f-f73d0e82015b") return TechType.Fabricator;

            try
            {
                // 1. Versuche CraftData.GetTechTypeForClassId via Reflection
                var getTechTypeMethod = typeof(CraftData).GetMethod("GetTechTypeForClassId", BindingFlags.Public | BindingFlags.Static);
                if (getTechTypeMethod != null)
                {
                    object[] args = new object[] { classId, TechType.None };
                    bool success = (bool)getTechTypeMethod.Invoke(null, args);
                    if (success)
                    {
                        return (TechType)args[1];
                    }
                }
            }
            catch {}

            try
            {
                // 2. Versuche UWE.PrefabDatabase via Reflection
                var prefabDatabaseType = System.Type.GetType("UWE.PrefabDatabase, Assembly-CSharp-firstpass") 
                                      ?? System.Type.GetType("UWE.PrefabDatabase, Assembly-CSharp");
                if (prefabDatabaseType != null)
                {
                    var tryGetPrefabMethod = prefabDatabaseType.GetMethod("TryGetPrefab", BindingFlags.Public | BindingFlags.Static);
                    if (tryGetPrefabMethod != null)
                    {
                        object[] args = new object[] { classId, null };
                        bool success = (bool)tryGetPrefabMethod.Invoke(null, args);
                        if (success && args[1] != null)
                        {
                            var getTechTypeMethod = typeof(CraftData).GetMethod("GetTechType", new[] { typeof(GameObject) });
                            if (getTechTypeMethod != null)
                            {
                                return (TechType)getTechTypeMethod.Invoke(null, new object[] { args[1] });
                            }
                        }
                    }
                    
                    var getPrefabMethod = prefabDatabaseType.GetMethod("GetPrefab", new[] { typeof(string) });
                    if (getPrefabMethod != null)
                    {
                        GameObject prefab = getPrefabMethod.Invoke(null, new object[] { classId }) as GameObject;
                        if (prefab != null)
                        {
                            var getTechTypeMethod = typeof(CraftData).GetMethod("GetTechType", new[] { typeof(GameObject) });
                            if (getTechTypeMethod != null)
                            {
                                return (TechType)getTechTypeMethod.Invoke(null, new object[] { prefab });
                            }
                        }
                    }
                }
            }
            catch {}

            return TechType.None;
        }

        private static IEnumerator SpawnEntityRecursive(GlobalEntityData entityData, Transform parentTransform, MonoBehaviour runner, ManualLogSource logger, PersistedWorldData data, bool onlyBases = false)
        {
            if (entityData == null) yield break;

            // Skip player entity
            if (entityData.Id != null && data != null && data.PlayerData != null && data.PlayerData.Exists(p => p.NitroxId == entityData.Id))
            {
                logger.LogInfo($"[SpawnEntityRecursive] Skipping player entity {entityData.Id}.");
                yield break;
            }

            TechType techType = TechType.None;
            bool isBase = entityData.BaseData != null;

            if (onlyBases && !isBase)
            {
                yield break;
            }

            if (!isBase)
            {
                if (!string.IsNullOrEmpty(entityData.ClassId))
                {
                    techType = GetTechTypeFromClassId(entityData.ClassId);
                    if (techType == TechType.None)
                    {
                        Enum.TryParse(entityData.ClassId, true, out techType);
                    }
                }
                if (techType == TechType.None && !string.IsNullOrEmpty(entityData.TechType))
                {
                    Enum.TryParse(entityData.TechType, true, out techType);
                }
            }

            // Do NOT spawn vehicles here - they have their own method!
            if (techType == TechType.Cyclops || techType == TechType.Seamoth || techType == TechType.Exosuit)
            {
                yield break;
            }

            // Skip unspawnable types (continue processing recursive children)
            if (!isBase && (techType == TechType.None || (entityData.TechType == "None")))
            {
                if (entityData.ChildEntities != null)
                {
                    foreach (var child in entityData.ChildEntities)
                    {
                        // Skip vehicles in children too
                        string childTT = child?.TechType ?? "None";
                        if (childTT == "Cyclops" || childTT == "Seamoth" || childTT == "Exosuit") continue;
                        runner.StartCoroutine(SpawnEntityRecursive(child, parentTransform, runner, logger, data, onlyBases));
                    }
                }
                yield break;
            }

            // Upgrade modules on vehicles/UpgradeConsoles (NOT here - done in SpawnVehicle)
            if (parentTransform != null && !string.IsNullOrEmpty(entityData.Slot))
            {
                var parentGo = parentTransform.gameObject;
                var vehicleComp = parentGo.GetComponent<Vehicle>();
                var upgradeConsole = parentGo.GetComponent<UpgradeConsole>() ?? parentGo.GetComponentInChildren<UpgradeConsole>(true);
                
                if (vehicleComp != null || upgradeConsole != null)
                {
                    logger.LogInfo($"Equipping {techType} in slot {entityData.Slot} of {parentGo.name}...");
                    var modRequest = CraftData.GetPrefabForTechTypeAsync(techType, false);
                    yield return modRequest;
                    GameObject modPrefab = modRequest.GetResult();
                    if (modPrefab != null)
                    {
                        GameObject modGo = GameObject.Instantiate(modPrefab);
                        modGo.SetActive(false);
                        
                        var em = modGo.GetComponent<EnergyMixin>();
                        if (em != null) em.OnCraftEnd(techType);
                        
                        var pickupable = modGo.GetComponent<Pickupable>();
                        if (pickupable != null)
                        {
                            InventoryItem modItem = new InventoryItem(pickupable);
                            if (vehicleComp != null)
                            {
                                vehicleComp.modules.AddItem(entityData.Slot, modItem, true);
                            }
                            else if (upgradeConsole != null)
                            {
                                upgradeConsole.modules.AddItem(entityData.Slot, modItem, true);
                            }
                            logger.LogInfo($"Module {techType} successfully equipped!");
                        }
                    }
                    yield break;
                }
            }

            Vector3 localPos = Vector3.zero;
            Quaternion localRot = Quaternion.identity;

            if (entityData.Transform != null)
            {
                if (entityData.Transform.LocalPosition != null)
                {
                    localPos = ConvertVector3(entityData.Transform.LocalPosition);
                }
                if (entityData.Transform.LocalRotation != null)
                {
                    localRot = ConvertQuaternion(entityData.Transform.LocalRotation);
                }
            }

            Transform parentForSpawn = parentTransform;
            if (parentTransform != null)
            {
                var subRoot = parentTransform.GetComponent<SubRoot>() ?? parentTransform.GetComponentInParent<SubRoot>();
                if (subRoot != null)
                {
                    var modulesRoot = subRoot.GetModulesRoot();
                    if (modulesRoot != null)
                    {
                        parentForSpawn = modulesRoot;
                    }
                }
            }

            Vector3 worldPos = parentForSpawn != null ? parentForSpawn.TransformPoint(localPos) : localPos;
            Quaternion worldRot = parentForSpawn != null ? parentForSpawn.rotation * localRot : localRot;

            GameObject go = null;

            if (isBase)
            {
                logger.LogInfo("Instantiating base prefab via BaseGhost.basePrefab...");
                GameObject basePrefab = null;
                try
                {
                    var basePrefabField = typeof(BaseGhost).GetField("basePrefab", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                                       ?? typeof(BaseGhost).GetField("_basePrefab", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (basePrefabField != null)
                    {
                        basePrefab = basePrefabField.GetValue(null) as GameObject;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"Could not read basePrefab via reflection: {ex.Message}");
                }

                if (basePrefab != null)
                {
                    go = GameObject.Instantiate(basePrefab, worldPos, worldRot);
                    if (go != null && entityData.Id != null)
                    {
                        spawnedBases[entityData.Id] = go;
                    }
                }
                else
                {
                    logger.LogWarning("basePrefab is Null! Using empty GameObject as fallback.");
                    go = new GameObject("Base");
                    go.transform.position = worldPos;
                    go.transform.rotation = worldRot;
                }
            }
            else
            {
                logger.LogInfo($"Instantiating object {techType}...");
                var request = CraftData.GetPrefabForTechTypeAsync(techType, false);
                yield return request;

                GameObject prefab = request.GetResult();
                
                if (prefab == null)
                {
                    logger.LogWarning($"Prefab for {techType} delayed. Waiting 2 seconds...");
                    yield return new WaitForSeconds(2f);
                    request = CraftData.GetPrefabForTechTypeAsync(techType, false);
                    yield return request;
                    prefab = request.GetResult();
                }

                if (prefab == null)
                {
                    logger.LogWarning($"Prefab for {techType} could not be loaded.");
                    yield break;
                }

                if (parentForSpawn != null)
                {
                    go = GameObject.Instantiate(prefab, parentForSpawn);
                    go.transform.position = worldPos;
                    go.transform.rotation = worldRot;
                }
                else
                {
                    go = GameObject.Instantiate(prefab, worldPos, worldRot);
                }
            }

            if (go == null)
            {
                yield break;
            }

            if (parentForSpawn != null)
            {
                go.transform.position = worldPos;
                go.transform.rotation = worldRot;
            }

            if (parentForSpawn == null)
            {
                var sub = GetClosestSubRoot(go.transform.position);
                if (sub != null && sub.isBase)
                {
                    bool shouldParent = go.GetComponent<BaseNuclearReactor>() != null ||
                                        go.GetComponent<FiltrationMachine>() != null ||
                                        go.GetComponent<BaseBioReactor>() != null ||
                                        go.GetComponent<Constructable>() != null ||
                                        go.GetComponent<StorageContainer>() != null ||
                                        go.GetComponent<Charger>() != null;
                                        
                    if (shouldParent)
                    {
                        var modulesRoot = sub.GetModulesRoot();
                        parentForSpawn = modulesRoot ?? sub.transform;
                        go.transform.SetParent(parentForSpawn, true);
                        logger.LogInfo($"[Reparent] Parented {techType} to base {sub.gameObject.name}");
                    }
                }
            }



            // Rigidbody stabilisieren
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                if (isBase)
                {
                    rb.isKinematic = true;
                }
            }

            // Finish constructable
            var constructable = go.GetComponent<Constructable>();
            if (constructable != null)
            {
                constructable.SetState(true, true);
            }

            // Build base
            if (isBase)
            {
                try
                {
                    BuildBase(go, entityData.BaseData, logger);
                    go.SetActive(true);
                }
                catch (Exception ex)
                {
                    logger.LogError($"Error building base: {ex.Message}\n{ex.StackTrace}");
                }
            }

            // Apply names, colors & locker signs
            if (go != null)
            {
                var subName = go.GetComponent<SubName>() ?? go.GetComponentInChildren<SubName>(true);
                if (subName != null && entityData.Metadata != null)
                {
                    ApplyNameAndColors(subName, entityData.Metadata, logger);
                }

                var signInput = go.GetComponent<uGUI_SignInput>() ?? go.GetComponentInChildren<uGUI_SignInput>(true);
                if (signInput != null && entityData.Metadata != null)
                {
                    string text = entityData.Metadata.Value<string>("Text");
                    if (!string.IsNullOrEmpty(text))
                    {
                        logger.LogInfo($"Applying locker sign text '{text}'...");
                        try
                        {
                            signInput.text = text;
                        }
                        catch
                        {
                            var textProp = signInput.GetType().GetProperty("text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (textProp != null) textProp.SetValue(signInput, text);
                        }
                    }
                }
            }

            // Generic container finder via reflection!
            // Searches for any ItemsContainer fields or properties in all components
            object targetContainer = null;
            
            // 1. Special case for StorageContainer (also in children, e.g. for FiltrationMachine)
            var storageComp = go.GetComponent<StorageContainer>() ?? go.GetComponentInChildren<StorageContainer>(true);
            if (storageComp != null && storageComp.container != null)
            {
                targetContainer = storageComp.container;
            }
            
            // 2. Generic case for other components (reactors, etc.)
            if (targetContainer == null)
            {
                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    
                    // Suche nach einem Feld vom Typ "ItemsContainer"
                    var fields = comp.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    foreach (var field in fields)
                    {
                        if (field.FieldType.Name == "ItemsContainer" || field.FieldType.Name == "IItemsContainer")
                        {
                            var val = field.GetValue(comp);
                            if (val != null)
                            {
                                targetContainer = val;
                                logger.LogInfo($"[SpawnEntityRecursive] Generic ItemsContainer found in field {field.Name} of {comp.GetType().Name}!");
                                break;
                            }
                        }
                    }
                    if (targetContainer != null) break;

                    // Suche nach einer Eigenschaft vom Typ "ItemsContainer"
                    var props = comp.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    foreach (var prop in props)
                    {
                        if (prop.PropertyType.Name == "ItemsContainer" || prop.PropertyType.Name == "IItemsContainer")
                        {
                            try
                            {
                                var val = prop.GetValue(comp, null);
                                if (val != null)
                                {
                                    targetContainer = val;
                                    logger.LogInfo($"[SpawnEntityRecursive] Generic ItemsContainer found in property {prop.Name} of {comp.GetType().Name}!");
                                    break;
                                }
                            }
                            catch {}
                        }
                    }
                    if (targetContainer != null) break;
                }
            }

            // Container filling (lockers, reactors, filtration machine)
            if (targetContainer != null && entityData.ChildEntities != null)
            {
                var container = targetContainer as ItemsContainer;
                if (container != null)
                {
                    yield return new WaitForSeconds(0.1f);
                    container.Clear();
                    foreach (var childItem in entityData.ChildEntities)
                    {
                        if (childItem == null) continue;
                        
                        TechType itemTech = TechType.None;
                        if (!string.IsNullOrEmpty(childItem.ClassId))
                        {
                            itemTech = GetTechTypeFromClassId(childItem.ClassId);
                        }
                        if (itemTech == TechType.None && !string.IsNullOrEmpty(childItem.TechType))
                        {
                            Enum.TryParse(childItem.TechType, true, out itemTech);
                        }

                        if (itemTech != TechType.None)
                        {
                            var itemReq = CraftData.GetPrefabForTechTypeAsync(itemTech, false);
                            yield return itemReq;
                            GameObject itemPrefab = itemReq.GetResult();
                            
                            if (itemPrefab == null)
                            {
                                yield return new WaitForSeconds(0.5f);
                                itemReq = CraftData.GetPrefabForTechTypeAsync(itemTech, false);
                                yield return itemReq;
                                itemPrefab = itemReq.GetResult();
                            }

                            if (itemPrefab != null)
                            {
                                GameObject itemGo = GameObject.Instantiate(itemPrefab);
                                
                                var em = itemGo.GetComponent<EnergyMixin>();
                                if (em != null) em.OnCraftEnd(itemTech);
                                
                                var pickupable = itemGo.GetComponent<Pickupable>();
                                if (pickupable != null)
                                {
                                    itemGo.SetActive(false);
                                    var itemRb = itemGo.GetComponent<Rigidbody>();
                                    if (itemRb != null) itemRb.isKinematic = true;
                                    
                                    container.UnsafeAdd(new InventoryItem(pickupable));
                                    logger.LogInfo($"[SpawnEntityRecursive] {itemTech} added to container!");
                                }
                            }
                        }
                    }
                }
            }

            // Process children recursively (not for container contents and not for base geometries in Pass 1!)
            if (entityData.ChildEntities != null && targetContainer == null && !onlyBases)
            {
                foreach (var childData in entityData.ChildEntities)
                {
                    // Skip vehicles in children too
                    string childTT = childData?.TechType ?? "None";
                    if (childTT == "Cyclops" || childTT == "Seamoth" || childTT == "Exosuit") continue;
                    
                    runner.StartCoroutine(SpawnEntityRecursive(childData, go.transform, runner, logger, data, false));
                    yield return new WaitForSeconds(0.02f);
                }
            }
        }

        // ===========================================================================================
        // BASIS-AUFBAU
        // ===========================================================================================
        private static void BuildBase(GameObject baseGo, NitroxBaseData baseData, ManualLogSource logger)
        {
            try
            {
                Base baseComponent = baseGo.GetComponent<Base>();
                if (baseComponent == null)
                {
                    baseComponent = baseGo.AddComponent<Base>();
                }

                Int3 shape = new Int3((int)baseData.BaseShape.X, (int)baseData.BaseShape.Y, (int)baseData.BaseShape.Z);
                Int3 offset = new Int3((int)baseData.CellOffset.X, (int)baseData.CellOffset.Y, (int)baseData.CellOffset.Z);
                Int3 anchor = new Int3((int)baseData.Anchor.X, (int)baseData.Anchor.Y, (int)baseData.Anchor.Z);

                baseComponent.SetSize(shape);

                int baseSize = shape.x * shape.y * shape.z;
                logger.LogInfo($"[BuildBase] Shape = {shape.x}x{shape.y}x{shape.z}, Volume = {baseSize}");

                byte[] facesBytes = DecompressRleBytes(Convert.FromBase64String(baseData.Faces), baseSize * 6);
                byte[] cellsBytes = DecompressRleBytes(Convert.FromBase64String(baseData.Cells), baseSize);
                byte[] linksBytes = DecompressRleBytes(Convert.FromBase64String(baseData.Links), baseSize);
                byte[] masksBytes = baseData.Masks != null ? DecompressRleBytes(Convert.FromBase64String(baseData.Masks), baseSize) : null;
                byte[] isGlassBytes = DecompressRleBytes(Convert.FromBase64String(baseData.IsGlass), baseSize);

                var faceType = typeof(Base).GetNestedType("FaceType", BindingFlags.Public | BindingFlags.NonPublic);
                var cellType = typeof(Base).GetNestedType("CellType", BindingFlags.Public | BindingFlags.NonPublic);

                Array facesArray = Array.CreateInstance(faceType, baseSize * 6);
                for (int i = 0; i < baseSize * 6; i++)
                {
                    facesArray.SetValue(Enum.ToObject(faceType, facesBytes[i]), i);
                }

                Array cellsArray = Array.CreateInstance(cellType, baseSize);
                for (int i = 0; i < baseSize; i++)
                {
                    cellsArray.SetValue(Enum.ToObject(cellType, cellsBytes[i]), i);
                }

                bool[] isGlassArray = Array.ConvertAll(isGlassBytes, b => b != 0);

                SetPrivateField(baseComponent, "faces", facesArray);
                SetPrivateField(baseComponent, "cells", cellsArray);
                SetPrivateField(baseComponent, "links", linksBytes);
                SetPrivateField(baseComponent, "cellOffset", offset);
                if (masksBytes != null) SetPrivateField(baseComponent, "masks", masksBytes);
                SetPrivateField(baseComponent, "isGlass", isGlassArray);
                SetPrivateField(baseComponent, "anchor", anchor);

                Array previousfacesArray = Array.CreateInstance(faceType, baseSize * 6);
                Array.Copy(facesArray, previousfacesArray, baseSize * 6);
                SetPrivateField(baseComponent, "previousfaces", previousfacesArray);

                logger.LogInfo("[BuildBase] Basis-Strukturen eingetragen. Deserialisierung...");

                var onProtoDeserializeMethod = typeof(Base).GetMethod("OnProtoDeserialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (onProtoDeserializeMethod != null)
                {
                    try { onProtoDeserializeMethod.Invoke(baseComponent, new object[] { null }); }
                    catch (Exception ex) { logger.LogWarning($"[BuildBase] OnProtoDeserialize: {ex.Message}"); }
                }

                var finishedField = typeof(Base).GetField("deserializationFinished", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (finishedField != null) finishedField.SetValue(baseComponent, false);

                var finishDeserializationMethod = typeof(Base).GetMethod("FinishDeserialization", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (finishDeserializationMethod != null) finishDeserializationMethod.Invoke(baseComponent, null);

                logger.LogInfo("[BuildBase] Basis-Geometrie erzeugt!");
                
                var subRoot = baseGo.GetComponent<SubRoot>();
                if (subRoot != null)
                {
                    var subRootProtoMethod = typeof(SubRoot).GetMethod("OnProtoDeserialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (subRootProtoMethod != null)
                    {
                        try { subRootProtoMethod.Invoke(subRoot, new object[] { null }); }
                        catch (Exception ex) { logger.LogWarning($"[BuildBase] SubRoot OnProtoDeserialize: {ex.Message}"); }
                    }
                    InitializeSubRootPowerAndDryness(subRoot, logger);
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"[BuildBase] Exception: {ex.Message}");
                if (ex.InnerException != null)
                    logger.LogError($"[BuildBase] Inner: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}");
                else
                    logger.LogError($"[BuildBase] Stack: {ex.StackTrace}");
                throw;
            }
        }

        // ===========================================================================================
        // HILFSMETHODEN
        // ===========================================================================================

        private static byte[] DecompressRleBytes(byte[] compressedBytes, int targetSize)
        {
            if (compressedBytes == null) return null;
            byte[] result = new byte[targetSize];
            try
            {
                using (MemoryStream ms = new MemoryStream(compressedBytes))
                {
                    using (DeflateStream deflate = new DeflateStream(ms, CompressionMode.Decompress))
                    {
                        using (BinaryReader reader = new BinaryReader(deflate))
                        {
                            int index = 0;
                            bool isZeroRun = true;
                            while (index < targetSize)
                            {
                                try
                                {
                                    if (isZeroRun)
                                    {
                                        ushort zeroCount = reader.ReadUInt16();
                                        for (int i = 0; i < zeroCount && index < targetSize; i++)
                                        {
                                            result[index] = 0;
                                            index++;
                                        }
                                    }
                                    else
                                    {
                                        byte val = reader.ReadByte();
                                        result[index] = val;
                                        index++;
                                    }
                                }
                                catch (EndOfStreamException) { break; }
                                isZeroRun = !isZeroRun;
                            }
                        }
                    }
                }
            }
            catch (Exception) { }
            return result;
        }

        private static void SafeTeleport(Vector3 pos, Vector4Data rotData, ManualLogSource logger)
        {
            try
            {
                if (Player.main == null) return;

                var controller = Player.main.GetComponent<CharacterController>();
                if (controller != null) controller.enabled = false;

                Player.main.transform.position = pos;
                Player.main.transform.rotation = new Quaternion(rotData.X, rotData.Y, rotData.Z, rotData.W);

                var rb = Player.main.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                if (controller != null) controller.enabled = true;

                logger.LogInfo($"Player teleported successfully to {pos}.");
            }
            catch (Exception ex)
            {
                logger.LogError($"Error during teleportation: {ex.Message}");
            }
        }

        private static GlobalEntityData FindPlayerEntity(List<GlobalEntityData> entities, string nitroxId)
        {
            if (entities == null || string.IsNullOrEmpty(nitroxId)) return null;
            foreach (var e in entities)
            {
                if (e == null) continue;
                if (e.Id == nitroxId) return e;
                var found = FindPlayerEntity(e.ChildEntities, nitroxId);
                if (found != null) return found;
            }
            return null;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) field.SetValue(target, value);
        }

        private static Vector3 ConvertVector3(Vector3Data data)
        {
            return new Vector3(data.X, data.Y, data.Z);
        }

        private static Quaternion ConvertQuaternion(Vector4Data data)
        {
            return new Quaternion(data.X, data.Y, data.Z, data.W);
        }

        private static void ApplyNameAndColors(SubName subName, Newtonsoft.Json.Linq.JObject metadata, ManualLogSource logger)
        {
            if (subName == null || metadata == null) return;

            try
            {
                string name = metadata.Value<string>("Name");
                if (!string.IsNullOrEmpty(name))
                {
                    logger.LogInfo($"Applying name '{name}' to {subName.gameObject.name}...");
                    subName.DeserializeName(name);
                }

                var colorsToken = metadata["Colors"];
                if (colorsToken is Newtonsoft.Json.Linq.JArray colorsArray && colorsArray.Count > 0)
                {
                    logger.LogInfo($"Applying {colorsArray.Count} colors to {subName.gameObject.name}...");
                    Vector3[] unityColors = new Vector3[colorsArray.Count];
                    for (int i = 0; i < colorsArray.Count; i++)
                    {
                        var colorObj = colorsArray[i];
                        float x = colorObj.Value<float>("X");
                        float y = colorObj.Value<float>("Y");
                        float z = colorObj.Value<float>("Z");
                        unityColors[i] = new Vector3(x, y, z);
                    }
                    subName.DeserializeColors(unityColors);
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error applying name/colors: {ex.Message}");
            }
        }

        private static void InitializeSubRootPowerAndDryness(SubRoot subRoot, ManualLogSource logger)
        {
            if (subRoot == null) return;
            try
            {
                logger.LogInfo($"[SubRootInit] Power & dryness for: {subRoot.gameObject.name}");
                
                var isFloodedField = typeof(SubRoot).GetField("isFlooded", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (isFloodedField != null) isFloodedField.SetValue(subRoot, false);
                subRoot.floodFraction = 0f;

                var powerSource = subRoot.gameObject.GetComponent<PowerSource>();
                if (powerSource == null) powerSource = subRoot.gameObject.AddComponent<PowerSource>();
                powerSource.maxPower = 2000f;
                powerSource.power = 2000f;
                
                var powerRelay = subRoot.powerRelay;
                if (powerRelay == null)
                    powerRelay = subRoot.gameObject.GetComponent<PowerRelay>() ?? subRoot.gameObject.GetComponentInChildren<PowerRelay>(true);
                if (powerRelay != null)
                {
                    subRoot.powerRelay = powerRelay;
                    powerRelay.AddInboundPower(powerSource);
                }

                powerSource.UpdateConnection();

                var floodSim = subRoot.gameObject.GetComponent<BaseFloodSim>() ?? subRoot.gameObject.GetComponentInChildren<BaseFloodSim>(true);
                if (floodSim != null)
                {
                    try
                    {
                        floodSim.enabled = true;

                        var leakSpeedField = typeof(BaseFloodSim).GetField("leakSpeedPerHole", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (leakSpeedField != null) leakSpeedField.SetValue(floodSim, 0f);

                        var flowRateField = typeof(BaseFloodSim).GetField("flowRate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (flowRateField != null) flowRateField.SetValue(floodSim, 0f);

                        var bulkheadFlowRateField = typeof(BaseFloodSim).GetField("bulkheadFlowRate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (bulkheadFlowRateField != null) bulkheadFlowRateField.SetValue(floodSim, 0f);

                        var drainRateField = typeof(BaseFloodSim).GetField("drainRate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (drainRateField != null) drainRateField.SetValue(floodSim, 9999f);

                        var removeWaterMethod = floodSim.GetType().GetMethod("RemoveAllWater", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (removeWaterMethod != null) removeWaterMethod.Invoke(floodSim, null);
                    }
                    catch (Exception fEx)
                    {
                        logger.LogWarning($"[SubRootInit] BaseFloodSim error: {fEx.Message}");
                    }
                }

                var monoBehaviours = subRoot.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var mb in monoBehaviours)
                {
                    if (mb != null && mb.GetType().Name == "BaseFloodDamage")
                    {
                        mb.enabled = false;
                        GameObject.Destroy(mb);
                    }
                }

                logger.LogInfo($"[SubRootInit] Power: {powerSource.power}/{powerSource.maxPower}");
            }
            catch (Exception ex)
            {
                logger.LogError($"[SubRootInit] Error: {ex.Message}");
            }
        }

        // ===========================================================================================
        // DELAYED REGISTRATION: Waits for base systems to fully initialize before registering devices
        // ===========================================================================================
        private static IEnumerator DelayedRegisterAllBasePowerDevices(ManualLogSource logger, float delaySeconds)
        {
            yield return new WaitForSeconds(delaySeconds);
            
            logger.LogInfo("=== DELAYED REGISTRATION: Starting base power device registration ===");
            
            try
            {
                RegisterAllDevicesGlobally(logger);
            }
            catch (Exception ex)
            {
                logger.LogError($"[BasePowerDevices] First pass error: {ex.Message}\n{ex.StackTrace}");
            }
            
            // Second pass after 5 more seconds as safety net
            yield return new WaitForSeconds(5f);
            
            logger.LogInfo("=== DELAYED REGISTRATION: Safety pass (second attempt) ===");
            try
            {
                RegisterAllDevicesGlobally(logger);
            }
            catch (Exception ex)
            {
                logger.LogError($"[BasePowerDevices] Second pass error: {ex.Message}");
            }
            
            logger.LogInfo("=== DELAYED REGISTRATION: Complete ===");
        }



        private static void RegisterReactorGlobally<T>(SubRoot[] cache, string deviceName, float maxPower, ManualLogSource logger) where T : MonoBehaviour
        {
            var reactors = GameObject.FindObjectsOfType<T>();
            logger.LogInfo($"[BasePowerDevices] Found {reactors.Length} {deviceName}s globally");
            
            var prField = typeof(T).GetField("_powerRelay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var psField = typeof(T).GetField("_powerSource", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var reactor in reactors)
            {
                if (reactor == null) continue;
                try
                {
                    var subRoot = GetClosestSubRoot(reactor.transform.position, true, cache);
                    if (subRoot == null)
                    {
                        logger.LogWarning($"[BasePowerDevices] {deviceName} at {reactor.transform.position} has no closest base - skipping");
                        continue;
                    }

                    var basePowerRelay = subRoot.powerRelay 
                                      ?? subRoot.gameObject.GetComponent<PowerRelay>() 
                                      ?? subRoot.gameObject.GetComponentInChildren<PowerRelay>(true);

                    if (basePowerRelay == null) continue;

                    if (prField != null)
                    {
                        prField.SetValue(reactor, basePowerRelay);
                    }
                    
                    PowerSource ps = null;
                    if (psField != null)
                    {
                        ps = psField.GetValue(reactor) as PowerSource;
                    }
                    if (ps == null)
                    {
                        ps = reactor.gameObject.GetComponent<PowerSource>();
                    }
                    if (ps == null)
                    {
                        ps = reactor.gameObject.AddComponent<PowerSource>();
                    }
                    if (psField != null && ps != null)
                    {
                        psField.SetValue(reactor, ps);
                    }
                    
                    if (ps != null)
                    {
                        if (ps.maxPower <= 0f) ps.maxPower = maxPower;
                        if (ps.power <= 0f) ps.power = maxPower;
                        
                        try
                        {
                            basePowerRelay.AddInboundPower(ps);
                            logger.LogInfo($"[BasePowerDevices] {deviceName} connected to base: {subRoot.gameObject.name} (Power: {ps.power:F0}/{ps.maxPower:F0})");
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning($"[BasePowerDevices] {deviceName} connection failed: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"[BasePowerDevices] {deviceName} registration failed: {ex.Message}");
                }
            }
        }

        private static void RegisterAllDevicesGlobally(ManualLogSource logger)
        {
            logger.LogInfo("[BasePowerDevices] Scanning for base devices globally in the scene...");

            // Cache all SubRoots in the scene to avoid expensive FindObjectsOfType calls
            var cachedSubRoots = GameObject.FindObjectsOfType<SubRoot>();
            logger.LogInfo($"[BasePowerDevices] Cached {cachedSubRoots.Length} SubRoot objects in scene.");

            // 1. Register Nuclear Reactors
            RegisterReactorGlobally<BaseNuclearReactor>(cachedSubRoots, "Nuclear Reactor", 2500f, logger);

            // 2. Register Bio Reactors
            RegisterReactorGlobally<BaseBioReactor>(cachedSubRoots, "Bio Reactor", 500f, logger);

            // 3. Register Filtration Machines
            var filtrationMachines = GameObject.FindObjectsOfType<FiltrationMachine>();
            logger.LogInfo($"[BasePowerDevices] Found {filtrationMachines.Length} Filtration Machines globally");
            
            foreach (var fm in filtrationMachines)
            {
                if (fm == null) continue;
                try
                {
                    var subRoot = GetClosestSubRoot(fm.transform.position, true, cachedSubRoots);
                    if (subRoot == null)
                    {
                        logger.LogWarning($"[BasePowerDevices] Filtration Machine at {fm.transform.position} has no closest base - skipping");
                        continue;
                    }

                    var baseComp = subRoot.GetComponent<Base>() 
                                ?? subRoot.GetComponentInChildren<Base>(true);
                    
                    var basePowerRelay = subRoot.powerRelay 
                                      ?? subRoot.gameObject.GetComponent<PowerRelay>() 
                                      ?? subRoot.gameObject.GetComponentInChildren<PowerRelay>(true);

                    if (basePowerRelay == null) continue;

                    var prField = typeof(FiltrationMachine).GetField("powerRelay", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prField != null)
                    {
                        prField.SetValue(fm, basePowerRelay);
                    }
                    
                    var bcField = typeof(FiltrationMachine).GetField("baseComp", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (bcField != null && baseComp != null)
                    {
                        bcField.SetValue(fm, baseComp);
                    }
                    
                    if (baseComp != null)
                    {
                        var allGeoms = GameObject.FindObjectsOfType<BaseFiltrationMachineGeometry>();
                        BaseFiltrationMachineGeometry bestGeom = null;
                        float bestDist = float.MaxValue;
                        
                        foreach (var geom in allGeoms)
                        {
                            if (geom == null) continue;
                            float d = Vector3.Distance(fm.transform.position, geom.transform.position);
                            if (d < bestDist)
                            {
                                bestDist = d;
                                bestGeom = geom;
                            }
                        }
                        
                        if (bestGeom != null && bestDist < 10f)
                        {
                            Base.Face geomFace = bestGeom.geometryFace;
                            Int3 anchor = baseComp.GetAnchor();
                            Base.Face moduleFace = new Base.Face(geomFace.cell - anchor, geomFace.direction);
                            
                            fm.moduleFace = moduleFace;
                            logger.LogInfo($"[BasePowerDevices] Filtration Machine: Linked with moduleFace to base {subRoot.gameObject.name}");
                            
                            var modField = typeof(BaseFiltrationMachineGeometry).GetField("module", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (modField != null)
                            {
                                modField.SetValue(bestGeom, fm);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"[BasePowerDevices] Filtration Machine registration failed: {ex.Message}");
                }
            }
        }

        public static SubRoot GetClosestSubRoot(Vector3 pos, bool onlyBases = false, SubRoot[] cache = null)
        {
            SubRoot closest = null;
            float minDist = float.MaxValue;
            var subRoots = cache ?? GameObject.FindObjectsOfType<SubRoot>();
            foreach (var sub in subRoots)
            {
                if (sub == null) continue;
                if (onlyBases && !sub.isBase) continue;
                float dist = Vector3.Distance(pos, sub.transform.position);
                float maxDist = sub.isBase ? 150f : 50f;
                if (dist < maxDist && dist < minDist)
                {
                    minDist = dist;
                    closest = sub;
                }
            }
            return closest;
        }
    }
}
