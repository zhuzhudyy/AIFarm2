using System;
using AIFarm.Core;
using AIFarm.Npc;
using UnityEngine;

namespace AIFarm.Presentation
{
    [Serializable]
    public sealed class SaveData
    {
        public const int LegacySingleResidentVersion = 1;
        public const int LegacyMultiResidentVersion = 2;
        public const int CurrentVersion = 3;

        public int version = CurrentVersion;
        public string savedAtUtc = string.Empty;
        public ClockSaveData clock = new ClockSaveData();
        public PlotSaveData[] plots = Array.Empty<PlotSaveData>();
        public InventorySaveData inventory = new InventorySaveData();
        public SimulationSaveData simulation = new SimulationSaveData();
        public ResidentSaveData[] residents = Array.Empty<ResidentSaveData>();
    }

    [Serializable]
    public sealed class ResidentSaveData
    {
        public string residentId = string.Empty;
        public string displayName = string.Empty;
        public NpcSaveData npc = new NpcSaveData();
        public bool hasFarmGoal;
        public FarmGoalSaveData farmGoal = new FarmGoalSaveData();
        public ExecutorSaveData executor = new ExecutorSaveData();
        public MemorySaveData[] recentMemories = Array.Empty<MemorySaveData>();
        public ReflectionSaveData[] recentReflections = Array.Empty<ReflectionSaveData>();
        public NpcRuntimeSaveData runtime = new NpcRuntimeSaveData();
    }

    [Serializable]
    public sealed class LegacySaveDataV1
    {
        public int version = SaveData.LegacySingleResidentVersion;
        public string savedAtUtc = string.Empty;
        public ClockSaveData clock = new ClockSaveData();
        public PlotSaveData[] plots = Array.Empty<PlotSaveData>();
        public InventorySaveData inventory = new InventorySaveData();
        public SimulationSaveData simulation = new SimulationSaveData();
        public NpcSaveData npc = new NpcSaveData();
        public bool hasFarmGoal;
        public FarmGoalSaveData farmGoal = new FarmGoalSaveData();
        public ExecutorSaveData executor = new ExecutorSaveData();
        public MemorySaveData[] recentMemories = Array.Empty<MemorySaveData>();
        public ReflectionSaveData[] recentReflections = Array.Empty<ReflectionSaveData>();
        public NpcRuntimeSaveData npcRuntime = new NpcRuntimeSaveData();
    }

    public static class SaveDataMigration
    {
        public static ActionResult TryDeserializeAndMigrate(
            string json,
            out SaveData data,
            out bool migratedLegacySave)
        {
            data = null;
            migratedLegacySave = false;
            if (string.IsNullOrWhiteSpace(json))
            {
                return InvalidSave("Save JSON is empty.");
            }

            try
            {
                SaveVersionHeader header = JsonUtility.FromJson<SaveVersionHeader>(json);
                if (header == null)
                {
                    return InvalidSave("Save JSON does not contain a version header.");
                }

                if (header.version == SaveData.CurrentVersion)
                {
                    data = JsonUtility.FromJson<SaveData>(json);
                    return data == null
                        ? InvalidSave("Save JSON could not be read.")
                        : ActionResult.Success("Current save data read.");
                }

                if (header.version == SaveData.LegacyMultiResidentVersion)
                {
                    data = JsonUtility.FromJson<SaveData>(json);
                    if (data == null)
                    {
                        return InvalidSave("Version 2 save JSON could not be read.");
                    }

                    data.version = SaveData.CurrentVersion;
                    NormalizeLegacyMemoryOwners(data.residents);
                    migratedLegacySave = true;
                    return ActionResult.Success(
                        "Version 2 multi-resident save migrated to the provenance-aware format.");
                }

                if (header.version != SaveData.LegacySingleResidentVersion)
                {
                    return InvalidSave(
                        $"Unsupported save version {header.version}; expected " +
                        $"{SaveData.LegacySingleResidentVersion}, " +
                        $"{SaveData.LegacyMultiResidentVersion}, or {SaveData.CurrentVersion}.");
                }

                LegacySaveDataV1 legacy = JsonUtility.FromJson<LegacySaveDataV1>(json);
                if (legacy == null)
                {
                    return InvalidSave("Legacy save JSON could not be read.");
                }

                data = MigrateLegacySingleResident(legacy);
                migratedLegacySave = true;
                return ActionResult.Success("Legacy single-resident save migrated to resident-001.");
            }
            catch (ArgumentException exception)
            {
                data = null;
                return InvalidSave($"Save JSON is malformed: {exception.Message}");
            }
        }

