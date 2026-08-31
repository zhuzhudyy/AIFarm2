using System;
using System.Collections.Generic;
using System.Globalization;
using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Npc;
using AIFarm.Time;
using UnityEngine;

namespace AIFarm.Presentation
{
    public sealed class SaveGameService
    {
        private const int MaximumActionCount = 128;
        private const int MaximumUiTextLength = 512;
        private const float MaximumActionDurationSeconds = 600f;

        private readonly GameBootstrap bootstrap;
        private readonly NpcPlanExecutor executor;
        private readonly ReplanController replanner;
        private readonly Transform npcTransform;
        private readonly ISaveGameStorage storage;
        private readonly Vector3 initialNpcPosition;
        private readonly Quaternion initialNpcRotation;

        public SaveGameService(
            GameBootstrap gameBootstrap,
            NpcPlanExecutor planExecutor,
            ReplanController replanController,
            Transform npc,
            ISaveGameStorage saveStorage)
        {
            bootstrap = gameBootstrap ?? throw new ArgumentNullException(nameof(gameBootstrap));
            executor = planExecutor ?? throw new ArgumentNullException(nameof(planExecutor));
            replanner = replanController ?? throw new ArgumentNullException(nameof(replanController));
            npcTransform = npc ?? throw new ArgumentNullException(nameof(npc));
            storage = saveStorage ?? throw new ArgumentNullException(nameof(saveStorage));
            initialNpcPosition = npcTransform.position;
            initialNpcRotation = npcTransform.rotation;
        }

        public string SavePath => storage.SavePath;

        public bool LastLoadUsedSafeReplan { get; private set; }

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
            ActionResult read = storage.Read(out string json);
            if (read.Failed)
            {
                return read;
            }

