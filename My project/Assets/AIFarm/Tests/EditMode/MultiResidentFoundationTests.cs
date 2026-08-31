using System;
using AIFarm.Npc;
using AIFarm.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace AIFarm.Tests.EditMode
{
    public sealed class MultiResidentFoundationTests
    {
        [Test]
        public void ResidentRuntimeStates_DoNotShareCurrentGoal()
        {
            ResidentRuntimeState first = CreateRuntime("resident-test-a", "测试甲");
            ResidentRuntimeState second = CreateRuntime("resident-test-b", "测试乙");
            FarmGoalSpec goal = FarmGoalSpec.CreateFullFieldCarrotLifecycle();

            Assert.That(first.SetCurrentGoal(goal).Succeeded, Is.True);

            Assert.That(first.CurrentGoal, Is.SameAs(goal));
            Assert.That(second.CurrentGoal, Is.Null);
            Assert.That(first.GoalRevision, Is.EqualTo(1));
            Assert.That(second.GoalRevision, Is.Zero);

            Assert.That(second.SetCurrentGoal(goal).Succeeded, Is.True);
            Assert.That(first.ClearCurrentGoal().Succeeded, Is.True);
            Assert.That(first.CurrentGoal, Is.Null);
            Assert.That(second.CurrentGoal, Is.SameAs(goal));
        }

        [Test]
        public void MemoryStores_AreStrictlyPartitionedByResidentId()
        {
            var firstId = new ResidentId("resident-test-a");
            var secondId = new ResidentId("resident-test-b");
            var first = new MemoryStore(firstId);
            var second = new MemoryStore(secondId);

            Assert.That(
                first.AddObservation(
                    1d,
                    "ONLY-A",
                    5,
                    AIFarm.Core.WorldEventKind.System,
                    out MemoryEntry firstEntry).Succeeded,
                Is.True);
            Assert.That(
                second.AddObservation(
                    2d,
                    "ONLY-B",
                    5,
                    AIFarm.Core.WorldEventKind.System,
                    out MemoryEntry secondEntry).Succeeded,
                Is.True);

            Assert.That(firstEntry.OwnerResidentId, Is.EqualTo(firstId));
            Assert.That(secondEntry.OwnerResidentId, Is.EqualTo(secondId));
            Assert.That(first.GetRecent(10)[0].Text, Is.EqualTo("ONLY-A"));
            Assert.That(second.GetRecent(10)[0].Text, Is.EqualTo("ONLY-B"));
            Assert.That(
                first.GetRecent(secondId, 10, out var crossResidentRead).Failed,
                Is.True);
            Assert.That(crossResidentRead, Is.Empty);
            Assert.That(first.Restore(new[] { secondEntry }).Failed, Is.True);
            Assert.That(first.GetRecent(10)[0].Text, Is.EqualTo("ONLY-A"));
        }

        [Test]
        public void ResidentRegistry_RejectsDuplicateResidentId()
        {
            var registry = new ResidentRegistry();
            ResidentRuntimeState first = CreateRuntime("resident-test-a", "测试甲");
            ResidentRuntimeState duplicate = CreateRuntime("resident-test-a", "另一个名字");

            Assert.That(registry.Register(first.Definition, first).Succeeded, Is.True);
            Assert.That(registry.Register(duplicate.Definition, duplicate).Failed, Is.True);
            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(
                registry.TryGetRuntimeState(first.ResidentId, out ResidentRuntimeState resolved).Succeeded,
                Is.True);
            Assert.That(resolved, Is.SameAs(first));
        }

        [Test]
        public void SaveData_RoundTripsMultipleResidentRecords()
        {
            var data = new SaveData
            {
                residents = new[]
                {
                    CreateResidentSaveData("resident-test-a", "测试甲", "MEMORY-A"),
                    CreateResidentSaveData("resident-test-b", "测试乙", "MEMORY-B")
                }
            };

            string json = JsonUtility.ToJson(data);
            AIFarm.Core.ActionResult result = SaveDataMigration.TryDeserializeAndMigrate(
                json,
                out SaveData restored,
                out bool migrated);

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(migrated, Is.False);
            Assert.That(restored.version, Is.EqualTo(SaveData.CurrentVersion));
            Assert.That(restored.residents, Has.Length.EqualTo(2));
            Assert.That(restored.residents[0].residentId, Is.EqualTo("resident-test-a"));
            Assert.That(restored.residents[0].recentMemories[0].text, Is.EqualTo("MEMORY-A"));
            Assert.That(restored.residents[1].residentId, Is.EqualTo("resident-test-b"));
            Assert.That(restored.residents[1].recentMemories[0].text, Is.EqualTo("MEMORY-B"));
        }

        [Test]
        public void LegacySingleResidentSave_MigratesToYayaResidentRecord()
        {
            var legacy = new LegacySaveDataV1
            {
                savedAtUtc = "2026-08-31T00:00:00.0000000Z",
                recentMemories = new[]
                {
                    new MemorySaveData
                    {
                        sequence = 1,
                        gameSeconds = 10d,
                        kind = 0,
                        text = "旧存档记忆",
                        importance = 5
                    }
                }
            };

            AIFarm.Core.ActionResult result = SaveDataMigration.TryDeserializeAndMigrate(
                JsonUtility.ToJson(legacy),
                out SaveData migratedData,
                out bool migrated);

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(migrated, Is.True);
            Assert.That(migratedData.version, Is.EqualTo(SaveData.CurrentVersion));
            Assert.That(migratedData.residents, Has.Length.EqualTo(1));
            Assert.That(
                migratedData.residents[0].residentId,
                Is.EqualTo(ResidentIds.YayaValue));
            Assert.That(
                migratedData.residents[0].recentMemories[0].ownerResidentId,
                Is.EqualTo(ResidentIds.YayaValue));
        }

        private static ResidentRuntimeState CreateRuntime(
            string residentId,
            string displayName)
        {
            var definition = new ResidentDefinition(
                new ResidentId(residentId),
                displayName,
                NpcPersonaDefinition.Yaya);
            return new ResidentRuntimeState(definition);
        }

        private static ResidentSaveData CreateResidentSaveData(
            string residentId,
            string displayName,
            string memoryText)
        {
            return new ResidentSaveData
            {
                residentId = residentId,
                displayName = displayName,
                recentMemories = new[]
                {
                    new MemorySaveData
                    {
                        ownerResidentId = residentId,
                        sequence = 1,
                        gameSeconds = 1d,
                        kind = 0,
                        text = memoryText,
                        importance = 5
                    }
                }
            };
        }
    }
}
