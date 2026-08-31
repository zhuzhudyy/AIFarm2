using System;

namespace AIFarm.Presentation
{
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
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
        public long sequence;
        public double gameSeconds;
        public int kind;
        public string text = string.Empty;
        public int importance;
        public bool hasSourceEventKind;
        public int sourceEventKind;
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