        public static SaveData MigrateLegacySingleResident(LegacySaveDataV1 legacy)
        {
            if (legacy == null)
            {
                throw new ArgumentNullException(nameof(legacy));
            }

            MemorySaveData[] memories = legacy.recentMemories ?? Array.Empty<MemorySaveData>();
            foreach (MemorySaveData memory in memories)
            {
                if (memory != null)
                {
                    memory.ownerResidentId = ResidentIds.YayaValue;
                    NormalizeLegacyMemory(memory);
                }
            }

            return new SaveData
            {
                version = SaveData.CurrentVersion,
                savedAtUtc = legacy.savedAtUtc,
                clock = legacy.clock,
                plots = legacy.plots,
                inventory = legacy.inventory,
                simulation = legacy.simulation,
                residents = new[]
                {
                    new ResidentSaveData
                    {
                        residentId = ResidentIds.YayaValue,
                        displayName = ResidentDefinition.Yaya.DisplayName,
                        npc = legacy.npc,
                        hasFarmGoal = legacy.hasFarmGoal,
                        farmGoal = legacy.farmGoal,
                        executor = legacy.executor,
                        recentMemories = memories,
                        recentReflections = legacy.recentReflections,
                        runtime = legacy.npcRuntime
                    }
                }
            };
        }

        private static void NormalizeLegacyMemoryOwners(ResidentSaveData[] residents)
        {
            if (residents == null)
            {
                return;
            }

            foreach (ResidentSaveData resident in residents)
            {
                if (resident == null || resident.recentMemories == null)
                {
                    continue;
                }

                foreach (MemorySaveData memory in resident.recentMemories)
                {
                    if (memory == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(memory.ownerResidentId))
                    {
                        memory.ownerResidentId = resident.residentId;
                    }

                    NormalizeLegacyMemory(memory);
                }
            }
        }

        private static void NormalizeLegacyMemory(MemorySaveData memory)
        {
            bool isReflection = memory.kind == (int)MemoryEntryKind.Reflection;
            if (memory.kind == (int)MemoryEntryKind.ConversationSummary)
            {
                // V2 summaries did not contain a verifiable immediate source. Keep
                // their text as a private imported observation, never as a fact.
                memory.kind = (int)MemoryEntryKind.Observation;
            }

            memory.hasSourceEventKind = false;
            memory.sourceEventKind = 0;
            memory.sourceKind = isReflection
                ? (int)MemorySourceKind.Reflection
                : (int)MemorySourceKind.LegacyImported;
            memory.sourceEventId = string.Empty;
            memory.knowledgeId = CreateLegacyKnowledgeId(
                memory.ownerResidentId,
                memory.sequence);
            memory.rootFactId = memory.knowledgeId;
            memory.parentKnowledgeId = string.Empty;
            memory.immediateSourceResidentId = string.Empty;
            memory.tags = isReflection
                ? new[] { "reflection", "legacy-imported" }
                : new[] { "legacy-imported" };
            memory.isShareable = false;
        }

        private static string CreateLegacyKnowledgeId(string ownerResidentId, long sequence)
        {
            string owner = string.IsNullOrWhiteSpace(ownerResidentId)
                ? ResidentIds.YayaValue
                : ownerResidentId.Trim();
            return $"knowledge:{owner}:{sequence:D12}";
        }

