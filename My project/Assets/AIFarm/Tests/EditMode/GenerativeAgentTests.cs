using System.Collections.Generic;
using System.Linq;
using AIFarm.Core;
using AIFarm.Npc;
using NUnit.Framework;

namespace AIFarm.Tests.EditMode
{
    public sealed class GenerativeAgentTests
    {
        [Test]
        public void YayaPersona_IsFixedAndComplete()
        {
            NpcPersonaDefinition persona = NpcPersonaDefinition.Yaya;

            Assert.That(persona.Name, Is.EqualTo("芽芽"));
            Assert.That(persona.Role, Is.EqualTo("农场助手"));
            Assert.That(persona.PersonalityTraits, Is.EqualTo(
                new[] { "认真", "乐观", "偶尔有一点小抱怨" }));
            Assert.That(persona.SpeakingStyle, Does.Contain("Emoji"));
            Assert.That(persona.WorkHabit, Does.Contain("影响作物生长"));
            Assert.That(persona.Preference, Does.Contain("整齐"));
            Assert.That(persona.Dislike, Does.Contain("杂草").And.Contain("浪费"));
        }

        [Test]
        public void ObservationService_ConvertsWorldEventsIntoBoundedMemories()
        {
            var events = new WorldEventLog();
            Assert.That(
                events.Record(
                    10d,
                    WorldEventKind.WeedsAppeared,
                    "03 号地出现杂草。",
                    plotNumber: 3).Succeeded,
                Is.True);
            Assert.That(
                events.Record(
                    12d,
                    WorldEventKind.ActionCompleted,
                    "完成动作：Weed Plot 03。",
                    plotNumber: 3).Succeeded,
                Is.True);

            var memories = new MemoryStore();
            var observations = new ObservationService();
            ActionResult captured = observations.CaptureNewObservations(
                events,
                memories,
                out int capturedCount);

            Assert.That(captured.Succeeded, Is.True, captured.Message);
            Assert.That(capturedCount, Is.EqualTo(2));
            Assert.That(memories.Entries, Has.Count.EqualTo(2));
            Assert.That(memories.Entries[0].Text, Does.Contain("03 号地").And.Contain("杂草"));
            Assert.That(memories.Entries[0].Importance, Is.EqualTo(9));
            Assert.That(memories.Entries[0].IsHighImportance, Is.True);
            Assert.That(memories.Entries[1].Text, Does.Contain("完成").And.Contain("清理"));

            Assert.That(
                observations.CaptureNewObservations(events, memories, out capturedCount).Succeeded,
                Is.True);
            Assert.That(capturedCount, Is.Zero);
            Assert.That(memories.Entries, Has.Count.EqualTo(2));
        }

        [Test]
        public void MemoryStore_IsBoundedAndRetainsImportantObservations()
        {
            var memories = new MemoryStore(capacity: 3);
            Assert.That(
                memories.AddObservation(
                    1d,
                    "我发现杂草。",
                    9,
                    WorldEventKind.WeedsAppeared,
                    out MemoryEntry important).Succeeded,
                Is.True);
            for (int index = 0; index < 3; index++)
            {
                Assert.That(
                    memories.AddObservation(
                        index + 2d,
                        $"普通观察 {index}",
                        2,
                        WorldEventKind.ActionStarted,
                        out _).Succeeded,
                    Is.True);
            }

            Assert.That(memories.Entries, Has.Count.EqualTo(3));
            Assert.That(memories.Entries, Does.Contain(important));
            Assert.That(
                memories.GetImportantRecentObservations(3).Single(),
                Is.SameAs(important));
        }

