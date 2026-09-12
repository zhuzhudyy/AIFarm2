using System;
using AIFarm.Activities;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Npc;
using AIFarm.Social;
using AIFarm.Time;
using AIFarm.Town;
using UnityEngine;

namespace AIFarm.Presentation
{
    public sealed class SaveGameService
    {
        private const int MaximumActionCount = 128;
        private const int MaximumResidentCount = 32;
        private const int MaximumUiTextLength = 512;
        private const float MaximumActionDurationSeconds = 600f;

        private readonly GameBootstrap bootstrap;
        private readonly NpcPlanExecutor executor;
        private readonly ReplanController replanner;
        private readonly Transform npcTransform;
        private readonly ISaveGameStorage storage;
        private readonly SocialGraph relationshipGraph;
        private readonly Vector3 initialNpcPosition;
        private readonly Quaternion initialNpcRotation;

        public SaveGameService(
            GameBootstrap gameBootstrap,
            NpcPlanExecutor planExecutor,
            ReplanController replanController,
            Transform npc,
            ISaveGameStorage saveStorage,
            SocialGraph socialGraph = null)
        {
            bootstrap = gameBootstrap ?? throw new ArgumentNullException(nameof(gameBootstrap));
            executor = planExecutor ?? throw new ArgumentNullException(nameof(planExecutor));
            replanner = replanController ?? throw new ArgumentNullException(nameof(replanController));
            npcTransform = npc ?? throw new ArgumentNullException(nameof(npc));
            storage = saveStorage ?? throw new ArgumentNullException(nameof(saveStorage));
            relationshipGraph = socialGraph;
            initialNpcPosition = npcTransform.position;
            initialNpcRotation = npcTransform.rotation;
        }

        public string SavePath => storage.SavePath;

        public bool LastLoadUsedSafeReplan { get; private set; }

        public bool LastLoadMigratedLegacySave { get; private set; }

        public bool LastLoadRecoveredBackup { get; private set; }

        public ActionResult Save()
        {
            ActionResult captured = Capture(out SaveData data);
            if (captured.Failed)
            {
                return captured;
            }

            string json;
            try
            {
                json = JsonUtility.ToJson(data, true);
            }
            catch (ArgumentException exception)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    $"Could not serialize the save data: {exception.Message}");
            }