            SaveData data;
            try
            {
                data = JsonUtility.FromJson<SaveData>(json);
            }
            catch (ArgumentException exception)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    $"Save JSON is malformed: {exception.Message}");
            }

            ActionResult planned = BuildRestorePlan(data, out RestorePlan restorePlan);
            if (planned.Failed)
            {
                return planned;
            }

            ActionResult applied = ApplyRestorePlan(restorePlan);
            if (applied.Succeeded)
            {
                LastLoadUsedSafeReplan = restorePlan.ReplanStatus == ReplanStatus.Running;
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

            ActionResult gameResult = bootstrap.ResetToConfiguredDefaults();
            if (gameResult.Failed)
            {
                return gameResult;
            }

            npcTransform.SetPositionAndRotation(initialNpcPosition, initialNpcRotation);
            LastLoadUsedSafeReplan = false;
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

            NpcRuntimeState runtimeState = replanner.RuntimeState;
            if (runtimeState == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "NPC runtime state is not available for saving.");
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
                    carrots = bootstrap.Inventory.GetCount(InventoryItem.Carrot)
                },
                npc = CaptureNpc(),
                hasFarmGoal = replanner.ActiveGoal != null,
                farmGoal = CaptureGoal(replanner.ActiveGoal),
                executor = new ExecutorSaveData(),
                recentMemories = CaptureMemories(runtimeState.Memories.Entries),
                recentReflections = CaptureReflections(runtimeState.RecentReflections),
                npcRuntime = new NpcRuntimeSaveData
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

                snapshot.executor.hasCurrentAction = true;
                snapshot.executor.currentAction = currentAction;
            }

            IReadOnlyList<INpcAction> pendingActions = executor.PendingActions;
            snapshot.executor.remainingActions = new NpcActionSaveData[pendingActions.Count];
            for (int index = 0; index < pendingActions.Count; index++)
            {
                ActionResult actionResult = CaptureAction(
                    pendingActions[index],
                    out NpcActionSaveData actionData);
                if (actionResult.Failed)
                {
                    return actionResult;
                }

                snapshot.executor.remainingActions[index] = actionData;
            }

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
                    sequence = memory.Sequence,
                    gameSeconds = memory.GameSeconds,
                    kind = (int)memory.Kind,
                    text = memory.Text,
                    importance = memory.Importance,
                    hasSourceEventKind = memory.SourceEventKind.HasValue,
                    sourceEventKind = memory.SourceEventKind.HasValue
                        ? (int)memory.SourceEventKind.Value
                        : 0
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
                data.npc == null || data.farmGoal == null || data.executor == null ||
                data.npcRuntime == null || data.plots == null ||
                data.recentMemories == null || data.recentReflections == null)
            {
                return InvalidSave("Save data is missing a required section.");
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
                data.inventory.fertilizer < 0 || data.inventory.carrots < 0)
            {
                return InvalidSave("Saved inventory contains a negative count.");
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

            ActionResult npcResult = ValidateNpc(data.npc, out Vector3 position, out Quaternion rotation);
            if (npcResult.Failed)
            {
                return npcResult;
            }

            ActionResult goalResult = RestoreGoal(data, out FarmGoalSpec goal);
            if (goalResult.Failed)
            {
                return goalResult;
            }

            ActionResult actionResult = RestoreActions(
                data.executor,
                out INpcAction currentAction,
                out List<INpcAction> remainingActions);
            if (actionResult.Failed)
            {
                return actionResult;
            }

            ActionResult runtimeResult = RestoreNpcRuntime(data, out NpcRuntimeState runtimeState);
            if (runtimeResult.Failed)
            {
                return runtimeResult;
            }

            var replanStatus = (ReplanStatus)data.npc.replanStatus;
            if ((replanStatus == ReplanStatus.Idle && data.hasFarmGoal) ||
                (replanStatus != ReplanStatus.Idle && !data.hasFarmGoal) ||
                data.npc.activeCycleNumber < 0 ||
                data.npc.activeCycleNumber > runtimeState.StartedCycleCount ||
                (data.hasFarmGoal && data.npc.activeCycleNumber == 0))
            {
                return InvalidSave("Saved goal, replanning status, and NPC cycle are inconsistent.");
            }

            restorePlan = new RestorePlan(
                data,
                position,
                rotation,
                goal,
                currentAction,
                remainingActions,
                runtimeState,
                replanStatus);
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

        private static ActionResult RestoreGoal(SaveData data, out FarmGoalSpec goal)
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

        private static ActionResult RestoreNpcRuntime(
            SaveData data,
            out NpcRuntimeState runtimeState)
        {
            runtimeState = null;
            if (data.recentMemories.Length > MemoryStore.DefaultCapacity ||
                data.recentReflections.Length > NpcRuntimeState.ReflectionHistoryCapacity ||
                data.npcRuntime.reflectedCycleNumbers == null)
            {
                return InvalidSave("Saved NPC memory or reflection history exceeds its limit.");
            }

            var memories = new List<MemoryEntry>();
            try
            {
                foreach (MemorySaveData memoryData in data.recentMemories)
                {
                    if (memoryData == null ||
                        !Enum.IsDefined(typeof(MemoryEntryKind), memoryData.kind) ||
                        !IsBoundedRequired(memoryData.text, MemoryStore.MaximumTextLength) ||
                        (memoryData.hasSourceEventKind &&
                            !Enum.IsDefined(typeof(WorldEventKind), memoryData.sourceEventKind)))
                    {
                        return InvalidSave("Saved NPC memory entry is invalid.");
                    }

                    memories.Add(new MemoryEntry(
                        memoryData.sequence,
                        memoryData.gameSeconds,
                        (MemoryEntryKind)memoryData.kind,
                        memoryData.text,
                        memoryData.importance,
                        memoryData.hasSourceEventKind
                            ? (WorldEventKind?)memoryData.sourceEventKind
                            : null));
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is ArgumentOutOfRangeException)
            {
                return InvalidSave($"Saved NPC memory is invalid: {exception.Message}");
            }

            var memoryStore = new MemoryStore();
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

            var restoredRuntime = new NpcRuntimeState(
                NpcPersonaDefinition.Yaya,
                memoryStore);
            ActionResult runtimeResult = restoredRuntime.Restore(
                data.npcRuntime.startedCycleCount,
                data.npcRuntime.currentCycleNumber,
                data.npcRuntime.completedCycleCount,
                data.npcRuntime.reflectedCycleNumbers,
                reflections);
            if (runtimeResult.Failed)
            {
                return InvalidSave(runtimeResult.Message);
            }

            runtimeState = restoredRuntime;
            return ActionResult.Success("Saved NPC runtime restored.");
        }

        private ActionResult ApplyRestorePlan(RestorePlan plan)
        {
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
                inventory.carrots);
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
            NpcSaveData npc = plan.Data.npc;
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

            if (plan.ReplanStatus == ReplanStatus.Running)
            {
                ActionResult replanned = replanner.TickReplan();
                return replanned.Failed
                    ? replanned
                    : ActionResult.Success("Game loaded; unfinished FarmGoalSpec safely replanned.");
            }

            if (!plan.Data.hasFarmGoal &&
                (NpcExecutionStatus)plan.Data.npc.executorStatus != NpcExecutionStatus.Failed)
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
                Vector3 npcPosition,
                Quaternion npcRotation,
                FarmGoalSpec goal,
                INpcAction currentAction,
                List<INpcAction> remainingActions,
                NpcRuntimeState runtimeState,
                ReplanStatus replanStatus)
            {
                Data = data;
                NpcPosition = npcPosition;
                NpcRotation = npcRotation;
                Goal = goal;
                CurrentAction = currentAction;
                RemainingActions = remainingActions;
                RuntimeState = runtimeState;
                ReplanStatus = replanStatus;
            }

            public SaveData Data { get; }

            public Vector3 NpcPosition { get; }

            public Quaternion NpcRotation { get; }

            public FarmGoalSpec Goal { get; }

            public INpcAction CurrentAction { get; }

            public List<INpcAction> RemainingActions { get; }

            public NpcRuntimeState RuntimeState { get; }

            public ReplanStatus ReplanStatus { get; }
        }
    }
}