        [Test]
        public void ReflectionService_UsesImportantMemoryAndPreviousReflectionOncePerCycle()
        {
            var state = new NpcRuntimeState();
            Assert.That(
                state.Memories.AddObservation(
                    30d,
                    "我发现 05 号地长出了杂草，得尽快清理。",
                    9,
                    WorldEventKind.WeedsAppeared,
                    out _).Succeeded,
                Is.True);
            var service = new ReflectionService(state);
            FarmGoalSpec goal = FarmGoalSpec.CreateFullFieldCarrotLifecycle();

            string expressionContext = service.BuildExpressionContext("准备继续工作");
            Assert.That(expressionContext, Does.Contain("芽芽").And.Contain("杂草"));
            Assert.That(
                expressionContext.Length,
                Is.LessThanOrEqualTo(ReflectionService.MaximumExpressionContextLength));

            int firstCycle = state.BeginCycle();
            Assert.That(
                service.CreateLocalReflection(
                    goal,
                    NpcReflectionOutcome.Completed,
                    "第一轮完成",
                    out NpcReflection firstReflection).Succeeded,
                Is.True);
            Assert.That(firstReflection.Text, Does.Contain("杂草"));
            Assert.That(
                state.RecordCompletedCycleReflection(firstCycle, firstReflection, 40d).Succeeded,
                Is.True);
            Assert.That(
                state.RecordCompletedCycleReflection(firstCycle, firstReflection, 41d).Failed,
                Is.True);

            int secondCycle = state.BeginCycle();
            string secondContext = service.BuildReflectionContext("第二轮完成");
            Assert.That(secondContext, Does.Contain("上次反思").And.Contain(firstReflection.Text));
            Assert.That(
                secondContext.Length,
                Is.LessThanOrEqualTo(ReflectionService.MaximumReflectionContextLength));
            Assert.That(
                service.CreateLocalReflection(
                    goal,
                    NpcReflectionOutcome.Completed,
                    secondContext,
                    out NpcReflection secondReflection).Succeeded,
                Is.True);
            Assert.That(secondReflection.Text, Does.Contain("上次"));
            Assert.That(
                state.RecordCompletedCycleReflection(secondCycle, secondReflection, 80d).Succeeded,
                Is.True);
            Assert.That(state.CompletedCycleCount, Is.EqualTo(2));
            Assert.That(state.RecentReflections, Has.Count.EqualTo(2));
            Assert.That(state.Memories.GetRecentReflections(3), Has.Count.EqualTo(2));
        }

        [Test]
        public void ReflectionPolicy_AllowsOnlyMajorGoalAndDayEndTriggers()
        {
            Assert.That(
                ReflectionService.IsEligibleTrigger(WorldEventKind.GoalCompleted),
                Is.True);
            Assert.That(
                ReflectionService.IsEligibleTrigger(WorldEventKind.DayEnded),
                Is.True);
            Assert.That(
                ReflectionService.IsEligibleTrigger(WorldEventKind.ActionCompleted),
                Is.False);
            Assert.That(
                ReflectionService.IsEligibleTrigger(WorldEventKind.ConversationCompleted),
                Is.False);
        }

        [Test]
        public void DayEndReflection_RecordsExactlyOneOwnerScopedLocalFallbackPerResident()
        {
            var registry = new ResidentRegistry();
            foreach (ResidentDefinition definition in ResidentDefinition.TownResidents)
            {
                var runtime = new ResidentRuntimeState(definition);
                Assert.That(registry.Register(definition, runtime).Succeeded, Is.True);
            }

            var eventLog = new WorldEventLog(16);
            var coordinator = new ResidentReflectionCoordinator(registry, 86399d);

            Assert.That(
                coordinator.TickDayBoundaries(86399.5d, eventLog, out int before).Succeeded,
                Is.True);
            Assert.That(before, Is.Zero);
            Assert.That(
                coordinator.TickDayBoundaries(86400d, eventLog, out int reflected).Succeeded,
                Is.True);
            Assert.That(reflected, Is.EqualTo(4));
            Assert.That(eventLog.Entries, Has.Count.EqualTo(1));
            Assert.That(eventLog.Entries[0].Kind, Is.EqualTo(WorldEventKind.DayEnded));

            foreach (ResidentId residentId in ResidentIds.TownResidents)
            {
                Assert.That(
                    registry.TryGetRuntimeState(
                        residentId,
                        out ResidentRuntimeState runtime).Succeeded,
                    Is.True);
                Assert.That(
                    runtime.Memories.GetRecentReflections(
                        residentId,
                        3,
                        out IReadOnlyList<MemoryEntry> reflections).Succeeded,
                    Is.True);
                Assert.That(reflections, Has.Count.EqualTo(1));
                Assert.That(reflections[0].OwnerResidentId, Is.EqualTo(residentId));
                Assert.That(reflections[0].SourceKind, Is.EqualTo(MemorySourceKind.Reflection));
                Assert.That(
                    reflections[0].SourceEventKind,
                    Is.EqualTo(WorldEventKind.DayEnded));
                Assert.That(reflections[0].Tags, Does.Contain("day-end").And.Contain("day-1"));
                Assert.That(reflections[0].IsShareable, Is.False);
            }

            Assert.That(
                coordinator.TickDayBoundaries(86401d, eventLog, out int repeated).Succeeded,
                Is.True);
            Assert.That(repeated, Is.Zero);
        }

