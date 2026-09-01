using System.Linq;
using AIFarm.Core;
using AIFarm.Npc;
using NUnit.Framework;

namespace AIFarm.Tests.EditMode
{
    public sealed class MemoryVisibilityTests
    {
        [Test]
        public void ObservationProjection_DeliversOnlyVisibleEvents_WithProvenance()
        {
            var events = new WorldEventLog(capacity: 8);
            Assert.That(
                events.RecordPrivate(
                    1d,
                    WorldEventKind.GoalCompleted,
                    "芽芽完成胡萝卜收获。",
                    ResidentIds.Yaya,
                    factId: "fact-carrot-harvest",
                    tags: new[] { "carrot", "harvest" }).Succeeded,
                Is.True);
            Assert.That(
                events.RecordPerceivable(
                    2d,
                    WorldEventKind.CropMatured,
                    "芽芽看见胡萝卜成熟。",
                    new[] { ResidentIds.Yaya },
                    ResidentIds.Yaya,
                    plotNumber: 1,
                    factId: "fact-carrot-matured",
                    tags: new[] { "carrot", "matured" }).Succeeded,
                Is.True);
            Assert.That(
                events.RecordConversation(
                    3d,
                    WorldEventKind.ConversationCompleted,
                    "芽芽告诉小穗收获消息。",
                    ResidentIds.Yaya,
                    new[] { ResidentIds.Yaya, ResidentIds.Xiaosui },
                    factId: "fact-carrot-harvest",
                    tags: new[] { "carrot", "harvest" }).Succeeded,
                Is.True);
            Assert.That(
                events.RecordPublicTownEvent(
                    4d,
                    WorldEventKind.System,
                    "全镇停水通知。",
                    factId: "fact-water-notice",
                    tags: new[] { "water", "notice" }).Succeeded,
                Is.True);

            MemoryStore yaya = CaptureAll(events, ResidentIds.Yaya, expectedCount: 4);
            MemoryStore xiaosui = CaptureAll(events, ResidentIds.Xiaosui, expectedCount: 2);
            MemoryStore amu = CaptureAll(events, ResidentIds.Amu, expectedCount: 1);
            MemoryStore momo = CaptureAll(events, ResidentIds.Momo, expectedCount: 1);

            Assert.That(
                xiaosui.Entries.Any(entry => entry.RootFactId == "fact-carrot-matured"),
                Is.False);
            MemoryEntry learnedFromYaya = xiaosui.Entries.Single(
                entry => entry.RootFactId == "fact-carrot-harvest");
            Assert.That(learnedFromYaya.SourceKind, Is.EqualTo(MemorySourceKind.Conversation));
            Assert.That(learnedFromYaya.ImmediateSourceResidentId, Is.EqualTo(ResidentIds.Yaya));
            Assert.That(learnedFromYaya.IsShareable, Is.False);
            Assert.That(learnedFromYaya.SourceEventId, Is.Not.Empty);
            Assert.That(learnedFromYaya.Tags, Does.Contain("harvest"));
            Assert.That(amu.Entries.Single().SourceKind, Is.EqualTo(MemorySourceKind.PublicTownEvent));
            Assert.That(momo.Entries.Single().RootFactId, Is.EqualTo("fact-water-notice"));

            Assert.That(events.GetVisibleEntries(ResidentIds.Yaya), Has.Count.EqualTo(4));
            Assert.That(events.GetVisibleEntries(ResidentIds.Xiaosui), Has.Count.EqualTo(2));
            Assert.That(events.GetVisibleEntries(ResidentIds.Momo), Has.Count.EqualTo(1));
        }

        [Test]
        public void MemoryQuery_IsOwnerScopedAndRanksTagsThenImportanceThenRecency()
        {
            var store = new MemoryStore(ResidentIds.Yaya);
            AddKnowledge(store, 1d, "高重要单标签", 10, "fact-single", new[] { "carrot" });
            MemoryEntry older = AddKnowledge(
                store,
                2d,
                "高重要双标签",
                7,
                "fact-older",
                new[] { "carrot", "harvest" });
            MemoryEntry olderLow = AddKnowledge(
                store,
                3d,
                "较早低重要双标签",
                6,
                "fact-older-low",
                new[] { "carrot", "harvest" });
            MemoryEntry newer = AddKnowledge(
                store,
                4d,
                "较新低重要双标签",
                6,
                "fact-newer",
                new[] { "carrot", "harvest" });

            ActionResult query = store.Query(
                ResidentIds.Yaya,
                new[] { "HARVEST", "carrot" },
                4,
                out var matches);

            Assert.That(query.Succeeded, Is.True, query.Message);
            Assert.That(matches.Select(entry => entry.KnowledgeId), Is.EqualTo(new[]
            {
                older.KnowledgeId,
                newer.KnowledgeId,
                olderLow.KnowledgeId,
                store.Entries[0].KnowledgeId
            }));
            Assert.That(
                store.Query(ResidentIds.Amu, new[] { "carrot" }, 3, out var leaked).Failed,
                Is.True);
            Assert.That(leaked, Is.Empty);

            Assert.That(
                store.TryGetKnowledge(ResidentIds.Yaya, newer.KnowledgeId, out MemoryEntry found).Succeeded,
                Is.True);
            Assert.That(found, Is.SameAs(newer));
            Assert.That(
                store.TryGetKnowledge(ResidentIds.Amu, newer.KnowledgeId, out _).Failed,
                Is.True);
            Assert.That(
                store.ContainsRootFact(ResidentIds.Yaya, "fact-newer", out bool contains).Succeeded,
                Is.True);
            Assert.That(contains, Is.True);
            Assert.That(
                store.ContainsRootFact(ResidentIds.Amu, "fact-newer", out _).Failed,
                Is.True);
        }