            return storage.Write(json);
        }

        public ActionResult Load()
        {
            LastLoadUsedSafeReplan = false;
            LastLoadMigratedLegacySave = false;
            LastLoadRecoveredBackup = false;
            ActionResult read = storage.Read(out string json);
            SaveData data = null;
            bool migratedLegacySave = false;
            ActionResult deserialized = read.Failed
                ? read
                : SaveDataMigration.TryDeserializeAndMigrate(
                    json,
                    out data,
                    out migratedLegacySave);
            if (deserialized.Failed && storage is IRecoverableSaveGameStorage recoverable)
            {
                ActionResult backupRead = recoverable.ReadBackup(out string backupJson);
                if (backupRead.Succeeded)
                {
                    ActionResult backupDeserialized =
                        SaveDataMigration.TryDeserializeAndMigrate(
                            backupJson,
                            out SaveData backupData,
                            out bool backupMigrated);
                    if (backupDeserialized.Succeeded)
                    {
                        deserialized = backupDeserialized;
                        data = backupData;
                        migratedLegacySave = backupMigrated;
                        LastLoadRecoveredBackup = true;
                    }
                }
            }

            if (deserialized.Failed)
            {
                return deserialized;
            }

            ActionResult planned = BuildRestorePlan(data, out RestorePlan restorePlan);
            if (planned.Failed && !LastLoadRecoveredBackup &&
                storage is IRecoverableSaveGameStorage validationRecoverable)
            {
                ActionResult backupRead = validationRecoverable.ReadBackup(
                    out string backupJson);
                if (backupRead.Succeeded &&
                    SaveDataMigration.TryDeserializeAndMigrate(
                        backupJson,
                        out SaveData backupData,
                        out bool backupMigrated).Succeeded)
                {
                    ActionResult backupPlanned = BuildRestorePlan(
                        backupData,
                        out RestorePlan backupRestorePlan);
                    if (backupPlanned.Succeeded)
                    {
                        planned = backupPlanned;
                        data = backupData;
                        restorePlan = backupRestorePlan;
                        migratedLegacySave = backupMigrated;
                        LastLoadRecoveredBackup = true;
                    }
                }
            }

            if (planned.Failed)
            {
                return planned;
            }

            ActionResult applied = ApplyRestorePlan(restorePlan);
            if (applied.Succeeded)
            {
                LastLoadUsedSafeReplan = restorePlan.ReplanStatus == ReplanStatus.Running;
                LastLoadMigratedLegacySave = migratedLegacySave;
            }

            return applied;
        }

        public ActionResult NewDemo(bool deleteSave = true)
        {
            if (!bootstrap.IsInitialized || !executor.IsInitialized || !replanner.IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Save services require initialized game, executor, and replanner components.");
            }

            bootstrap.NotifyAuthoritativeStateResetting();
            ResolveRelationshipGraph()?.Reset();

            ActionResult executorResult = executor.RestorePendingActions(Array.Empty<INpcAction>());
            if (executorResult.Failed)
            {
                return executorResult;
            }

            ActionResult replanResult = replanner.ResetForNewDemo();
            if (replanResult.Failed)
            {
                return replanResult;
            }

            foreach (ResidentId residentId in bootstrap.ResidentRegistry.ResidentIds)
            {
                if (residentId == replanner.ResidentId)
                {
                    continue;
                }

                ActionResult definitionResult = bootstrap.ResidentRegistry.TryGetDefinition(
                    residentId,
                    out ResidentDefinition definition);
                if (definitionResult.Failed)
                {
                    return definitionResult;
                }

                ActionResult replaced = bootstrap.ResidentRegistry.ReplaceRuntimeState(
                    residentId,
                    new ResidentRuntimeState(definition));
                if (replaced.Failed)
                {
                    return replaced;
                }
            }

            ActionResult gameResult = bootstrap.ResetToConfiguredDefaults();
            if (gameResult.Failed)
            {
                return gameResult;
            }

            foreach (TownLifeController life in bootstrap.LifeControllers.Values)
            {
                ActionResult restored = life.RestoreTask(new ResidentTaskSnapshot { residentId = life.ResidentId.Value });
                if (restored.Failed) return restored;
            }

            npcTransform.SetPositionAndRotation(initialNpcPosition, initialNpcRotation);
            LastLoadUsedSafeReplan = false;
            LastLoadMigratedLegacySave = false;
            LastLoadRecoveredBackup = false;
            if (deleteSave)
            {
                ActionResult deleted = storage.Delete();
                if (deleted.Failed)
                {
                    return deleted;
                }
            }

            return ActionResult.Success("New demo created.");
        }

        private ActionResult Capture(out SaveData data)
        {
            data = null;
            if (!bootstrap.IsInitialized || !executor.IsInitialized || !replanner.IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Save services require initialized game, executor, and replanner components.");
            }

            ResidentRuntimeState runtimeState = replanner.RuntimeState;
            if (runtimeState == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "NPC runtime state is not available for saving.");
            }

            ActionResult activeMemoriesResult = TryCaptureResidentMemories(
                runtimeState,
                out MemorySaveData[] activeMemories);
            if (activeMemoriesResult.Failed)
            {
                return activeMemoriesResult;
            }

            var snapshot = new SaveData
            {
                version = SaveData.CurrentVersion,
                savedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                clock = new ClockSaveData
                {
                    elapsedGameSeconds = bootstrap.Clock.ElapsedGameSeconds,
                    timeScale = bootstrap.Clock.TimeScale,
                    isPaused = bootstrap.Clock.IsPaused
                },
                inventory = new InventorySaveData
                {
                    carrotSeeds = bootstrap.Inventory.GetCount(InventoryItem.CarrotSeed),
                    water = bootstrap.Inventory.GetCount(InventoryItem.Water),
                    fertilizer = bootstrap.Inventory.GetCount(InventoryItem.Fertilizer),
                    carrots = bootstrap.Inventory.GetCount(InventoryItem.Carrot),
                    fish = bootstrap.Inventory.GetCount(InventoryItem.Fish),
                    fruit = bootstrap.Inventory.GetCount(InventoryItem.Fruit),
                    compost = bootstrap.Inventory.GetCount(InventoryItem.Compost)
                },
                activities = bootstrap.Activities?.Capture() ?? Array.Empty<ActivityResourceSnapshot>()
            };

            var residentSnapshot = new ResidentSaveData
            {
                residentId = replanner.ResidentId.Value,
                displayName = runtimeState.Definition.DisplayName,
                npc = CaptureNpc(),
                hasFarmGoal = replanner.ActiveGoal != null,
                farmGoal = CaptureGoal(replanner.ActiveGoal),
                executor = new ExecutorSaveData(),
                recentMemories = activeMemories,
                recentReflections = CaptureReflections(runtimeState.RecentReflections),
                runtime = new NpcRuntimeSaveData
                {
                    startedCycleCount = runtimeState.StartedCycleCount,
                    currentCycleNumber = runtimeState.CurrentCycleNumber,
                    completedCycleCount = runtimeState.CompletedCycleCount,
                    reflectedCycleNumbers = CopyIntegers(runtimeState.ReflectedCycleNumbers)
                }
            };

            snapshot.plots = new PlotSaveData[FarmField.PlotCount];
            for (int index = 0; index < FarmField.PlotCount; index++)
            {
                FarmPlot plot = bootstrap.Field.Plots[index];
                snapshot.plots[index] = new PlotSaveData
                {
                    plotNumber = plot.PlotNumber,
                    state = (int)plot.State,
                    hasCrop = plot.Crop.HasValue,
                    crop = plot.Crop.HasValue ? (int)plot.Crop.Value : 0,
                    waterLevel = plot.WaterLevel,
                    isFertilized = plot.IsFertilized,
                    hasWeeds = plot.HasWeeds,
                    hasBeenWeeded = plot.HasBeenWeeded,
                    growthProgress = plot.GrowthProgress
                };
            }

            bootstrap.Simulation.CaptureRuntimeState(
                out double[] waterElapsed,
                out double[] weedElapsed,
                out double[] growthElapsed,
                out bool[] waterHasDecayed);
            snapshot.simulation = new SimulationSaveData
            {
                waterDecayEventCount = bootstrap.Simulation.WaterDecayEventCount,
                weedEventCount = bootstrap.Simulation.WeedEventCount,
                waterElapsed = waterElapsed,
                weedElapsed = weedElapsed,
                growthElapsed = growthElapsed,
                waterHasDecayed = waterHasDecayed
            };

            if (executor.CurrentAction != null)
            {
                ActionResult currentActionResult = CaptureAction(
                    executor.CurrentAction,
                    out NpcActionSaveData currentAction);
                if (currentActionResult.Failed)
                {
                    return currentActionResult;
                }

                residentSnapshot.executor.hasCurrentAction = true;
                residentSnapshot.executor.currentAction = currentAction;
            }

            IReadOnlyList<INpcAction> pendingActions = executor.PendingActions;
            residentSnapshot.executor.remainingActions = new NpcActionSaveData[pendingActions.Count];
            for (int index = 0; index < pendingActions.Count; index++)
            {
                ActionResult actionResult = CaptureAction(
                    pendingActions[index],
                    out NpcActionSaveData actionData);
                if (actionResult.Failed)
                {
                    return actionResult;
                }

                residentSnapshot.executor.remainingActions[index] = actionData;
            }

            ActionResult residentsResult = MergeResidentSnapshots(
                residentSnapshot,
                out ResidentSaveData[] residentSnapshots);
            if (residentsResult.Failed)
            {
                return residentsResult;
            }

            snapshot.residents = residentSnapshots;
            snapshot.relationships = CaptureRelationships();
            var lifeTasks = new List<ResidentTaskSnapshot>();
            foreach (TownLifeController life in bootstrap.LifeControllers.Values)
            {
                if (life.ResidentId.IsValid) lifeTasks.Add(life.CaptureTask());
            }
            snapshot.lifeTasks = lifeTasks.ToArray();

            data = snapshot;
            return ActionResult.Success("Versioned game state captured.");
        }

        private NpcSaveData CaptureNpc()
        {
            Vector3 position = npcTransform.position;
            Quaternion rotation = npcTransform.rotation;
            var harvestedPlotNumbers = new List<int>(replanner.HarvestedPlotNumbers);
            harvestedPlotNumbers.Sort();
            return new NpcSaveData
            {
                positionX = position.x,
                positionY = position.y,
                positionZ = position.z,
                rotationX = rotation.x,
                rotationY = rotation.y,
                rotationZ = rotation.z,
                rotationW = rotation.w,
                hasSceneTransform = true,
                executorStatus = (int)executor.Status,
                replanStatus = (int)replanner.Status,
                currentMood = (int)replanner.CurrentMood,
                currentEmoji = replanner.CurrentEmoji,
                currentExpression = replanner.NpcExpression,
                currentGoalText = replanner.CurrentGoalText,
                currentDecisionReason = replanner.CurrentDecisionReason,
                lastFailureReason = replanner.LastFailureReason,
                activeCycleNumber = replanner.ActiveCycleNumber,
                harvestedPlotNumbers = harvestedPlotNumbers.ToArray()
            };
        }

        private static FarmGoalSaveData CaptureGoal(FarmGoalSpec goal)
        {
            if (goal == null)
            {
                return new FarmGoalSaveData();
            }

            return new FarmGoalSaveData
            {
                goalId = goal.GoalId,
                crop = (int)goal.Crop,
                targetPlotNumbers = CopyIntegers(goal.TargetPlotNumbers),
                requiresSowing = goal.RequiresSowing,
                requiresWatering = goal.RequiresWatering,
                requiresFertilizing = goal.RequiresFertilizing,
                requiresWeeding = goal.RequiresWeeding,
                requiresHarvesting = goal.RequiresHarvesting,
                summary = goal.Summary
            };
        }

        private static MemorySaveData[] CaptureMemories(IReadOnlyList<MemoryEntry> memories)
        {
            var result = new MemorySaveData[memories.Count];
            for (int index = 0; index < memories.Count; index++)
            {
                MemoryEntry memory = memories[index];
                result[index] = new MemorySaveData
                {
                    ownerResidentId = memory.OwnerResidentId.Value,
                    sequence = memory.Sequence,
                    gameSeconds = memory.GameSeconds,
                    kind = (int)memory.Kind,
                    text = memory.Text,
                    importance = memory.Importance,
                    hasSourceEventKind = memory.SourceEventKind.HasValue,
                    sourceEventKind = memory.SourceEventKind.HasValue
                        ? (int)memory.SourceEventKind.Value
                        : 0,
                    sourceKind = (int)memory.SourceKind,
                    sourceEventId = memory.SourceEventId,
                    knowledgeId = memory.KnowledgeId,
                    rootFactId = memory.RootFactId,
                    parentKnowledgeId = memory.ParentKnowledgeId,
                    immediateSourceResidentId = memory.ImmediateSourceResidentId.IsValid
                        ? memory.ImmediateSourceResidentId.Value
                        : string.Empty,
                    tags = CopyStrings(memory.Tags),
                    isShareable = memory.IsShareable
                };
            }

            return result;
        }

        private static ReflectionSaveData[] CaptureReflections(
            IReadOnlyList<NpcReflection> reflections)
        {
            var result = new ReflectionSaveData[reflections.Count];
            for (int index = 0; index < reflections.Count; index++)
            {
                NpcReflection reflection = reflections[index];
                result[index] = new ReflectionSaveData
                {
                    goalId = reflection.GoalId,
                    outcome = (int)reflection.Outcome,
                    mood = (int)reflection.Mood,
                    emoji = reflection.Emoji,
                    text = reflection.Text
                };
            }

            return result;
        }

        private ActionResult MergeResidentSnapshots(
            ResidentSaveData activeResident,
            out ResidentSaveData[] snapshots)
        {
            snapshots = null;
            var merged = new List<ResidentSaveData>();
            foreach (ResidentId residentId in bootstrap.ResidentRegistry.ResidentIds)
            {
                if (residentId == replanner.ResidentId)
                {
                    merged.Add(activeResident);
                    continue;
                }

                ActionResult runtimeResult = bootstrap.ResidentRegistry.TryGetRuntimeState(
                    residentId,
                    out ResidentRuntimeState runtimeState);
                if (runtimeResult.Failed || runtimeState == null)
                {
                    return runtimeResult.Failed
                        ? runtimeResult
                        : ActionResult.Failure(
                            ActionFailureReason.InvalidState,
                            $"Resident '{residentId}' has no runtime state to save.");
                }

                ActionResult captured = CaptureBackgroundResident(
                    runtimeState,
                    out ResidentSaveData backgroundResident);
                if (captured.Failed)
                {
                    return captured;
                }

                merged.Add(backgroundResident);
            }

            merged.Sort((left, right) => string.Compare(
                left.residentId,
                right.residentId,
                StringComparison.Ordinal));
            snapshots = merged.ToArray();
            return ActionResult.Success("All registered resident snapshots captured.");
        }

        private ActionResult CaptureBackgroundResident(
            ResidentRuntimeState runtimeState,
            out ResidentSaveData resident)
        {
            resident = null;
            ActionResult memoriesResult = TryCaptureResidentMemories(
                runtimeState,
                out MemorySaveData[] memories);
            if (memoriesResult.Failed)
            {
                return memoriesResult;
            }

            bool hasFarmGoal = runtimeState.CurrentGoal != null;
            var npc = new NpcSaveData
            {
                executorStatus = (int)NpcExecutionStatus.Completed,
                replanStatus = hasFarmGoal
                    ? (int)ReplanStatus.Running
                    : (int)ReplanStatus.Idle,
                currentMood = (int)NpcMood.Focused,
                activeCycleNumber = hasFarmGoal
                    ? runtimeState.CurrentCycleNumber
                    : 0,
                harvestedPlotNumbers = Array.Empty<int>()
            };
            TownResidentScheduleController controller = FindTownResidentController(
                runtimeState.ResidentId);
            if (controller != null)
            {
                Vector3 position = controller.transform.position;
                Quaternion rotation = controller.transform.rotation;
                npc.hasSceneTransform = true;
                npc.positionX = position.x;
                npc.positionY = position.y;
                npc.positionZ = position.z;
                npc.rotationX = rotation.x;
                npc.rotationY = rotation.y;
                npc.rotationZ = rotation.z;
                npc.rotationW = rotation.w;
            }

            resident = new ResidentSaveData
            {
                residentId = runtimeState.ResidentId.Value,
                displayName = runtimeState.Definition.DisplayName,
                npc = npc,
                hasFarmGoal = hasFarmGoal,
                farmGoal = CaptureGoal(runtimeState.CurrentGoal),
                // Background schedule paths, conversations and network operations are
                // transient. Never carry an executor queue forward from an older load.
                executor = new ExecutorSaveData(),
                recentMemories = memories,
                recentReflections = CaptureReflections(runtimeState.RecentReflections),
                runtime = CaptureRuntime(runtimeState)
            };
            return ActionResult.Success(
                $"Resident '{runtimeState.ResidentId}' snapshot captured.");
        }

        private ActionResult TryCaptureResidentMemories(
            ResidentRuntimeState runtimeState,
            out MemorySaveData[] memories)
        {
            memories = null;
            if (runtimeState == null || runtimeState.Memories == null ||
                runtimeState.Memories.OwnerResidentId != runtimeState.ResidentId)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Resident memory ownership is invalid while capturing a save.");
            }

            var snapshotStore = new MemoryStore(
                runtimeState.ResidentId,
                runtimeState.Memories.Capacity);
            ActionResult restored = snapshotStore.Restore(
                runtimeState.Memories.Entries);
            if (restored.Failed)
            {
                return restored;
            }

            TownSocialCoordinator social =
                bootstrap.GetComponent<TownSocialCoordinator>();
            if (social?.ConversationCoordinator != null)
            {
                ActionResult projected =
                    social.ConversationCoordinator.ProjectPlayedTranscriptForSave(
                        runtimeState.ResidentId,
                        bootstrap.Clock.ElapsedGameSeconds,
                        snapshotStore,
                        out _);
                if (projected.Failed)
                {
                    return projected;
                }
            }

            memories = CaptureMemories(snapshotStore.Entries);
            return ActionResult.Success(
                $"Resident '{runtimeState.ResidentId}' memories captured without transient session state.");
        }

        private static NpcRuntimeSaveData CaptureRuntime(
            ResidentRuntimeState runtimeState)
        {
            return new NpcRuntimeSaveData
            {
                startedCycleCount = runtimeState.StartedCycleCount,
                currentCycleNumber = runtimeState.CurrentCycleNumber,
                completedCycleCount = runtimeState.CompletedCycleCount,
                reflectedCycleNumbers = CopyIntegers(runtimeState.ReflectedCycleNumbers)
            };
        }

        private RelationshipSaveData[] CaptureRelationships()
        {
            SocialGraph graph = ResolveRelationshipGraph() ??
                new SocialGraph(bootstrap.ResidentRegistry.ResidentIds);
            IReadOnlyList<RelationshipStateSnapshot> snapshots = graph.CaptureSnapshots();
            var result = new RelationshipSaveData[snapshots.Count];
            for (int index = 0; index < snapshots.Count; index++)
            {
                RelationshipStateSnapshot snapshot = snapshots[index];
                result[index] = new RelationshipSaveData
                {
                    ownerResidentId = snapshot.OwnerResidentId.Value,
                    otherResidentId = snapshot.OtherResidentId.Value,
                    familiarity = snapshot.Familiarity,
                    trust = snapshot.Trust,
                    relationVersion = snapshot.RelationVersion,
                    lastChangeEventId = snapshot.LastChangeEventId
                };
            }

            return result;
        }

        private SocialGraph ResolveRelationshipGraph()
        {
            if (relationshipGraph != null)
            {
                return relationshipGraph;
            }

            TownSocialCoordinator social = bootstrap.GetComponent<TownSocialCoordinator>();
            return social?.SocialGraph;
        }

        private ActionResult TrySelectResident(
            ResidentSaveData[] residents,
            ResidentId selectedResidentId,
            out ResidentSaveData selectedResident)
        {
            selectedResident = null;
            int expectedResidentCount = bootstrap.ResidentRegistry.Count;
            if (residents == null || residents.Length != expectedResidentCount ||
                residents.Length > MaximumResidentCount)
            {
                return InvalidSave(
                    $"Save must contain exactly {expectedResidentCount} registered residents.");
            }

            var residentIds = new HashSet<ResidentId>();
            foreach (ResidentSaveData resident in residents)
            {
                if (resident == null ||
                    !ResidentId.TryCreate(resident.residentId, out ResidentId residentId) ||
                    !residentIds.Add(residentId) ||
                    bootstrap.ResidentRegistry.TryGetDefinition(
                        residentId,
                        out ResidentDefinition canonicalDefinition).Failed ||
                    !IsBoundedRequired(resident.displayName, 100) ||
                    !string.Equals(
                        resident.displayName,
                        canonicalDefinition.DisplayName,
                        StringComparison.Ordinal) ||
                    resident.npc == null || resident.farmGoal == null ||
                    resident.executor == null || resident.executor.currentAction == null ||
                    resident.executor.remainingActions == null ||
                    resident.recentMemories == null || resident.recentReflections == null ||
                    resident.runtime == null || resident.runtime.reflectedCycleNumbers == null)
                {
                    return InvalidSave(
                        "Saved resident records contain a missing, invalid, or duplicate ResidentId.");
                }

                foreach (MemorySaveData memory in resident.recentMemories)
                {
                    if (memory == null || memory.ownerResidentId != residentId.Value)
                    {
                        return InvalidSave(
                            $"Saved memory owner does not match ResidentId '{residentId}'.");
                    }
                }

                ActionResult residentValidation = ValidateResidentRecord(
                    resident,
                    residentId);
                if (residentValidation.Failed)
                {
                    return residentValidation;
                }

                if (residentId == selectedResidentId)
                {
                    selectedResident = resident;
                }
            }

            foreach (ResidentId registeredResidentId in
                bootstrap.ResidentRegistry.ResidentIds)
            {
                if (!residentIds.Contains(registeredResidentId))
                {
                    return InvalidSave(
                        $"Save is missing registered ResidentId '{registeredResidentId}'.");
                }
            }

            return selectedResident == null
                ? InvalidSave(
                    $"Save does not contain the active ResidentId '{selectedResidentId}'.")
                : ActionResult.Success(
                    $"Selected saved resident '{selectedResidentId}'.");
        }

        private ActionResult ValidateResidentRecord(
            ResidentSaveData resident,
            ResidentId residentId)
        {
            ActionResult npcResult = ValidateNpc(resident.npc, out _, out _);
            if (npcResult.Failed)
            {
                return npcResult;
            }

            ActionResult goalResult = RestoreGoal(resident, out FarmGoalSpec goal);
            if (goalResult.Failed)
            {
                return goalResult;
            }

            ActionResult actionsResult = RestoreActions(
                resident.executor,
                out _,
                out _);
            if (actionsResult.Failed)
            {
                return actionsResult;
            }

            ActionResult runtimeResult = RestoreResidentRuntime(
                resident,
                residentId,
                out ResidentRuntimeState runtimeState);
            if (runtimeResult.Failed)
            {
                return runtimeResult;
            }

            var replanStatus = (ReplanStatus)resident.npc.replanStatus;
            if ((replanStatus == ReplanStatus.Idle && resident.hasFarmGoal) ||
                (replanStatus == ReplanStatus.Running && !resident.hasFarmGoal) ||
                resident.npc.activeCycleNumber < 0 ||
                resident.npc.activeCycleNumber > runtimeState.StartedCycleCount ||
                (residentId == replanner.ResidentId && goal != null &&
                    resident.npc.activeCycleNumber == 0))
            {
                return InvalidSave(
                    $"Saved resident '{residentId}' has inconsistent goal and cycle state.");
            }

            return ActionResult.Success($"Saved resident '{residentId}' validated.");
        }

        private ActionResult BuildRestorePlan(SaveData data, out RestorePlan restorePlan)
        {
            restorePlan = null;
            if (data == null || data.version != SaveData.CurrentVersion)
            {
                return InvalidSave(
                    data == null
                        ? "Save data is missing."
                        : $"Unsupported save version {data.version}; expected {SaveData.CurrentVersion}.");
            }

            if (string.IsNullOrWhiteSpace(data.savedAtUtc) ||
                !DateTime.TryParse(
                    data.savedAtUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out _))
            {
                return InvalidSave("Save timestamp is invalid.");
            }

            if (data.clock == null || data.inventory == null || data.simulation == null ||
                data.plots == null || data.residents == null || data.relationships == null)
            {
                return InvalidSave("Save data is missing a required section.");
            }

            ActionResult selectedResult = TrySelectResident(
                data.residents,
                replanner.ResidentId,
                out ResidentSaveData residentData);
            if (selectedResult.Failed)
            {
                return selectedResult;
            }

            ActionResult relationshipResult = RestoreRelationships(
                data.relationships,
                out List<RelationshipStateSnapshot> relationships);
            if (relationshipResult.Failed)
            {
                return relationshipResult;
            }

            var validationClock = new GameClock();
            ActionResult clockResult = validationClock.Restore(
                data.clock.elapsedGameSeconds,
                data.clock.timeScale,
                data.clock.isPaused);
            if (clockResult.Failed)
            {
                return InvalidSave(clockResult.Message);
            }

            if (data.inventory.carrotSeeds < 0 || data.inventory.water < 0 ||
                data.inventory.fertilizer < 0 || data.inventory.carrots < 0 ||
                data.inventory.fish < 0 || data.inventory.fruit < 0 || data.inventory.compost < 0)
            {
                return InvalidSave("Saved inventory contains a negative count.");
            }

            var validationActivities = new TownActivityResources(validationClock, new FarmInventory(),
                bootstrap.Activities?.Rules);
            ActionResult activitiesValidation = validationActivities.Restore(data.activities);
            if (activitiesValidation.Failed) return InvalidSave(activitiesValidation.Message);
            if (data.lifeTasks != null)
            {
                var taskOwners = new HashSet<ResidentId>();
                foreach (ResidentTaskSnapshot task in data.lifeTasks)
                {
                    if (task == null || !ResidentId.TryCreate(task.residentId, out ResidentId owner) ||
                        !taskOwners.Add(owner) || !bootstrap.ResidentRegistry.ResidentIds.Contains(owner))
                        return InvalidSave("Saved resident activity has a missing, duplicate, or unknown ResidentId.");
                    if (
                        task.completedCycles < 0 || float.IsNaN(task.energy) || float.IsNaN(task.hunger) || float.IsNaN(task.social) ||
                        float.IsInfinity(task.energy) || float.IsInfinity(task.hunger) || float.IsInfinity(task.social) ||
                        task.energy < 0 || task.energy > 100 || task.hunger < 0 || task.hunger > 100 || task.social < 0 || task.social > 100)
                        return InvalidSave($"Saved resident {owner} has invalid cycles/needs: " +
                            $"cycles={task.completedCycles}, energy={task.energy}, hunger={task.hunger}, social={task.social}.");
                    ActionResult taskPresence = task.NormalizeTaskPresence(owner);
                    if (taskPresence.Failed) return InvalidSave($"Saved resident {owner} task is invalid: {taskPresence.Message}");
                    ActionResult activityProgress = task.ValidateActivityProgress();
                    if (activityProgress.Failed) return InvalidSave($"Saved resident {owner} activity progress is invalid: {activityProgress.Message}");
                    if (task.completedPlots != null)
                    {
                        var uniquePlots = new HashSet<int>();
                        foreach (int plot in task.completedPlots)
                            if (plot < 1 || plot > FarmField.PlotCount || !uniquePlots.Add(plot))
                                return InvalidSave("Saved farming task has invalid or duplicate completed plots.");
                    }
                }
            }

            var validationField = new FarmField();
            if (data.plots.Length != FarmField.PlotCount)
            {
                return InvalidSave($"Save must contain exactly {FarmField.PlotCount} plots.");
            }

            for (int index = 0; index < FarmField.PlotCount; index++)
            {
                PlotSaveData plotData = data.plots[index];
                if (plotData == null || plotData.plotNumber != index + 1 ||
                    !Enum.IsDefined(typeof(PlotState), plotData.state) ||
                    (plotData.hasCrop && !Enum.IsDefined(typeof(CropType), plotData.crop)))
                {
                    return InvalidSave($"Saved Plot {index + 1:00} is missing or misnumbered.");
                }

                CropType? crop = plotData.hasCrop ? (CropType?)plotData.crop : null;
                ActionResult plotResult = validationField.Plots[index].RestoreState(
                    (PlotState)plotData.state,
                    crop,
                    plotData.waterLevel,
                    plotData.isFertilized,
                    plotData.hasWeeds,
                    plotData.hasBeenWeeded,
                    plotData.growthProgress);
                if (plotResult.Failed)
                {
                    return InvalidSave(plotResult.Message);
                }
            }

            var validationSimulation = new FarmSimulation(
                validationField,
                validationClock,
                bootstrap.Mode);
            ActionResult simulationResult = validationSimulation.RestoreRuntimeState(
                data.simulation.waterDecayEventCount,
                data.simulation.weedEventCount,
                data.simulation.waterElapsed,
                data.simulation.weedElapsed,
                data.simulation.growthElapsed,
                data.simulation.waterHasDecayed);
            if (simulationResult.Failed)
            {
                return InvalidSave(simulationResult.Message);
            }

            ActionResult npcResult = ValidateNpc(
                residentData.npc,
                out Vector3 position,
                out Quaternion rotation);
            if (npcResult.Failed)
            {
                return npcResult;
            }

            ActionResult goalResult = RestoreGoal(residentData, out FarmGoalSpec goal);
            if (goalResult.Failed)
            {
                return goalResult;
            }

            ActionResult actionResult = RestoreActions(
                residentData.executor,
                out INpcAction currentAction,
                out List<INpcAction> remainingActions);
            if (actionResult.Failed)
            {
                return actionResult;
            }

            ActionResult runtimeResult = RestoreResidentRuntime(
                residentData,
                replanner.ResidentId,
                out ResidentRuntimeState runtimeState);
            if (runtimeResult.Failed)
            {
                return runtimeResult;
            }

            var replanStatus = (ReplanStatus)residentData.npc.replanStatus;
            if ((replanStatus == ReplanStatus.Idle && residentData.hasFarmGoal) ||
                (replanStatus == ReplanStatus.Running && !residentData.hasFarmGoal) ||
                residentData.npc.activeCycleNumber < 0 ||
                residentData.npc.activeCycleNumber > runtimeState.StartedCycleCount ||
                (residentData.hasFarmGoal && residentData.npc.activeCycleNumber == 0))
            {
                return InvalidSave("Saved goal, replanning status, and NPC cycle are inconsistent.");
            }

            // Terminal V2 goals are no longer mutable "current" state. Accept old
            // terminal saves that carried the goal, but normalize them while loading.
            FarmGoalSpec activeGoal = replanStatus == ReplanStatus.Running
                ? goal
                : null;
            restorePlan = new RestorePlan(
                data,
                residentData,
                position,
                rotation,
                activeGoal,
                currentAction,
                remainingActions,
                runtimeState,
                replanStatus,
                relationships);
            return ActionResult.Success("Save data validated without changing live state.");
        }

        private static ActionResult ValidateNpc(
            NpcSaveData npc,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (!IsFinite(npc.positionX) || !IsFinite(npc.positionY) || !IsFinite(npc.positionZ) ||
                !IsFinite(npc.rotationX) || !IsFinite(npc.rotationY) ||
                !IsFinite(npc.rotationZ) || !IsFinite(npc.rotationW) ||
                !Enum.IsDefined(typeof(NpcExecutionStatus), npc.executorStatus) ||
                !Enum.IsDefined(typeof(ReplanStatus), npc.replanStatus) ||
                !Enum.IsDefined(typeof(NpcMood), npc.currentMood) ||
                !IsBounded(npc.currentEmoji, 16) ||
                !IsBounded(npc.currentExpression, MaximumUiTextLength) ||
                !IsBounded(npc.currentGoalText, MaximumUiTextLength) ||
                !IsBounded(npc.currentDecisionReason, MaximumUiTextLength) ||
                !IsBounded(npc.lastFailureReason, MaximumUiTextLength) ||
                npc.harvestedPlotNumbers == null ||
                npc.harvestedPlotNumbers.Length > FarmField.PlotCount)
            {
                return InvalidSave("Saved NPC position, status, or display state is invalid.");
            }

            var harvested = new HashSet<int>();
            foreach (int plotNumber in npc.harvestedPlotNumbers)
            {
                if (plotNumber < 1 || plotNumber > FarmField.PlotCount ||
                    !harvested.Add(plotNumber))
                {
                    return InvalidSave("Saved harvested plot numbers are invalid.");
                }
            }

            var savedRotation = new Quaternion(
                npc.rotationX,
                npc.rotationY,
                npc.rotationZ,
                npc.rotationW);
            float rotationMagnitude = Mathf.Sqrt(
                savedRotation.x * savedRotation.x +
                savedRotation.y * savedRotation.y +
                savedRotation.z * savedRotation.z +
                savedRotation.w * savedRotation.w);
            if (!IsFinite(rotationMagnitude) || rotationMagnitude < 0.0001f)
            {
                return InvalidSave("Saved NPC rotation is invalid.");
            }

            position = new Vector3(npc.positionX, npc.positionY, npc.positionZ);
            rotation = new Quaternion(
                savedRotation.x / rotationMagnitude,
                savedRotation.y / rotationMagnitude,
                savedRotation.z / rotationMagnitude,
                savedRotation.w / rotationMagnitude);
            return ActionResult.Success("Saved NPC transform validated.");
        }

        private static ActionResult RestoreGoal(
            ResidentSaveData data,
            out FarmGoalSpec goal)
        {
            goal = null;
            if (!data.hasFarmGoal)
            {
                return ActionResult.Success("Save has no active farm goal.");
            }

            FarmGoalSaveData goalData = data.farmGoal;
            FarmGoalSpec expected = FarmGoalSpec.CreateFullFieldCarrotLifecycle();
            if (goalData.goalId != expected.GoalId ||
                goalData.crop != (int)expected.Crop ||
                !MatchesIntegers(goalData.targetPlotNumbers, expected.TargetPlotNumbers) ||
                goalData.requiresSowing != expected.RequiresSowing ||
                goalData.requiresWatering != expected.RequiresWatering ||
                goalData.requiresFertilizing != expected.RequiresFertilizing ||
                goalData.requiresWeeding != expected.RequiresWeeding ||
                goalData.requiresHarvesting != expected.RequiresHarvesting ||
                goalData.summary != expected.Summary)
            {
                return InvalidSave("Saved FarmGoalSpec is not the supported full-field carrot goal.");
            }

            goal = expected;
            return ActionResult.Success("Saved FarmGoalSpec restored.");
        }

        private static ActionResult RestoreActions(
            ExecutorSaveData executorData,
            out INpcAction currentAction,
            out List<INpcAction> remainingActions)
        {
            currentAction = null;
            remainingActions = new List<INpcAction>();
            if (executorData.remainingActions == null ||
                executorData.remainingActions.Length > MaximumActionCount ||
                (executorData.hasCurrentAction && executorData.currentAction == null))
            {
                return InvalidSave("Saved NPC action queue is invalid.");
            }

            if (executorData.hasCurrentAction)
            {
                ActionResult currentResult = RestoreAction(
                    executorData.currentAction,
                    out currentAction);
                if (currentResult.Failed)
                {
                    return currentResult;
                }
            }

            foreach (NpcActionSaveData actionData in executorData.remainingActions)
            {
                ActionResult actionResult = RestoreAction(actionData, out INpcAction action);
                if (actionResult.Failed)
                {
                    return actionResult;
                }

                remainingActions.Add(action);
            }

            return ActionResult.Success("Saved NPC actions restored.");
        }

        private static ActionResult RestoreAction(
            NpcActionSaveData data,
            out INpcAction action)
        {
            action = null;
            if (data == null || string.IsNullOrWhiteSpace(data.kind) ||
                !IsFinite(data.durationSeconds) || data.durationSeconds <= 0f ||
                data.durationSeconds > MaximumActionDurationSeconds)
            {
                return InvalidSave("Saved NPC action has invalid fields.");
            }

            bool requiresPlot = data.kind != nameof(WaitAction);
            if ((requiresPlot && (data.plotNumber < 1 || data.plotNumber > FarmField.PlotCount)) ||
                (!requiresPlot && data.plotNumber != 0))
            {
                return InvalidSave("Saved NPC action target is invalid.");
            }

            switch (data.kind)
            {
                case nameof(MoveToPlotAction):
                    action = new MoveToPlotAction(data.plotNumber, data.durationSeconds);
                    break;
                case nameof(SowAction):
                    action = new SowAction(data.plotNumber, data.durationSeconds);
                    break;
                case nameof(WaterAction):
                    action = new WaterAction(data.plotNumber, data.durationSeconds);
                    break;
                case nameof(FertilizeAction):
                    action = new FertilizeAction(data.plotNumber, data.durationSeconds);
                    break;
                case nameof(WeedAction):
                    action = new WeedAction(data.plotNumber, data.durationSeconds);
                    break;
                case nameof(HarvestAction):
                    action = new HarvestAction(data.plotNumber, data.durationSeconds);
                    break;
                case nameof(WaitAction):
                    action = new WaitAction(data.durationSeconds);
                    break;
                default:
                    return InvalidSave($"Unsupported saved NPC action kind '{data.kind}'.");
            }

            return ActionResult.Success("Saved NPC action restored.");
        }

        private ActionResult RestoreResidentRuntime(
            ResidentSaveData data,
            ResidentId residentId,
            out ResidentRuntimeState runtimeState)
        {
            runtimeState = null;
            if (data.recentMemories.Length > MemoryStore.DefaultCapacity ||
                data.recentReflections.Length > ResidentRuntimeState.ReflectionHistoryCapacity ||
                data.runtime.reflectedCycleNumbers == null)
            {
                return InvalidSave("Saved NPC memory or reflection history exceeds its limit.");
            }

            var memories = new List<MemoryEntry>();
            try
            {
                foreach (MemorySaveData memoryData in data.recentMemories)
                {
                    if (memoryData == null)
                    {
                        return InvalidSave("Saved NPC memory entry is invalid.");
                    }

                    ResidentId immediateSourceResidentId = default;
                    bool hasImmediateSource = !string.IsNullOrWhiteSpace(
                        memoryData.immediateSourceResidentId);
                    bool immediateSourceIsValid = !hasImmediateSource ||
                        (ResidentId.TryCreate(
                            memoryData.immediateSourceResidentId,
                            out immediateSourceResidentId) &&
                            immediateSourceResidentId != residentId &&
                            bootstrap.ResidentRegistry.TryGetDefinition(
                                immediateSourceResidentId,
                                out _).Succeeded);
                    var memoryKind = (MemoryEntryKind)memoryData.kind;
                    var sourceKind = (MemorySourceKind)memoryData.sourceKind;
                    if (memoryData.ownerResidentId != residentId.Value ||
                        !Enum.IsDefined(typeof(MemoryEntryKind), memoryData.kind) ||
                        !Enum.IsDefined(typeof(MemorySourceKind), memoryData.sourceKind) ||
                        !IsBoundedRequired(memoryData.text, MemoryStore.MaximumTextLength) ||
                        !IsBounded(memoryData.sourceEventId, MemoryEntry.MaximumIdentifierLength) ||
                        !IsBoundedRequired(
                            memoryData.knowledgeId,
                            MemoryEntry.MaximumIdentifierLength) ||
                        !IsBoundedRequired(
                            memoryData.rootFactId,
                            MemoryEntry.MaximumIdentifierLength) ||
                        !IsBounded(memoryData.parentKnowledgeId, MemoryEntry.MaximumIdentifierLength) ||
                        memoryData.tags == null ||
                        memoryData.tags.Length > MemoryEntry.MaximumTagCount ||
                        !AreValidMemoryTags(memoryData.tags) ||
                        !immediateSourceIsValid ||
                        (memoryData.hasSourceEventKind &&
                            !Enum.IsDefined(typeof(WorldEventKind), memoryData.sourceEventKind)) ||
                        !HasValidSavedMemoryProvenance(
                            memoryData,
                            memoryKind,
                            sourceKind,
                            hasImmediateSource))
                    {
                        return InvalidSave("Saved NPC memory entry is invalid.");
                    }

                    memories.Add(new MemoryEntry(
                        residentId,
                        memoryData.sequence,
                        memoryData.gameSeconds,
                        memoryKind,
                        memoryData.text,
                        memoryData.importance,
                        memoryData.hasSourceEventKind
                            ? (WorldEventKind?)memoryData.sourceEventKind
                            : null,
                        sourceKind,
                        memoryData.sourceEventId,
                        memoryData.knowledgeId,
                        memoryData.rootFactId,
                        memoryData.parentKnowledgeId,
                        immediateSourceResidentId,
                        memoryData.tags,
                        memoryData.isShareable));
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is ArgumentOutOfRangeException)
            {
                return InvalidSave($"Saved NPC memory is invalid: {exception.Message}");
            }

            var memoryStore = new MemoryStore(residentId);
            ActionResult memoryResult = memoryStore.Restore(memories);
            if (memoryResult.Failed)
            {
                return InvalidSave(memoryResult.Message);
            }

            var reflections = new List<NpcReflection>();
            foreach (ReflectionSaveData reflectionData in data.recentReflections)
            {
                if (reflectionData == null ||
                    !Enum.IsDefined(typeof(NpcReflectionOutcome), reflectionData.outcome) ||
                    !Enum.IsDefined(typeof(NpcMood), reflectionData.mood) ||
                    !IsBounded(reflectionData.goalId, 100) ||
                    !IsBounded(reflectionData.emoji, 16) ||
                    !IsBoundedRequired(reflectionData.text, MemoryStore.MaximumTextLength))
                {
                    return InvalidSave("Saved NPC reflection is invalid.");
                }

                reflections.Add(new NpcReflection(
                    reflectionData.goalId,
                    (NpcReflectionOutcome)reflectionData.outcome,
                    (NpcMood)reflectionData.mood,
                    reflectionData.emoji,
                    reflectionData.text));
            }

            ActionResult definitionResult = bootstrap.ResidentRegistry.TryGetDefinition(
                residentId,
                out ResidentDefinition definition);
            if (definitionResult.Failed ||
                !string.Equals(
                    data.displayName,
                    definition.DisplayName,
                    StringComparison.Ordinal))
            {
                return InvalidSave(
                    $"Saved definition for ResidentId '{residentId}' is not canonical.");
            }

            var restoredRuntime = new ResidentRuntimeState(
                definition,
                memoryStore);
            ActionResult runtimeResult = restoredRuntime.Restore(
                data.runtime.startedCycleCount,
                data.runtime.currentCycleNumber,
                data.runtime.completedCycleCount,
                data.runtime.reflectedCycleNumbers,
                reflections);
            if (runtimeResult.Failed)
            {
                return InvalidSave(runtimeResult.Message);
            }

            runtimeState = restoredRuntime;
            return ActionResult.Success("Saved NPC runtime restored.");
        }

        private ActionResult RestoreRelationships(
            RelationshipSaveData[] savedRelationships,
            out List<RelationshipStateSnapshot> snapshots)
        {
            snapshots = new List<RelationshipStateSnapshot>();
            if (savedRelationships == null)
            {
                return InvalidSave("Saved relationships are missing.");
            }

            foreach (RelationshipSaveData relationship in savedRelationships)
            {
                if (relationship == null ||
                    !ResidentId.TryCreate(
                        relationship.ownerResidentId,
                        out ResidentId ownerResidentId) ||
                    !ResidentId.TryCreate(
                        relationship.otherResidentId,
                        out ResidentId otherResidentId) ||
                    !IsBounded(
                        relationship.lastChangeEventId,
                        MemoryEntry.MaximumIdentifierLength))
                {
                    return InvalidSave("A saved directed relationship is invalid.");
                }

                snapshots.Add(new RelationshipStateSnapshot(
                    ownerResidentId,
                    otherResidentId,
                    relationship.familiarity,
                    relationship.trust,
                    relationship.relationVersion,
                    relationship.lastChangeEventId));
            }

            var validationGraph = new SocialGraph(
                bootstrap.ResidentRegistry.ResidentIds);
            ActionResult restored = validationGraph.Restore(snapshots);
            return restored.Failed
                ? InvalidSave(restored.Message)
                : ActionResult.Success("Saved directed relationships validated.");
        }

        private static bool HasValidSavedMemoryProvenance(
            MemorySaveData memory,
            MemoryEntryKind memoryKind,
            MemorySourceKind sourceKind,
            bool hasImmediateSource)
        {
            if ((sourceKind == MemorySourceKind.Conversation) != hasImmediateSource)
            {
                return false;
            }

            if (memoryKind == MemoryEntryKind.Reflection &&
                (sourceKind != MemorySourceKind.Reflection || memory.isShareable))
            {
                return false;
            }

            if (memoryKind == MemoryEntryKind.ConversationSummary &&
                (sourceKind != MemorySourceKind.Conversation || memory.isShareable))
            {
                return false;
            }

            if (sourceKind == MemorySourceKind.Reflection &&
                memoryKind != MemoryEntryKind.Reflection)
            {
                return false;
            }

            if (sourceKind == MemorySourceKind.LegacyImported && memory.isShareable)
            {
                return false;
            }

            bool hasParentKnowledge = !string.IsNullOrWhiteSpace(
                memory.parentKnowledgeId);
            if (hasParentKnowledge && sourceKind != MemorySourceKind.Conversation)
            {
                return false;
            }

            if (sourceKind == MemorySourceKind.Conversation && memory.isShareable &&
                !hasParentKnowledge)
            {
                return false;
            }

            return true;
        }

        private ActionResult ApplyRestorePlan(RestorePlan plan)
        {
            bootstrap.NotifyAuthoritativeStateResetting();
            ActionResult executorReset = executor.RestorePendingActions(Array.Empty<INpcAction>());
            if (executorReset.Failed)
            {
                return executorReset;
            }

            for (int index = 0; index < FarmField.PlotCount; index++)
            {
                PlotSaveData plotData = plan.Data.plots[index];
                ActionResult plotResult = bootstrap.Field.Plots[index].RestoreState(
                    (PlotState)plotData.state,
                    plotData.hasCrop ? (CropType?)plotData.crop : null,
                    plotData.waterLevel,
                    plotData.isFertilized,
                    plotData.hasWeeds,
                    plotData.hasBeenWeeded,
                    plotData.growthProgress);
                if (plotResult.Failed)
                {
                    return plotResult;
                }
            }

            InventorySaveData inventory = plan.Data.inventory;
            ActionResult inventoryResult = bootstrap.Inventory.RestoreCounts(
                inventory.carrotSeeds,
                inventory.water,
                inventory.fertilizer,
                inventory.carrots,
                inventory.fish,
                inventory.fruit,
                inventory.compost);
            if (inventoryResult.Failed)
            {
                return inventoryResult;
            }

            ClockSaveData clock = plan.Data.clock;
            ActionResult clockResult = bootstrap.Clock.Restore(
                clock.elapsedGameSeconds,
                clock.timeScale,
                clock.isPaused);
            if (clockResult.Failed)
            {
                return clockResult;
            }

            bootstrap.ResidentReflections?.Synchronize(clock.elapsedGameSeconds);

            ActionResult activitiesResult = bootstrap.Activities?.Restore(plan.Data.activities) ?? ActionResult.Success();
            if (activitiesResult.Failed) return activitiesResult;

            SimulationSaveData simulation = plan.Data.simulation;
            ActionResult simulationResult = bootstrap.Simulation.RestoreRuntimeState(
                simulation.waterDecayEventCount,
                simulation.weedEventCount,
                simulation.waterElapsed,
                simulation.weedElapsed,
                simulation.growthElapsed,
                simulation.waterHasDecayed);
            if (simulationResult.Failed)
            {
                return simulationResult;
            }

            npcTransform.SetPositionAndRotation(plan.NpcPosition, plan.NpcRotation);
            NpcSaveData npc = plan.ResidentData.npc;
            ActionResult replanResult = replanner.RestoreFromSave(
                plan.Goal,
                plan.ReplanStatus,
                npc.harvestedPlotNumbers,
                plan.RuntimeState,
                npc.activeCycleNumber,
                npc.currentGoalText,
                npc.currentDecisionReason,
                npc.lastFailureReason,
                (NpcMood)npc.currentMood,
                npc.currentEmoji,
                npc.currentExpression);
            if (replanResult.Failed)
            {
                return replanResult;
            }

            ActionResult residentsResult = RestoreBackgroundResidents(plan.Data.residents);
            if (residentsResult.Failed)
            {
                return residentsResult;
            }

            // Release every old resident claim before restoring any new claim;
            // otherwise load order can leave an earlier resident blocked by a
            // later resident's stale task from the world being replaced.
            foreach (TownLifeController life in bootstrap.LifeControllers.Values) life.StopTask();
            foreach (TownLifeController life in bootstrap.LifeControllers.Values)
            {
                ResidentTaskSnapshot task = null;
                foreach (ResidentTaskSnapshot savedTask in plan.Data.lifeTasks ?? Array.Empty<ResidentTaskSnapshot>())
                    if (savedTask.residentId == life.ResidentId.Value) { task = savedTask; break; }
                if (task == null) task = new ResidentTaskSnapshot { residentId = life.ResidentId.Value };
                ActionResult taskResult = life.RestoreTask(task);
                if (taskResult.Failed) return taskResult;
            }

            SocialGraph graph = ResolveRelationshipGraph();
            if (graph != null)
            {
                ActionResult relationshipsResult = graph.Restore(plan.Relationships);
                if (relationshipsResult.Failed)
                {
                    return relationshipsResult;
                }
            }

            if (plan.ReplanStatus == ReplanStatus.Running)
            {
                ActionResult replanned = replanner.TickReplan();
                return replanned.Failed
                    ? replanned
                    : ActionResult.Success("Game loaded; unfinished FarmGoalSpec safely replanned.");
            }

            if (!plan.ResidentData.hasFarmGoal &&
                (NpcExecutionStatus)plan.ResidentData.npc.executorStatus != NpcExecutionStatus.Failed)
            {
                var restartedActions = new List<INpcAction>();
                if (plan.CurrentAction != null)
                {
                    restartedActions.Add(plan.CurrentAction);
                }

                restartedActions.AddRange(plan.RemainingActions);
                ActionResult actionRestore = executor.RestorePendingActions(restartedActions);
                if (actionRestore.Failed)
                {
                    return actionRestore;
                }
            }

            return ActionResult.Success("Game loaded; saved state restored.");
        }

        private ActionResult RestoreBackgroundResidents(
            ResidentSaveData[] savedResidents)
        {
            foreach (ResidentId residentId in bootstrap.ResidentRegistry.ResidentIds)
            {
                if (residentId == replanner.ResidentId)
                {
                    continue;
                }

                ResidentSaveData savedResident = null;
                foreach (ResidentSaveData candidate in savedResidents)
                {
                    if (candidate != null && candidate.residentId == residentId.Value)
                    {
                        savedResident = candidate;
                        break;
                    }
                }

                ResidentRuntimeState runtimeState;
                if (savedResident == null)
                {
                    ActionResult definitionResult = bootstrap.ResidentRegistry.TryGetDefinition(
                        residentId,
                        out ResidentDefinition definition);
                    if (definitionResult.Failed)
                    {
                        return definitionResult;
                    }

                    runtimeState = new ResidentRuntimeState(definition);
                }
                else
                {
                    ActionResult runtimeResult = RestoreResidentRuntime(
                        savedResident,
                        residentId,
                        out runtimeState);
                    if (runtimeResult.Failed)
                    {
                        return runtimeResult;
                    }

                    ActionResult goalResult = RestoreGoal(
                        savedResident,
                        out FarmGoalSpec backgroundGoal);
                    if (goalResult.Failed)
                    {
                        return goalResult;
                    }

                    if (backgroundGoal != null)
                    {
                        ActionResult assigned = runtimeState.SetCurrentGoal(backgroundGoal);
                        if (assigned.Failed)
                        {
                            return assigned;
                        }
                    }
                }

                ActionResult replaced = bootstrap.ResidentRegistry.ReplaceRuntimeState(
                    residentId,
                    runtimeState);
                if (replaced.Failed)
                {
                    return replaced;
                }

                if (savedResident != null && savedResident.npc.hasSceneTransform)
                {
                    TownResidentScheduleController controller =
                        FindTownResidentController(residentId);
                    if (controller != null)
                    {
                        ActionResult transformResult = ValidateNpc(
                            savedResident.npc,
                            out Vector3 position,
                            out Quaternion rotation);
                        if (transformResult.Failed)
                        {
                            return transformResult;
                        }

                        controller.transform.SetPositionAndRotation(position, rotation);
                    }
                }
            }

            return ActionResult.Success("All registered resident runtime states restored.");
        }

        private TownResidentScheduleController FindTownResidentController(
            ResidentId residentId)
        {
            TownScheduleCoordinator schedules =
                bootstrap.GetComponent<TownScheduleCoordinator>();
            if (schedules != null &&
                schedules.TryGetResidentController(
                    residentId,
                    out TownResidentScheduleController scheduledResident).Succeeded)
            {
                return scheduledResident;
            }

            Transform sceneRoot = bootstrap.transform.root;
            TownResidentScheduleController[] controllers =
                sceneRoot.GetComponentsInChildren<TownResidentScheduleController>(true);
            foreach (TownResidentScheduleController controller in controllers)
            {
                if (controller != null && controller.ResidentId == residentId)
                {
                    return controller;
                }
            }

            return null;
        }

        private static ActionResult CaptureAction(
            INpcAction action,
            out NpcActionSaveData data)
        {
            data = null;
            if (action == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Cannot save a null NPC action.");
            }

            string kind;
            if (action is MoveToPlotAction)
            {
                kind = nameof(MoveToPlotAction);
            }
            else if (action is SowAction)
            {
                kind = nameof(SowAction);
            }
            else if (action is WaterAction)
            {
                kind = nameof(WaterAction);
            }
            else if (action is FertilizeAction)
            {
                kind = nameof(FertilizeAction);
            }
            else if (action is WeedAction)
            {
                kind = nameof(WeedAction);
            }
            else if (action is HarvestAction)
            {
                kind = nameof(HarvestAction);
            }
            else if (action is WaitAction)
            {
                kind = nameof(WaitAction);
            }
            else
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    $"NPC action type {action.GetType().Name} cannot be saved.");
            }

            data = new NpcActionSaveData
            {
                kind = kind,
                plotNumber = action.TargetPlotNumber ?? 0,
                durationSeconds = action.DurationSeconds
            };
            return ActionResult.Success("NPC action captured.");
        }

        private static int[] CopyIntegers(IReadOnlyList<int> values)
        {
            var result = new int[values.Count];
            for (int index = 0; index < values.Count; index++)
            {
                result[index] = values[index];
            }

            return result;
        }

        private static string[] CopyStrings(IReadOnlyList<string> values)
        {
            var result = new string[values?.Count ?? 0];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = values[index];
            }

            return result;
        }

        private static bool MatchesIntegers(int[] saved, IReadOnlyList<int> expected)
        {
            if (saved == null || saved.Length != expected.Count)
            {
                return false;
            }

            for (int index = 0; index < saved.Length; index++)
            {
                if (saved[index] != expected[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsBounded(string value, int maximumLength)
        {
            return value != null && value.Length <= maximumLength;
        }

        private static bool IsBoundedRequired(string value, int maximumLength)
        {
            return IsBounded(value, maximumLength) && !string.IsNullOrWhiteSpace(value);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool AreValidMemoryTags(IEnumerable<string> tags)
        {
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string tag in tags)
            {
                string normalized = (tag ?? string.Empty).Trim().ToLowerInvariant();
                if (normalized.Length < 1 ||
                    normalized.Length > MemoryEntry.MaximumTagLength ||
                    !unique.Add(normalized))
                {
                    return false;
                }
            }

            return true;
        }

        private static ActionResult InvalidSave(string message)
        {
            return ActionResult.Failure(
                ActionFailureReason.InvalidResponse,
                message);
        }

        private sealed class RestorePlan
        {
            public RestorePlan(
                SaveData data,
                ResidentSaveData residentData,
                Vector3 npcPosition,
                Quaternion npcRotation,
                FarmGoalSpec goal,
                INpcAction currentAction,
                List<INpcAction> remainingActions,
                ResidentRuntimeState runtimeState,
                ReplanStatus replanStatus,
                List<RelationshipStateSnapshot> relationships)
            {
                Data = data;
                ResidentData = residentData;
                NpcPosition = npcPosition;
                NpcRotation = npcRotation;
                Goal = goal;
                CurrentAction = currentAction;
                RemainingActions = remainingActions;
                RuntimeState = runtimeState;
                ReplanStatus = replanStatus;
                Relationships = relationships;
            }

            public SaveData Data { get; }

            public ResidentSaveData ResidentData { get; }

            public Vector3 NpcPosition { get; }

            public Quaternion NpcRotation { get; }

            public FarmGoalSpec Goal { get; }

            public INpcAction CurrentAction { get; }

            public List<INpcAction> RemainingActions { get; }

            public ResidentRuntimeState RuntimeState { get; }

            public ReplanStatus ReplanStatus { get; }

            public IReadOnlyList<RelationshipStateSnapshot> Relationships { get; }
        }
    }
}