        private static ActionResult InvalidSave(string message)
        {
            return ActionResult.Failure(ActionFailureReason.InvalidResponse, message);
        }

        [Serializable]
        private sealed class SaveVersionHeader
        {
            public int version;
        }
    }

    [Serializable]
    public sealed class ClockSaveData
    {
        public double elapsedGameSeconds;
        public double timeScale = 1d;
        public bool isPaused;
    }

    [Serializable]
    public sealed class PlotSaveData
    {
        public int plotNumber;
        public int state;
        public bool hasCrop;
        public int crop;
        public int waterLevel;
        public bool isFertilized;
        public bool hasWeeds;
        public bool hasBeenWeeded;
        public int growthProgress;
    }

    [Serializable]
    public sealed class InventorySaveData
    {
        public int carrotSeeds;
        public int water;
        public int fertilizer;
        public int carrots;
    }

    [Serializable]
    public sealed class SimulationSaveData
    {
        public int waterDecayEventCount;
        public int weedEventCount;
        public double[] waterElapsed = Array.Empty<double>();
        public double[] weedElapsed = Array.Empty<double>();
        public double[] growthElapsed = Array.Empty<double>();
        public bool[] waterHasDecayed = Array.Empty<bool>();
    }

    [Serializable]
    public sealed class NpcSaveData
    {
        public float positionX;
        public float positionY;
        public float positionZ;
        public float rotationX;
        public float rotationY;
        public float rotationZ;
        public float rotationW = 1f;
        public int executorStatus;
        public int replanStatus;
        public int currentMood;
        public string currentEmoji = string.Empty;
        public string currentExpression = string.Empty;
        public string currentGoalText = string.Empty;
        public string currentDecisionReason = string.Empty;
        public string lastFailureReason = string.Empty;
        public int activeCycleNumber;
        public int[] harvestedPlotNumbers = Array.Empty<int>();
    }

    [Serializable]
    public sealed class FarmGoalSaveData
    {
        public string goalId = string.Empty;
        public int crop;
        public int[] targetPlotNumbers = Array.Empty<int>();
        public bool requiresSowing;
        public bool requiresWatering;
        public bool requiresFertilizing;
        public bool requiresWeeding;
        public bool requiresHarvesting;
        public string summary = string.Empty;
    }

    [Serializable]
    public sealed class ExecutorSaveData
    {
        public bool hasCurrentAction;
        public NpcActionSaveData currentAction = new NpcActionSaveData();
        public NpcActionSaveData[] remainingActions = Array.Empty<NpcActionSaveData>();
    }

    [Serializable]
    public sealed class NpcActionSaveData
    {
        public string kind = string.Empty;
        public int plotNumber;
        public float durationSeconds;
    }

    [Serializable]
    public sealed class MemorySaveData
    {
        public string ownerResidentId = string.Empty;
        public long sequence;
        public double gameSeconds;
        public int kind;
        public string text = string.Empty;
        public int importance;
        public bool hasSourceEventKind;
        public int sourceEventKind;
        public int sourceKind;
        public string sourceEventId = string.Empty;
        public string knowledgeId = string.Empty;
        public string rootFactId = string.Empty;
        public string parentKnowledgeId = string.Empty;
        public string immediateSourceResidentId = string.Empty;
        public string[] tags = Array.Empty<string>();
        public bool isShareable;
    }

    [Serializable]
    public sealed class ReflectionSaveData
    {
        public string goalId = string.Empty;
        public int outcome;
        public int mood;
        public string emoji = string.Empty;
        public string text = string.Empty;
    }

    [Serializable]
    public sealed class NpcRuntimeSaveData
    {
        public int startedCycleCount;
        public int currentCycleNumber;
        public int completedCycleCount;
        public int[] reflectedCycleNumbers = Array.Empty<int>();
    }
}
