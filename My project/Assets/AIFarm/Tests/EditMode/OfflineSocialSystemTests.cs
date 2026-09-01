using System.Collections.Generic;
using System.Linq;
using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Social;
using AIFarm.Town;
using NUnit.Framework;

namespace AIFarm.Tests.EditMode
{
    public sealed class OfflineSocialSystemTests
    {
        private static readonly TownLocationId Plaza =
            new TownLocationId("location-social-test");

        [Test]
        public void TwoIdleResidents_CanEstablishConversation()
        {
            SocialFixture fixture = CreateFixture();
            SocialOpportunity opportunity = Detect(
                fixture,
                Status(ResidentIds.Yaya),
                Status(ResidentIds.Amu));

            ActionResult started = fixture.Coordinator.TryStartConversation(
                opportunity.FirstResidentId,
                opportunity.SecondResidentId,
                "conversation-seat-01",
                "conversation-seat-02",
                targetSentenceCount: 4,
                monotonicSeconds: 10d,
                out ConversationSession session);

            Assert.That(started.Succeeded, Is.True, started.Message);
            Assert.That(session, Is.Not.Null);
            Assert.That(session.State, Is.EqualTo(ConversationState.Active));
            Assert.That(session.FirstResidentId, Is.Not.EqualTo(session.SecondResidentId));
            Assert.That(fixture.ParticipantLock.LockedResidentCount, Is.EqualTo(2));
            Assert.That(fixture.Reservations.ReservationCount, Is.EqualTo(2));
        }

        [Test]
        public void ThirdResident_CannotTakeResidentAlreadyInConversation()
        {
            SocialFixture fixture = CreateFixture();
            ConversationSession first = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                "seat-a",
                "seat-b");

            ActionResult attempted = fixture.Coordinator.TryStartConversation(
                ResidentIds.Xiaosui,
                ResidentIds.Amu,
                "seat-c",
                "seat-d",
                targetSentenceCount: 2,
                monotonicSeconds: 0.1d,
                out ConversationSession rejected);

            Assert.That(first.State, Is.EqualTo(ConversationState.Active));
            Assert.That(attempted.Failed, Is.True);
            Assert.That(rejected, Is.Null);
            Assert.That(fixture.ParticipantLock.IsLocked(ResidentIds.Xiaosui), Is.False);
            Assert.That(fixture.Coordinator.ActiveSessionCount, Is.EqualTo(1));
            Assert.That(fixture.Reservations.ReservationCount, Is.EqualTo(2));
        }

        [Test]
        public void Resident_CannotBelongToTwoConversationSessions()
        {
            SocialFixture fixture = CreateFixture();
            ConversationSession first = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                "seat-a",
                "seat-b");

            ActionResult second = fixture.Coordinator.TryStartConversation(
                ResidentIds.Yaya,
                ResidentIds.Xiaosui,
                "seat-c",
                "seat-d",
                targetSentenceCount: 2,
                monotonicSeconds: 0.1d,
                out _);

