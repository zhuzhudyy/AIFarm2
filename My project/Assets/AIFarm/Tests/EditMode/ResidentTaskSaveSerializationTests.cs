using System;
using AIFarm.Ai;
using AIFarm.Npc;
using AIFarm.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace AIFarm.Tests.EditMode
{
    public sealed class ResidentTaskSaveSerializationTests
    {
        [Test]
        public void JsonUtilityRoundTrip_IdleResidentRemainsTaskFree()
        {
            var original = new ResidentTaskSnapshot { residentId = ResidentIds.Amu.Value, task = null, hasTask = false };
            string json = JsonUtility.ToJson(original);
            ResidentTaskSnapshot loaded = JsonUtility.FromJson<ResidentTaskSnapshot>(json);
            Assert.That(loaded.NormalizeTaskPresence(ResidentIds.Amu).Succeeded, Is.True, json);
            Assert.That(loaded.task, Is.Null);
            Assert.That(loaded.hasTask, Is.False);
        }

        [Test]
        public void JsonUtilityRoundTrip_CyclicTaskRetainsIdentityAndProgress()
        {
            ResidentTaskSpec task = ResidentTaskSpec.Create(ResidentIds.Yaya, "TendFarm");
            task.repeat = true;
            task.target_plot_numbers = new[] { 1, 2, 3 };
            var original = new ResidentTaskSnapshot { residentId = ResidentIds.Yaya.Value, task = task, hasTask = true,
                completedCycles = 1, completedPlots = new[] { 1 } };
            ResidentTaskSnapshot loaded = JsonUtility.FromJson<ResidentTaskSnapshot>(JsonUtility.ToJson(original));
            Assert.That(loaded.NormalizeTaskPresence(ResidentIds.Yaya).Succeeded, Is.True);
            Assert.That(loaded.hasTask, Is.True);
            Assert.That(loaded.task.task_id, Is.EqualTo(task.task_id));
            Assert.That(loaded.task.repeat, Is.True);
            Assert.That(loaded.completedCycles, Is.EqualTo(1));
            Assert.That(loaded.completedPlots, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void ExplicitTaskPresenceOrNonemptyMalformedTask_CannotBeSilentlyDiscarded()
        {
            var explicitEmpty = new ResidentTaskSnapshot { residentId = ResidentIds.Yaya.Value,
                hasTask = true, task = new ResidentTaskSpec() };
            Assert.That(explicitEmpty.NormalizeTaskPresence(ResidentIds.Yaya).Failed, Is.True);
            var malformed = new ResidentTaskSnapshot { residentId = ResidentIds.Yaya.Value,
                hasTask = false, task = new ResidentTaskSpec { task_type = "TendFarm" } };
            Assert.That(malformed.NormalizeTaskPresence(ResidentIds.Yaya).Failed, Is.True);
        }

        [Test]
        public void LegacySnapshotWithoutActivityCount_DefaultsToZeroAndKeepsItsTask()
        {
            ResidentTaskSpec task = ResidentTaskSpec.Create(ResidentIds.Yaya, "Fish");
            task.quantity = 3;
            string json = JsonUtility.ToJson(new LegacyTaskSnapshot
                { residentId = ResidentIds.Yaya.Value, task = task, hasTask = true });
            Assert.That(json, Does.Not.Contain("completedActivityCount"));
            ResidentTaskSnapshot loaded = JsonUtility.FromJson<ResidentTaskSnapshot>(json);
            Assert.That(loaded.NormalizeTaskPresence(ResidentIds.Yaya).Succeeded, Is.True);
            Assert.That(loaded.ValidateActivityProgress().Succeeded, Is.True);
            Assert.That(loaded.completedActivityCount, Is.Zero);
            Assert.That(loaded.task.task_id, Is.EqualTo(task.task_id));
            Assert.That(loaded.task.quantity, Is.EqualTo(3));
        }

        [TestCase("Fish", 2, 3, false)]
        [TestCase("PickFruit", 98, 99, false)]
        [TestCase("Move", 0, 1, false)]
        [TestCase("Fish", 120, 1, true)]
        public void ActivityProgressRoundTrip_RetainsExactPartialOrRepeatingCount(
            string type, int completed, int quantity, bool repeat)
        {
            ResidentTaskSpec task = ResidentTaskSpec.Create(ResidentIds.Yaya, type);
            task.quantity = quantity;
            task.repeat = repeat;
            var original = new ResidentTaskSnapshot { residentId = ResidentIds.Yaya.Value,
                hasTask = true, task = task, completedActivityCount = completed };
            ResidentTaskSnapshot loaded = JsonUtility.FromJson<ResidentTaskSnapshot>(JsonUtility.ToJson(original));
            Assert.That(loaded.NormalizeTaskPresence(ResidentIds.Yaya).Succeeded, Is.True);
            Assert.That(loaded.ValidateActivityProgress().Succeeded, Is.True);
            Assert.That(loaded.completedActivityCount, Is.EqualTo(completed));
            Assert.That(loaded.task.quantity, Is.EqualTo(quantity));
            Assert.That(loaded.task.repeat, Is.EqualTo(repeat));
        }

        [TestCase("Fish", -1, false)]
        [TestCase("PickFruit", 3, false)]
        [TestCase("Move", 4, false)]
        [TestCase("Fish", -1, true)]
        [TestCase("TendFarm", 1, true)]
        [TestCase(null, 1, false)]
        public void InvalidActivityProgress_IsRejectedWithoutChangingTheCount(
            string type, int completed, bool repeat)
        {
            ResidentTaskSpec task = type == null ? null : ResidentTaskSpec.Create(ResidentIds.Yaya, type);
            if (task != null) { task.quantity = 3; task.repeat = repeat; }
            var snapshot = new ResidentTaskSnapshot { residentId = ResidentIds.Yaya.Value,
                hasTask = task != null, task = task, completedActivityCount = completed };
            Assert.That(snapshot.NormalizeTaskPresence(ResidentIds.Yaya).Succeeded, Is.True);
            Assert.That(snapshot.ValidateActivityProgress().Failed, Is.True);
            Assert.That(snapshot.completedActivityCount, Is.EqualTo(completed), "Invalid progress must not be silently clamped.");
        }

        [Serializable]
        private sealed class LegacyTaskSnapshot
        {
            public string residentId;
            public bool hasTask;
            public ResidentTaskSpec task;
        }
    }
}