        [Test]
        public void LegacyRecord_RemainsPublicAndGetsStableEventAndFactIds()
        {
            var events = new WorldEventLog(stableEventNamespace: "memory-visibility-test");
            Assert.That(
                events.Record(1d, WorldEventKind.System, "兼容事件").Succeeded,
                Is.True);

            WorldEventEntry entry = events.Entries.Single();
            Assert.That(entry.Visibility, Is.EqualTo(WorldEventVisibility.PublicTownEvent));
            Assert.That(
                entry.EventId,
                Is.EqualTo("world-event:memory-visibility-test:000000000001"));
            Assert.That(entry.FactId, Is.EqualTo(entry.EventId));
            Assert.That(entry.IsVisibleTo(ResidentIds.Yaya), Is.True);
            Assert.That(entry.IsVisibleTo(ResidentIds.Momo), Is.True);
        }

        [Test]
        public void ConversationEvent_RejectsActorOutsideItsTwoParticipants()
        {
            var events = new WorldEventLog();

            ActionResult recorded = events.RecordConversation(
                1d,
                WorldEventKind.ConversationCompleted,
                "第三人不能伪造二人对话来源。",
                ResidentIds.Momo,
                new[] { ResidentIds.Yaya, ResidentIds.Xiaosui });

            Assert.That(recorded.Failed, Is.True);
            Assert.That(events.Entries, Is.Empty);
        }

        [Test]
        public void SeparateWorldEventNamespaces_DoNotReuseFactIdsAfterColdStart()
        {
            var firstSession = new WorldEventLog(stableEventNamespace: "save-session-a");
            var secondSession = new WorldEventLog(stableEventNamespace: "save-session-b");
            Assert.That(
                firstSession.Record(1d, WorldEventKind.System, "第一轮事件").Succeeded,
                Is.True);
            Assert.That(
                secondSession.Record(1d, WorldEventKind.System, "冷启动后的另一事件").Succeeded,
                Is.True);

            Assert.That(
                firstSession.Entries.Single().EventId,
                Is.Not.EqualTo(secondSession.Entries.Single().EventId));
            Assert.That(
                firstSession.Entries.Single().FactId,
                Is.Not.EqualTo(secondSession.Entries.Single().FactId));
        }

        [Test]
        public void OwnerAwareRecentQueries_RejectCrossResidentReads()
        {
            var store = new MemoryStore(ResidentIds.Yaya);
            AddKnowledge(store, 1d, "芽芽私有", 5, "fact-private", new[] { "private" });
            Assert.That(
                store.GetRecentObservations(
                    ResidentIds.Yaya,
                    5,
                    out var ownObservations).Succeeded,
                Is.True);
            Assert.That(ownObservations, Has.Count.EqualTo(1));
            Assert.That(
                store.GetRecentObservations(
                    ResidentIds.Amu,
                    5,
                    out var crossObservations).Failed,
                Is.True);
            Assert.That(crossObservations, Is.Empty);
            Assert.That(
                store.GetRecentReflections(
                    ResidentIds.Amu,
                    5,
                    out var crossReflections).Failed,
                Is.True);
            Assert.That(crossReflections, Is.Empty);
        }

        private static MemoryStore CaptureAll(
            WorldEventLog events,
            ResidentId ownerResidentId,
            int expectedCount)
        {
            var store = new MemoryStore(ownerResidentId);
            var observations = new ObservationService(ownerResidentId);
            ActionResult captured = observations.CaptureNewObservations(
                events,
                store,
                out int capturedCount);
            Assert.That(captured.Succeeded, Is.True, captured.Message);
            Assert.That(capturedCount, Is.EqualTo(expectedCount));
            Assert.That(store.Entries, Has.Count.EqualTo(expectedCount));
            return store;
        }

        private static MemoryEntry AddKnowledge(
            MemoryStore store,
            double gameSeconds,
            string text,
            int importance,
            string rootFactId,
            string[] tags)
        {
            ActionResult added = store.AddObservation(
                store.OwnerResidentId,
                gameSeconds,
                text,
                importance,
                WorldEventKind.System,
                MemorySourceKind.Perception,
                rootFactId,
                parentKnowledgeId: null,
                immediateSourceResidentId: default,
                tags,
                isShareable: true,
                out MemoryEntry entry);
            Assert.That(added.Succeeded, Is.True, added.Message);
            return entry;
        }
    }
}