        [Test]
        public void DayEndReflection_SynchronizeAfterTimeReset_AllowsTheNewDayOne()
        {
            var registry = new ResidentRegistry();
            foreach (ResidentDefinition definition in ResidentDefinition.TownResidents)
            {
                Assert.That(
                    registry.Register(
                        definition,
                        new ResidentRuntimeState(definition)).Succeeded,
                    Is.True);
            }

            var eventLog = new WorldEventLog(16);
            var coordinator = new ResidentReflectionCoordinator(registry, 0d);
            Assert.That(
                coordinator.TickDayBoundaries(
                    ResidentReflectionCoordinator.GameSecondsPerDay,
                    eventLog,
                    out int firstRun).Succeeded,
                Is.True);
            Assert.That(firstRun, Is.EqualTo(4));

            foreach (ResidentDefinition definition in ResidentDefinition.TownResidents)
            {
                Assert.That(
                    registry.ReplaceRuntimeState(
                        definition.ResidentId,
                        new ResidentRuntimeState(definition)).Succeeded,
                    Is.True);
            }

            coordinator.Synchronize(0d);
            Assert.That(
                coordinator.TickDayBoundaries(
                    ResidentReflectionCoordinator.GameSecondsPerDay,
                    eventLog,
                    out int secondRun).Succeeded,
                Is.True);
            Assert.That(secondRun, Is.EqualTo(4));
            foreach (ResidentId residentId in ResidentIds.TownResidents)
            {
                Assert.That(
                    registry.TryGetRuntimeState(
                        residentId,
                        out ResidentRuntimeState runtime).Succeeded,
                    Is.True);
                Assert.That(
                    runtime.Memories.GetRecentReflections(
                        residentId,
                        3,
                        out IReadOnlyList<MemoryEntry> reflections).Succeeded,
                    Is.True);
                Assert.That(reflections, Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void DayEndReflection_UsesOnlyCompletedDayFacts_AndNeverReflectsOnReflection()
        {
            var runtime = new ResidentRuntimeState(ResidentDefinition.Yaya);
            var service = new ReflectionService(runtime);
            Assert.That(
                runtime.Memories.AddObservation(
                    runtime.ResidentId,
                    100d,
                    "第一天的重要事件。",
                    9,
                    WorldEventKind.GoalCompleted,
                    out _).Succeeded,
                Is.True);
            Assert.That(
                service.CreateAndStoreLocalDayEndReflection(
                    1,
                    ResidentReflectionCoordinator.GameSecondsPerDay,
                    out MemoryEntry firstReflection).Succeeded,
                Is.True);
            Assert.That(firstReflection.Text, Does.Contain("第一天的重要事件"));
            Assert.That(
                runtime.Memories.AddObservation(
                    runtime.ResidentId,
                    ResidentReflectionCoordinator.GameSecondsPerDay + 100d,
                    "第二天的新事件。",
                    8,
                    WorldEventKind.CropMatured,
                    out _).Succeeded,
                Is.True);

            Assert.That(
                service.CreateAndStoreLocalDayEndReflection(
                    2,
                    ResidentReflectionCoordinator.GameSecondsPerDay * 2d,
                    out MemoryEntry secondReflection).Succeeded,
                Is.True);

            Assert.That(secondReflection.Text, Does.Contain("第二天的新事件"));
            Assert.That(secondReflection.Text, Does.Not.Contain("第一天的重要事件"));
            Assert.That(secondReflection.ParentKnowledgeId, Is.Empty);
            Assert.That(secondReflection.IsShareable, Is.False);
        }

        [Test]
        public void DayEndReflection_DirectClockRollbackResetsResidentGuards()
        {
            var registry = new ResidentRegistry();
            foreach (ResidentDefinition definition in ResidentDefinition.TownResidents)
            {
                Assert.That(
                    registry.Register(
                        definition,
                        new ResidentRuntimeState(definition)).Succeeded,
                    Is.True);
            }

            var eventLog = new WorldEventLog(16);
            var coordinator = new ResidentReflectionCoordinator(registry, 0d);
            Assert.That(
                coordinator.TickDayBoundaries(
                    ResidentReflectionCoordinator.GameSecondsPerDay * 2d,
                    eventLog,
                    out int initial).Succeeded,
                Is.True);
            Assert.That(initial, Is.EqualTo(8));
            foreach (ResidentDefinition definition in ResidentDefinition.TownResidents)
            {
                Assert.That(
                    registry.ReplaceRuntimeState(
                        definition.ResidentId,
                        new ResidentRuntimeState(definition)).Succeeded,
                    Is.True);
            }

            Assert.That(
                coordinator.TickDayBoundaries(0d, eventLog, out int rollback).Succeeded,
                Is.True);
            Assert.That(rollback, Is.Zero);
            Assert.That(
                coordinator.TickDayBoundaries(
                    ResidentReflectionCoordinator.GameSecondsPerDay,
                    eventLog,
                    out int rerun).Succeeded,
                Is.True);
            Assert.That(rerun, Is.EqualTo(4));
        }
    }
}