            Assert.That(second.Failed, Is.True);
            Assert.That(
                fixture.ParticipantLock.TryGetConversationId(
                    ResidentIds.Yaya,
                    out ConversationId activeId),
                Is.True);
            Assert.That(activeId, Is.EqualTo(first.ConversationId));
            Assert.That(fixture.Coordinator.ActiveSessionCount, Is.EqualTo(1));
        }

        [Test]
        public void Timeout_AlwaysReleasesParticipantLocksAndPositionReservations()
        {
            SocialFixture fixture = CreateFixture(timeoutSeconds: 2d);
            ConversationSession session = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                "seat-timeout-a",
                "seat-timeout-b",
                monotonicSeconds: 5d);

            int timedOut = fixture.Coordinator.TickTimeouts(7d);

            Assert.That(timedOut, Is.EqualTo(1));
            Assert.That(session.State, Is.EqualTo(ConversationState.TimedOut));
            Assert.That(session.EndReason, Is.EqualTo(ConversationEndReason.TimedOut));
            Assert.That(fixture.Coordinator.ActiveSessionCount, Is.Zero);
            Assert.That(fixture.ParticipantLock.LockedResidentCount, Is.Zero);
            Assert.That(fixture.Reservations.ReservationCount, Is.Zero);
        }

        [Test]
        public void CompletedOutcome_IsStoredInEachParticipantsIndependentMemoryOnly()
        {
            SocialFixture fixture = CreateFixture(
                outcomeSelector: _ => ConversationOutcome.Positive);
            ConversationSession session = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                "seat-memory-a",
                "seat-memory-b",
                targetSentenceCount: 2);

            Complete(fixture, session);
            ResidentRuntimeState first = Runtime(fixture, ResidentIds.Yaya);
            ResidentRuntimeState second = Runtime(fixture, ResidentIds.Amu);
            ResidentRuntimeState third = Runtime(fixture, ResidentIds.Xiaosui);

            Assert.That(session.Outcome, Is.EqualTo(ConversationOutcome.Positive));
            Assert.That(first.Memories.Entries, Has.Count.EqualTo(1));
            Assert.That(second.Memories.Entries, Has.Count.EqualTo(1));
            Assert.That(third.Memories.Entries, Is.Empty);
            Assert.That(first.Memories, Is.Not.SameAs(second.Memories));
            Assert.That(
                first.Memories.Entries.Single().OwnerResidentId,
                Is.EqualTo(ResidentIds.Yaya));
            Assert.That(
                second.Memories.Entries.Single().OwnerResidentId,
                Is.EqualTo(ResidentIds.Amu));
            Assert.That(
                first.Memories.Entries.Single().SourceEventKind,
                Is.EqualTo(WorldEventKind.ConversationCompleted));
            Assert.That(
                second.Memories.Entries.Single().SourceEventKind,
                Is.EqualTo(WorldEventKind.ConversationCompleted));
            Assert.That(
                first.Memories.Entries.Single().Text,
                Does.Contain("我与阿木").And.Contain("我说").And.Contain("阿木告诉我"));
            Assert.That(
                second.Memories.Entries.Single().Text,
                Does.Contain("我与芽芽").And.Contain("我说").And.Contain("芽芽告诉我"));
            Assert.That(
                first.Memories.Entries.Single().ImmediateSourceResidentId,
                Is.EqualTo(ResidentIds.Amu));
            Assert.That(
                second.Memories.Entries.Single().ImmediateSourceResidentId,
                Is.EqualTo(ResidentIds.Yaya));
        }

        [Test]
        public void HelpfulOutcome_IncreasesTrustByFixedRuleForBothDirections()
        {
            SocialFixture fixture = CreateFixture(
                outcomeSelector: _ => ConversationOutcome.Helpful);
            fixture.Graph.TryGetRelationship(
                ResidentIds.Yaya,
                ResidentIds.Amu,
                out RelationshipState yayaBefore);
            fixture.Graph.TryGetRelationship(
                ResidentIds.Amu,
                ResidentIds.Yaya,
                out RelationshipState amuBefore);
            int initialYayaTrust = yayaBefore.Trust;
            int initialAmuTrust = amuBefore.Trust;
            ConversationSession session = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                "seat-helpful-a",
                "seat-helpful-b",
                targetSentenceCount: 3);

            Complete(fixture, session);

            Assert.That(
                yayaBefore.Trust,
                Is.EqualTo(initialYayaTrust + ConversationOutcomeApplier.HelpfulTrustIncrease));
            Assert.That(
                amuBefore.Trust,
                Is.EqualTo(initialAmuTrust + ConversationOutcomeApplier.HelpfulTrustIncrease));
            Assert.That(yayaBefore.RelationVersion, Is.EqualTo(1));
            Assert.That(amuBefore.RelationVersion, Is.EqualTo(1));
        }

        [Test]
        public void NetworkUnavailable_EntireConversationCompletesUsingOnlyLocalTemplates()
        {
            SocialFixture fixture = CreateFixture();
            ConversationSession session = Start(
                fixture,
                ResidentIds.Xiaosui,
                ResidentIds.Momo,
                "seat-offline-a",
                "seat-offline-b",
                targetSentenceCount: ConversationSession.MaximumSentenceCount);

            Complete(fixture, session);

            Assert.That(session.State, Is.EqualTo(ConversationState.Completed));
            Assert.That(session.Utterances, Has.Count.EqualTo(6));
            Assert.That(
                session.Utterances.All(line => !string.IsNullOrWhiteSpace(line.Text)),
                Is.True);
            Assert.That(
                System.Enum.IsDefined(typeof(ConversationOutcome), session.Outcome.Value),
                Is.True);
            Assert.That(fixture.ParticipantLock.LockedResidentCount, Is.Zero);
            Assert.That(fixture.Reservations.ReservationCount, Is.Zero);
        }

        [Test]
        public void LocalTemplates_ChangeWithBothNamesAndPersonas()
        {
            SocialFixture firstFixture = CreateFixture();
            ConversationSession firstSession = Start(
                firstFixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                "seat-template-a",
                "seat-template-b");
            Assert.That(
                firstFixture.Coordinator.AdvanceConversation(
                    firstSession.ConversationId,
                    0.1d,
                    10d,
                    out ConversationUtterance firstLine).Succeeded,
                Is.True);

            SocialFixture secondFixture = CreateFixture();
            ConversationSession secondSession = Start(
                secondFixture,
                ResidentIds.Yaya,
                ResidentIds.Xiaosui,
                "seat-template-c",
                "seat-template-d");
            Assert.That(
                secondFixture.Coordinator.AdvanceConversation(
                    secondSession.ConversationId,
                    0.1d,
                    10d,
                    out ConversationUtterance secondLine).Succeeded,
                Is.True);

            Assert.That(firstLine.Text, Is.Not.EqualTo(secondLine.Text));
            Assert.That(firstLine.Text, Does.Contain(ResidentDefinition.Yaya.DisplayName));
            Assert.That(firstLine.Text, Does.Contain(ResidentDefinition.Amu.DisplayName));
            Assert.That(secondLine.Text, Does.Contain(ResidentDefinition.Yaya.DisplayName));
            Assert.That(secondLine.Text, Does.Contain(ResidentDefinition.Xiaosui.DisplayName));
            Assert.That(firstLine.Text, Does.Contain(ResidentDefinition.Yaya.Persona.Role));
        }

        [Test]
        public void OpportunityDetector_EnforcesCooldownAndPriorityExclusions()
        {
            SocialFixture fixture = CreateFixture(cooldownGameSeconds: 100d);
            SocialOpportunity first = Detect(
                fixture,
                Status(ResidentIds.Yaya),
                Status(ResidentIds.Amu));
            Assert.That(
                fixture.Detector.RecordConversationStarted(first, 50d).Succeeded,
                Is.True);

            ActionResult coolingDown = fixture.Detector.TryDetect(
                new[] { Status(ResidentIds.Yaya), Status(ResidentIds.Amu) },
                149d,
                fixture.ParticipantLock,
                out _);
            ActionResult priorityBlocked = fixture.Detector.TryDetect(
                new[]
                {
                    Status(ResidentIds.Xiaosui, emergencyFarmWork: true),
                    Status(ResidentIds.Momo, goingHomeToSleep: true)
                },
                200d,
                fixture.ParticipantLock,
                out _);
            ActionResult playerInstructionBlocked = fixture.Detector.TryDetect(
                new[]
                {
                    Status(ResidentIds.Xiaosui, playerInstruction: true),
                    Status(ResidentIds.Momo)
                },
                200d,
                fixture.ParticipantLock,
                out _);

            Assert.That(coolingDown.Failed, Is.True);
            Assert.That(priorityBlocked.Failed, Is.True);
            Assert.That(playerInstructionBlocked.Failed, Is.True);
            Assert.That(fixture.Detector.IsCooldownComplete(ResidentIds.Yaya, 150d), Is.True);
            Assert.That(fixture.Detector.IsCooldownComplete(ResidentIds.Yaya, 0d), Is.True);
        }

        [Test]
        public void PreparedRemoteScript_PlaysMoodEmojiAndAppliesOnlyOutcomeTagRules()
        {
            SocialFixture fixture = CreateFixture();
            fixture.Graph.TryGetRelationship(
                ResidentIds.Yaya,
                ResidentIds.Amu,
                out RelationshipState relationship);
            ConversationSession session = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                "seat-remote-a",
                "seat-remote-b",
                targetSentenceCount: 4);
            var script = new ConversationScriptSpec(
                session.FirstResidentId,
                new[]
                {
                    new ConversationLineSpec(
                        session.FirstResidentId,
                        NpcMood.Happy,
                        "🙂",
                        "第一句已经实际播放。"),
                    new ConversationLineSpec(
                        session.SecondResidentId,
                        NpcMood.Focused,
                        "✓",
                        "第二句也已经实际播放。")
                },
                ConversationOutcome.Helpful,
                "openai");

            Assert.That(
                fixture.Coordinator.SetConversationScript(
                    session.ConversationId,
                    script).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.AdvanceConversation(
                    session.ConversationId,
                    0.1d,
                    10d,
                    out ConversationUtterance first).Succeeded,
                Is.True);
            Assert.That(first.Mood, Is.EqualTo(NpcMood.Happy));
            Assert.That(first.Emoji, Is.EqualTo("🙂"));
            Assert.That(
                fixture.Coordinator.AdvanceConversation(
                    session.ConversationId,
                    0.2d,
                    11d,
                    out ConversationUtterance second).Succeeded,
                Is.True);

            Assert.That(second.Mood, Is.EqualTo(NpcMood.Focused));
            Assert.That(session.State, Is.EqualTo(ConversationState.Completed));
            Assert.That(
                relationship.Trust,
                Is.EqualTo(ConversationOutcomeApplier.HelpfulTrustIncrease));
            Assert.That(
                Runtime(fixture, ResidentIds.Yaya).Memories.Entries.Single().Text,
                Does.Contain("第一句已经实际播放。"));
            Assert.That(
                Runtime(fixture, ResidentIds.Amu).Memories.Entries.Single().Text,
                Does.Contain("第二句也已经实际播放。"));
        }

        [Test]
        public void CancelledPreparedScript_RecordsPlayedLineButNotUnplayedContentOrOutcome()
        {
            SocialFixture fixture = CreateFixture();
            fixture.Graph.TryGetRelationship(
                ResidentIds.Yaya,
                ResidentIds.Amu,
                out RelationshipState relationship);
            ConversationSession session = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                "seat-cancel-a",
                "seat-cancel-b",
                targetSentenceCount: 3);
            var script = new ConversationScriptSpec(
                session.FirstResidentId,
                new[]
                {
                    new ConversationLineSpec(
                        session.FirstResidentId,
                        NpcMood.Happy,
                        "1",
                        "只播放这一句。"),
                    new ConversationLineSpec(
                        session.SecondResidentId,
                        NpcMood.Focused,
                        "2",
                        "这句尚未播放。"),
                    new ConversationLineSpec(
                        session.FirstResidentId,
                        NpcMood.Proud,
                        "3",
                        "这一句也没有播放。")
                },
                ConversationOutcome.Conflict,
                "openai");
            Assert.That(
                fixture.Coordinator.SetConversationScript(
                    session.ConversationId,
                    script).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.AdvanceConversation(
                    session.ConversationId,
                    0.1d,
                    12d,
                    out _).Succeeded,
                Is.True);

            Assert.That(
                fixture.Coordinator.CancelConversation(session.ConversationId).Succeeded,
                Is.True);

            string memory = Runtime(
                fixture,
                ResidentIds.Yaya).Memories.Entries.Single().Text;
            Assert.That(memory, Does.Contain("只播放这一句。"));
            Assert.That(memory, Does.Not.Contain("这句尚未播放。"));
            Assert.That(memory, Does.Not.Contain("Conflict"));
            Assert.That(relationship.RelationVersion, Is.Zero);
            Assert.That(fixture.ParticipantLock.LockedResidentCount, Is.Zero);
            Assert.That(fixture.Reservations.ReservationCount, Is.Zero);
        }

        [Test]
        public void PreparedScript_RejectsSpeakerOutsideSessionParticipants()
        {
            SocialFixture fixture = CreateFixture();
            ConversationSession session = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                "seat-invalid-a",
                "seat-invalid-b");
            var script = new ConversationScriptSpec(
                session.FirstResidentId,
                new[]
                {
                    new ConversationLineSpec(
                        new ResidentId("resident-not-in-town"),
                        NpcMood.Happy,
                        "!",
                        "非法说话者。"),
                    new ConversationLineSpec(
                        session.SecondResidentId,
                        NpcMood.Focused,
                        "?",
                        "合法说话者。")
                },
                ConversationOutcome.Neutral,
                "openai");

            ActionResult attached = fixture.Coordinator.SetConversationScript(
                session.ConversationId,
                script);

            Assert.That(attached.Failed, Is.True);
            Assert.That(session.HasPreparedScript, Is.False);
            Assert.That(session.Utterances, Is.Empty);
        }

        [Test]
        public void OfflineInformationPropagation_PreservesImmediateSourceAcrossTwoHops()
        {
            SocialFixture fixture = CreateFixture(
                outcomeSelector: _ => ConversationOutcome.Helpful);
            ResidentRuntimeState yaya = Runtime(fixture, ResidentIds.Yaya);
            ResidentRuntimeState xiaosui = Runtime(fixture, ResidentIds.Xiaosui);
            ResidentRuntimeState amu = Runtime(fixture, ResidentIds.Amu);
            ResidentRuntimeState momo = Runtime(fixture, ResidentIds.Momo);
            ActionResult perceived = yaya.Memories.AddObservation(
                ResidentIds.Yaya,
                gameSeconds: 100d,
                text: "芽芽亲眼看到并完成了胡萝卜收获。",
                importance: 9,
                sourceEventKind: WorldEventKind.GoalCompleted,
                sourceKind: MemorySourceKind.Perception,
                rootFactId: "fact-carrot-harvest-001",
                parentKnowledgeId: null,
                immediateSourceResidentId: default,
                tags: new[] { "carrot", "harvest" },
                isShareable: true,
                out MemoryEntry yayaKnowledge);
            Assert.That(perceived.Succeeded, Is.True, perceived.Message);
            Assert.That(xiaosui.Memories.Entries, Is.Empty);
            Assert.That(amu.Memories.Entries, Is.Empty);
            Assert.That(momo.Memories.Entries, Is.Empty);

            ConversationSession firstConversation = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Xiaosui,
                "plaza-yaya",
                "plaza-xiaosui",
                targetSentenceCount: 2);
            ActionResult firstPrepared = fixture.Coordinator.PrepareLocalFallbackScript(
                firstConversation.ConversationId);
            Assert.That(firstPrepared.Succeeded, Is.True, firstPrepared.Message);
            Complete(fixture, firstConversation);

            MemoryEntry xiaosuiSummary = xiaosui.Memories.Entries.Single(
                entry => entry.Kind == MemoryEntryKind.ConversationSummary);
            MemoryEntry xiaosuiKnowledge = xiaosui.Memories.Entries.Single(
                entry => entry.RootFactId == yayaKnowledge.RootFactId);
            Assert.That(xiaosuiSummary.IsShareable, Is.False);
            Assert.That(xiaosuiSummary.RootFactId, Is.Not.EqualTo(yayaKnowledge.RootFactId));
            Assert.That(xiaosuiKnowledge.Kind, Is.EqualTo(MemoryEntryKind.Observation));
            Assert.That(xiaosuiKnowledge.SourceKind, Is.EqualTo(MemorySourceKind.Conversation));
            Assert.That(xiaosuiKnowledge.ImmediateSourceResidentId, Is.EqualTo(ResidentIds.Yaya));
            Assert.That(xiaosuiKnowledge.RootFactId, Is.EqualTo(yayaKnowledge.RootFactId));
            Assert.That(xiaosuiKnowledge.ParentKnowledgeId, Is.EqualTo(yayaKnowledge.KnowledgeId));
            Assert.That(xiaosuiKnowledge.Tags, Does.Contain("harvest"));
            Assert.That(xiaosuiKnowledge.Text, Does.Contain("芽芽告诉我"));

            ConversationSession secondConversation = Start(
                fixture,
                ResidentIds.Xiaosui,
                ResidentIds.Amu,
                "plaza-xiaosui-second",
                "plaza-amu",
                targetSentenceCount: 2,
                monotonicSeconds: 1d);
            ActionResult secondPrepared = fixture.Coordinator.PrepareLocalFallbackScript(
                secondConversation.ConversationId);
            Assert.That(secondPrepared.Succeeded, Is.True, secondPrepared.Message);
            Complete(fixture, secondConversation);

            MemoryEntry amuSummary = amu.Memories.Entries.Single(
                entry => entry.Kind == MemoryEntryKind.ConversationSummary);
            MemoryEntry amuKnowledge = amu.Memories.Entries.Single(
                entry => entry.RootFactId == yayaKnowledge.RootFactId);
            Assert.That(amuSummary.IsShareable, Is.False);
            Assert.That(amuKnowledge.Kind, Is.EqualTo(MemoryEntryKind.Observation));
            Assert.That(amuKnowledge.ImmediateSourceResidentId, Is.EqualTo(ResidentIds.Xiaosui));
            Assert.That(amuKnowledge.ImmediateSourceResidentId, Is.Not.EqualTo(ResidentIds.Yaya));
            Assert.That(amuKnowledge.RootFactId, Is.EqualTo(yayaKnowledge.RootFactId));
            Assert.That(amuKnowledge.ParentKnowledgeId, Is.EqualTo(xiaosuiKnowledge.KnowledgeId));
            Assert.That(amuKnowledge.Text, Does.Contain("小穗告诉我"));
            Assert.That(momo.Memories.Entries, Is.Empty);
        }

        [Test]
        public void CompletedConversation_StoresEachSharedFactSeparately_FromPrivateSummary()
        {
            SocialFixture fixture = CreateFixture();
            ResidentRuntimeState yaya = Runtime(fixture, ResidentIds.Yaya);
            ResidentRuntimeState xiaosui = Runtime(fixture, ResidentIds.Xiaosui);
            Assert.That(
                yaya.Memories.AddObservation(
                    ResidentIds.Yaya,
                    1d,
                    "胡萝卜已经收获。",
                    9,
                    WorldEventKind.GoalCompleted,
                    MemorySourceKind.Perception,
                    "fact-harvest-separate",
                    null,
                    default,
                    new[] { "harvest" },
                    true,
                    out MemoryEntry harvest).Succeeded,
                Is.True);
            Assert.That(
                yaya.Memories.AddObservation(
                    ResidentIds.Yaya,
                    2d,
                    "水井今天已经修好。",
                    8,
                    WorldEventKind.System,
                    MemorySourceKind.Perception,
                    "fact-well-separate",
                    null,
                    default,
                    new[] { "well" },
                    true,
                    out MemoryEntry well).Succeeded,
                Is.True);
            ConversationSession session = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Xiaosui,
                "separate-facts-a",
                "separate-facts-b",
                targetSentenceCount: 4);
            var script = new ConversationScriptSpec(
                ResidentIds.Yaya,
                new[]
                {
                    new ConversationLineSpec(
                        ResidentIds.Yaya,
                        NpcMood.Happy,
                        "🥕",
                        "第一条事实。",
                        harvest.KnowledgeId),
                    new ConversationLineSpec(
                        ResidentIds.Xiaosui,
                        NpcMood.Focused,
                        "🙂",
                        "这句只是普通回应。"),
                    new ConversationLineSpec(
                        ResidentIds.Yaya,
                        NpcMood.Proud,
                        "💧",
                        "第二条事实。",
                        well.KnowledgeId),
                    new ConversationLineSpec(
                        ResidentIds.Xiaosui,
                        NpcMood.Happy,
                        "✓",
                        "这句私人寒暄不得跟随事实传播。")
                },
                ConversationOutcome.Helpful,
                "test");
            Assert.That(
                fixture.Coordinator.SetConversationScript(
                    session.ConversationId,
                    script).Succeeded,
                Is.True);
            Complete(fixture, session);

            MemoryEntry summary = xiaosui.Memories.Entries.Single(
                entry => entry.Kind == MemoryEntryKind.ConversationSummary);
            MemoryEntry receivedHarvest = xiaosui.Memories.Entries.Single(
                entry => entry.RootFactId == harvest.RootFactId);
            MemoryEntry receivedWell = xiaosui.Memories.Entries.Single(
                entry => entry.RootFactId == well.RootFactId);
            Assert.That(summary.IsShareable, Is.False);
            Assert.That(summary.Text, Does.Contain("私人寒暄"));
            Assert.That(receivedHarvest.ParentKnowledgeId, Is.EqualTo(harvest.KnowledgeId));
            Assert.That(receivedWell.ParentKnowledgeId, Is.EqualTo(well.KnowledgeId));
            Assert.That(receivedHarvest.Text, Does.Not.Contain("私人寒暄"));
            Assert.That(receivedWell.Text, Does.Not.Contain("私人寒暄"));
        }

        [Test]
        public void PreparedScript_RejectsKnowledgeReferenceNotOwnedBySpeaker()
        {
            SocialFixture fixture = CreateFixture();
            ResidentRuntimeState yaya = Runtime(fixture, ResidentIds.Yaya);
            Assert.That(
                yaya.Memories.AddObservation(
                    ResidentIds.Yaya,
                    10d,
                    "芽芽的私人来源事实。",
                    8,
                    WorldEventKind.GoalCompleted,
                    MemorySourceKind.Perception,
                    "fact-owned-by-yaya",
                    null,
                    default,
                    new[] { "private-owner-check" },
                    true,
                    out MemoryEntry yayaKnowledge).Succeeded,
                Is.True);
            ConversationSession session = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                "owner-check-a",
                "owner-check-b",
                targetSentenceCount: 2);
            var script = new ConversationScriptSpec(
                session.FirstResidentId,
                new[]
                {
                    new ConversationLineSpec(
                        ResidentIds.Amu,
                        NpcMood.Focused,
                        "!",
                        "阿木不能冒充这条记忆的拥有者。",
                        yayaKnowledge.KnowledgeId),
                    new ConversationLineSpec(
                        ResidentIds.Yaya,
                        NpcMood.Happy,
                        "?",
                        "这段脚本不应开始播放。")
                },
                ConversationOutcome.Neutral,
                "test");

            ActionResult attached = fixture.Coordinator.SetConversationScript(
                session.ConversationId,
                script);

            Assert.That(attached.Failed, Is.True);
            Assert.That(session.HasPreparedScript, Is.False);
            Assert.That(session.Utterances, Is.Empty);
        }

        [Test]
        public void PreparedKnowledgeSnapshot_SurvivesSourceMemoryEvictionBeforeCompletion()
        {
            SocialFixture fixture = CreateFixture();
            ResidentRuntimeState yaya = Runtime(fixture, ResidentIds.Yaya);
            ResidentRuntimeState xiaosui = Runtime(fixture, ResidentIds.Xiaosui);
            Assert.That(
                yaya.Memories.AddObservation(
                    ResidentIds.Yaya,
                    1d,
                    "会话准备时仍存在的胡萝卜收获事实。",
                    9,
                    WorldEventKind.GoalCompleted,
                    MemorySourceKind.Perception,
                    "fact-evicted-after-script-preparation",
                    null,
                    default,
                    new[] { "carrot", "harvest" },
                    true,
                    out MemoryEntry source).Succeeded,
                Is.True);
            ConversationSession session = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Xiaosui,
                "snapshot-eviction-a",
                "snapshot-eviction-b",
                targetSentenceCount: 2);
            Assert.That(
                fixture.Coordinator.PrepareLocalFallbackScript(
                    session.ConversationId).Succeeded,
                Is.True);
            Assert.That(
                session.PreparedLines[0].SharedKnowledgeId,
                Is.EqualTo(source.KnowledgeId));

            for (int index = 0; index < MemoryStore.DefaultCapacity - 1; index++)
            {
                Assert.That(
                    yaya.Memories.AddObservation(
                        ResidentIds.Yaya,
                        2d + index,
                        $"用于填满记忆容量的高重要度事实 {index}。",
                        10,
                        WorldEventKind.System,
                        MemorySourceKind.Perception,
                        $"fact-capacity-filler-{index}",
                        null,
                        default,
                        new[] { "capacity-filler" },
                        true,
                        out MemoryEntry _).Succeeded,
                    Is.True);
            }

            Complete(fixture, session);

            Assert.That(
                yaya.Memories.TryGetKnowledge(
                    ResidentIds.Yaya,
                    source.KnowledgeId,
                    out MemoryEntry _).Failed,
                Is.True,
                "Writing the first perspective summary should evict the older source entry.");
            MemoryEntry received = xiaosui.Memories.Entries.Single(
                entry => entry.RootFactId == source.RootFactId);
            Assert.That(received.ParentKnowledgeId, Is.EqualTo(source.KnowledgeId));
            Assert.That(received.ImmediateSourceResidentId, Is.EqualTo(ResidentIds.Yaya));
            Assert.That(received.Text, Does.Contain("芽芽告诉我"));
        }

        [Test]
        public void CancelledConversation_DoesNotPropagatePlayedKnowledgeReference()
        {
            SocialFixture fixture = CreateFixture();
            ResidentRuntimeState yaya = Runtime(fixture, ResidentIds.Yaya);
            ResidentRuntimeState xiaosui = Runtime(fixture, ResidentIds.Xiaosui);
            Assert.That(
                yaya.Memories.AddObservation(
                    ResidentIds.Yaya,
                    10d,
                    "只在完整会话后传播的事实。",
                    8,
                    WorldEventKind.GoalCompleted,
                    MemorySourceKind.Perception,
                    "fact-cancelled-conversation",
                    null,
                    default,
                    new[] { "cancel-check" },
                    true,
                    out MemoryEntry source).Succeeded,
                Is.True);
            ConversationSession session = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Xiaosui,
                "cancel-transfer-a",
                "cancel-transfer-b",
                targetSentenceCount: 2);
            Assert.That(
                fixture.Coordinator.PrepareLocalFallbackScript(
                    session.ConversationId).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.AdvanceConversation(
                    session.ConversationId,
                    0.1d,
                    11d,
                    out ConversationUtterance played).Succeeded,
                Is.True);
            Assert.That(played.SharedKnowledgeId, Is.EqualTo(source.KnowledgeId));

            Assert.That(
                fixture.Coordinator.CancelConversation(session.ConversationId).Succeeded,
                Is.True);

            MemoryEntry fragment = xiaosui.Memories.Entries.Single();
            Assert.That(fragment.Kind, Is.EqualTo(MemoryEntryKind.Observation));
            Assert.That(fragment.SourceKind, Is.EqualTo(MemorySourceKind.Conversation));
            Assert.That(fragment.IsShareable, Is.False);
            Assert.That(
                xiaosui.Memories.ContainsRootFact(
                    ResidentIds.Xiaosui,
                    source.RootFactId,
                    out bool learned).Succeeded,
                Is.True);
            Assert.That(learned, Is.False);
        }

        private static SocialFixture CreateFixture(
            double timeoutSeconds = ConversationCoordinator.DefaultTimeoutSeconds,
            double cooldownGameSeconds = SocialOpportunityDetector.DefaultCooldownGameSeconds,
            System.Func<ConversationSession, ConversationOutcome> outcomeSelector = null)
        {
            var registry = new ResidentRegistry();
            foreach (ResidentDefinition definition in ResidentDefinition.TownResidents)
            {
                ActionResult registered = registry.Register(
                    definition,
                    new ResidentRuntimeState(definition));
                Assert.That(registered.Succeeded, Is.True, registered.Message);
            }

            var graph = new SocialGraph(registry.ResidentIds);
            var participantLock = new ConversationParticipantLock();
            var reservations = new InteractionPointReservationService();
            var templates = new LocalConversationTemplateService(outcomeSelector);
            var applier = new ConversationOutcomeApplier(registry, graph);
            var coordinator = new ConversationCoordinator(
                registry,
                participantLock,
                reservations,
                templates,
                applier,
                timeoutSeconds);
            return new SocialFixture(
                registry,
                graph,
                participantLock,
                reservations,
                new SocialOpportunityDetector(cooldownGameSeconds),
                coordinator);
        }

        private static SocialOpportunity Detect(
            SocialFixture fixture,
            params SocialResidentStatus[] statuses)
        {
            ActionResult detected = fixture.Detector.TryDetect(
                statuses,
                gameSeconds: 0d,
                fixture.ParticipantLock,
                out SocialOpportunity opportunity);
            Assert.That(detected.Succeeded, Is.True, detected.Message);
            return opportunity;
        }

        private static SocialResidentStatus Status(
            ResidentId residentId,
            bool emergencyFarmWork = false,
            bool playerInstruction = false,
            bool goingHomeToSleep = false)
        {
            return new SocialResidentStatus(
                residentId,
                Plaza,
                isIdle: true,
                hasActiveAction: false,
                isPerformingEmergencyFarmWork: emergencyFarmWork,
                hasPlayerInstruction: playerInstruction,
                isGoingHomeToSleep: goingHomeToSleep);
        }

        private static ConversationSession Start(
            SocialFixture fixture,
            ResidentId firstResidentId,
            ResidentId secondResidentId,
            string firstPoint,
            string secondPoint,
            int targetSentenceCount = 4,
            double monotonicSeconds = 0d)
        {
            ActionResult started = fixture.Coordinator.TryStartConversation(
                firstResidentId,
                secondResidentId,
                firstPoint,
                secondPoint,
                targetSentenceCount,
                monotonicSeconds,
                out ConversationSession session);
            Assert.That(started.Succeeded, Is.True, started.Message);
            return session;
        }

        private static void Complete(SocialFixture fixture, ConversationSession session)
        {
            double monotonicSeconds = session.StartedAtMonotonicSeconds;
            while (session.State == ConversationState.Active)
            {
                monotonicSeconds += 0.1d;
                ActionResult advanced = fixture.Coordinator.AdvanceConversation(
                    session.ConversationId,
                    monotonicSeconds,
                    gameSeconds: 100d + monotonicSeconds,
                    out _);
                Assert.That(advanced.Succeeded, Is.True, advanced.Message);
            }
        }

        private static ResidentRuntimeState Runtime(
            SocialFixture fixture,
            ResidentId residentId)
        {
            ActionResult resolved = fixture.Registry.TryGetRuntimeState(
                residentId,
                out ResidentRuntimeState runtime);
            Assert.That(resolved.Succeeded, Is.True, resolved.Message);
            return runtime;
        }

        private sealed class SocialFixture
        {
            public SocialFixture(
                ResidentRegistry registry,
                SocialGraph graph,
                ConversationParticipantLock participantLock,
                InteractionPointReservationService reservations,
                SocialOpportunityDetector detector,
                ConversationCoordinator coordinator)
            {
                Registry = registry;
                Graph = graph;
                ParticipantLock = participantLock;
                Reservations = reservations;
                Detector = detector;
                Coordinator = coordinator;
            }

            public ResidentRegistry Registry { get; }

            public SocialGraph Graph { get; }

            public ConversationParticipantLock ParticipantLock { get; }

            public InteractionPointReservationService Reservations { get; }

            public SocialOpportunityDetector Detector { get; }

            public ConversationCoordinator Coordinator { get; }
        }
    }
}
