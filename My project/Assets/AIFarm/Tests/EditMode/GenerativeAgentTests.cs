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
    }
}
