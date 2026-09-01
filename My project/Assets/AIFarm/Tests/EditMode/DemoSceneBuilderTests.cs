using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Editor;
using AIFarm.Npc;
using AIFarm.Presentation;
using AIFarm.Social;
using AIFarm.Town;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AIFarm.Tests.EditMode
{
    public sealed class DemoSceneBuilderTests
    {
        [Test]
        public void BuildDemoScene_Twice_RebuildsOneCompleteSceneWithoutDuplicates()
        {
            Assert.That(DemoSceneBuilder.BuildDemoScene(promptToSaveCurrentScenes: false), Is.True);
            AssertGeneratedScene();

            Assert.That(DemoSceneBuilder.BuildDemoScene(promptToSaveCurrentScenes: false), Is.True);
            AssertGeneratedScene();

            Scene reopened = EditorSceneManager.OpenScene(
                DemoSceneBuilder.ScenePath,
                OpenSceneMode.Single);
            Assert.That(reopened.IsValid(), Is.True);
            AssertGeneratedScene();
        }

        [Test]
        public void BuiltScene_TransientLifecycleAndHarvestDinnerLoadRecoveryRemainSafe()
        {
            Assert.That(DemoSceneBuilder.BuildDemoScene(promptToSaveCurrentScenes: false), Is.True);
            GameBootstrap bootstrap = GameObject.Find("GameBootstrap").GetComponent<GameBootstrap>();
            TownScheduleCoordinator schedules =
                bootstrap.GetComponent<TownScheduleCoordinator>();
            TownSocialCoordinator social =
                bootstrap.GetComponent<TownSocialCoordinator>();
            TownEventSceneCoordinator townEvents =
                bootstrap.GetComponent<TownEventSceneCoordinator>();
            ReplanController replanner = bootstrap.GetComponent<ReplanController>();
            GameObject yayaObject = GameObject.Find("NPC_Blockout_Capsule");
            NpcPlanExecutor executor = yayaObject.GetComponent<NpcPlanExecutor>();
            TownResidentScheduleController[] residents =
                Object.FindObjectsByType<TownResidentScheduleController>();

            Assert.That(residents, Has.Length.EqualTo(4));
            Assert.That(bootstrap.Initialize().Succeeded, Is.True);
            Assert.That(executor.Initialize().Succeeded, Is.True);
            Assert.That(replanner.Initialize().Succeeded, Is.True);
            Assert.That(schedules.Initialize().Succeeded, Is.True);
            Assert.That(social.Initialize().Succeeded, Is.True);
            Assert.That(townEvents.Initialize().Succeeded, Is.True);
            Assert.That(townEvents.IsInitialized, Is.True);
            foreach (TownResidentScheduleController resident in residents)
            {
                resident.enabled = false;
                InvokeLifecycle(resident, "OnDisable");
                Assert.That(
                    resident.Runtime.State,
                    Is.EqualTo(ResidentScheduleState.Suspended));
            }

            Assert.That(schedules.ReservationService.ReservationCount, Is.Zero);
            Assert.That(schedules.TickSchedules(0.1f, 10d).Succeeded, Is.True);
            Assert.That(
                schedules.ReservationService.ReservationCount,
                Is.Zero,
                "Disabled resident controllers must not reacquire schedule reservations.");
            foreach (TownResidentScheduleController resident in residents)
            {
                resident.enabled = true;
                InvokeLifecycle(resident, "OnEnable");
                Assert.That(
                    resident.Runtime.State,
                    Is.EqualTo(ResidentScheduleState.WaitingForSchedule));
            }

            townEvents.enabled = false;
            InvokeLifecycle(townEvents, "OnDisable");
            Assert.That(townEvents.IsInitialized, Is.True);
            townEvents.enabled = true;
            InvokeLifecycle(townEvents, "OnEnable");
            Assert.That(townEvents.IsInitialized, Is.True);

            TownResidentScheduleController yaya =
                yayaObject.GetComponent<TownResidentScheduleController>();
            Assert.That(yaya.TickSchedule(17 * 60, 20d, 0f).Succeeded, Is.True);
            Assert.That(yaya.SuspendForTownEvent().Succeeded, Is.True);
            ActionResult blockedGoal = replanner.SubmitGoal(
                "把九块地种满胡萝卜并照顾到收获");
            Assert.That(blockedGoal.Failed, Is.True);
            Assert.That(replanner.RuntimeState.CurrentGoal, Is.Null);
            Assert.That(executor.IsBusy, Is.False);
            Assert.That(yaya.ResumeAfterTownEvent().Succeeded, Is.True);

            const string conversationId = "conversation:scene-save-guard";
            const string rootFactId = "fact:scene-save-guard-carrot-harvest";
            double proposedAt = bootstrap.Clock.ElapsedGameSeconds;
            Assert.That(
                bootstrap.ResidentRegistry.TryGetRuntimeState(
                    ResidentIds.Yaya,
                    out ResidentRuntimeState yayaRuntime).Succeeded,
                Is.True);
            Assert.That(
                bootstrap.ResidentRegistry.TryGetRuntimeState(
                    ResidentIds.Xiaosui,
                    out ResidentRuntimeState xiaosuiRuntime).Succeeded,
                Is.True);
            Assert.That(
                yayaRuntime.Memories.AddObservation(
                    ResidentIds.Yaya,
                    proposedAt - 2d,
                    "I harvested the carrots.",
                    9,
                    WorldEventKind.ActionCompleted,
                    MemorySourceKind.Perception,
                    "farm-event:scene-save-guard",
                    rootFactId,
                    string.Empty,
                    default,
                    new[] { "carrot", "harvest" },
                    true,
                    out MemoryEntry sourceKnowledge).Succeeded,
                Is.True);
            Assert.That(
                xiaosuiRuntime.Memories.AddObservation(
                    ResidentIds.Xiaosui,
                    proposedAt - 1d,
                    "Yaya told me about the carrot harvest.",
                    9,
                    WorldEventKind.ConversationCompleted,
                    MemorySourceKind.Conversation,
                    conversationId,
                    rootFactId,
                    sourceKnowledge.KnowledgeId,
                    ResidentIds.Yaya,
                    new[] { "carrot", "harvest" },
                    true,
                    out MemoryEntry receivedKnowledge).Succeeded,
                Is.True);
            var decision = new ResidentDecisionSpec(
                ResidentIds.Xiaosui,
                ResidentHighLevelIntents.ProposeTownEvent,
                null,
                "Let's make carrot soup tonight.",
                "Test");
            Assert.That(
                TownEventProposal.TryCreateFromDecision(
                    decision,
                    receivedKnowledge.KnowledgeId,
                    conversationId,
                    proposedAt,
                    out TownEventProposal proposal).Succeeded,
                Is.True);
            Assert.That(
                townEvents.TrySubmitProposal(proposal, out TownEventSession session).Succeeded,
                Is.True);

            var storage = new RecordingSaveStorage();
            var saveService = new SaveGameService(
                bootstrap,
                executor,
                replanner,
                yayaObject.transform,
                storage);
            ActionResult stableSave = saveService.Save();
            Assert.That(stableSave.Succeeded, Is.True, stableSave.Message);
            Assert.That(storage.WriteCount, Is.EqualTo(1));
            Assert.That(session.IsTerminal, Is.False);
            Assert.That(townEvents.EventCoordinator.ActiveTownEventCount, Is.EqualTo(1));
            Assert.That(storage.Json, Does.Not.Contain("TownEventSession"));
            Assert.That(storage.Json, Does.Not.Contain("activeRequest"));

            Assert.That(
                townEvents.EventCoordinator.TryBeginGathering(
                    session.ScheduledStartGameSeconds,
                    monotonicSeconds: 0d).Succeeded,
                Is.True);
            Assert.That(session.State, Is.EqualTo(TownEventState.Gathering));
            Assert.That(schedules.ReservationService.ReservationCount, Is.EqualTo(2));

            ActionResult loadedDuringEvent = saveService.Load();

            Assert.That(loadedDuringEvent.Succeeded, Is.True, loadedDuringEvent.Message);
            Assert.That(session.State, Is.EqualTo(TownEventState.Cancelled));
            Assert.That(townEvents.EventCoordinator.ActiveTownEventCount, Is.Zero);
            Assert.That(schedules.ReservationService.ReservationCount, Is.Zero);
            foreach (ResidentId residentId in ResidentIds.TownResidents)
            {
                Assert.That(
                    bootstrap.ResidentRegistry.TryGetRuntimeState(
                        residentId,
                        out ResidentRuntimeState runtime).Succeeded,
                    Is.True);
                Assert.That(
                    runtime.Memories.Entries.Any(entry =>
                        entry.SourceEventKind == WorldEventKind.TownEventCompleted &&
                        entry.Tags.Contains("attended")),
                    Is.False);
            }

            Assert.That(townEvents.IsInitialized, Is.True);
            Assert.That(saveService.Save().Succeeded, Is.True);
            Assert.That(storage.WriteCount, Is.EqualTo(2));
        }

        [Test]
        public void BuiltScene_SaveLoadDuringConversation_CancelsAndCanStartAgain()
        {
            BuiltSceneFixture fixture = BuildAndInitializeFixture();
            TownResidentScheduleController yaya = fixture.Resident(ResidentIds.Yaya);
            TownResidentScheduleController xiaosui = fixture.Resident(ResidentIds.Xiaosui);
            const double conversationGameSeconds = 17d * 60d * 60d;
            Assert.That(
                fixture.Bootstrap.Clock.Restore(
                    conversationGameSeconds,
                    fixture.Bootstrap.Clock.TimeScale,
                    false).Succeeded,
                Is.True);
            MoveResidentToScheduledActivity(yaya, 17 * 60, 1d);
            MoveResidentToScheduledActivity(xiaosui, 17 * 60, 1d);
            Assert.That(yaya.CurrentLocationId.Value, Is.EqualTo("location-plaza"));
            Assert.That(xiaosui.CurrentLocationId.Value, Is.EqualTo("location-plaza"));

            double firstMonotonicSeconds =
                UnityEngine.Time.realtimeSinceStartupAsDouble + 100d;
            ActionResult started = fixture.Social.TickSocial(
                0f,
                firstMonotonicSeconds,
                conversationGameSeconds);

            Assert.That(started.Succeeded, Is.True, started.Message);
            Assert.That(fixture.Social.ActiveSceneConversationCount, Is.EqualTo(1));
            Assert.That(fixture.Social.ConversationCoordinator.ActiveSessionCount, Is.EqualTo(1));
            Assert.That(fixture.Social.ParticipantLock.LockedResidentCount, Is.EqualTo(2));
            Assert.That(fixture.Schedules.ReservationService.ReservationCount, Is.EqualTo(2));
            Assert.That(yaya.IsConversationSuspended, Is.True);
            Assert.That(xiaosui.IsConversationSuspended, Is.True);
            Assert.That(
                fixture.Social.ParticipantLock.TryGetConversationId(
                    ResidentIds.Yaya,
                    out ConversationId firstConversationId),
                Is.True);
            Assert.That(
                fixture.Social.ConversationCoordinator.TryGetSession(
                    firstConversationId,
                    ResidentIds.Yaya,
                    out ConversationSession firstSession).Succeeded,
                Is.True);
            Assert.That(
                fixture.Social.ConversationCoordinator.PrepareLocalFallbackScript(
                    firstConversationId).Succeeded,
                Is.True);
            Assert.That(firstSession.PreparedLines, Has.Count.GreaterThanOrEqualTo(2));
            string firstUnplayedText = firstSession.PreparedLines[1].Text;
            ActionResult played = fixture.Social.TickSocial(
                1f,
                firstMonotonicSeconds + 0.1d,
                conversationGameSeconds + 0.1d);
            Assert.That(played.Succeeded, Is.True, played.Message);
            Assert.That(firstSession.State, Is.EqualTo(ConversationState.Active));
            Assert.That(firstSession.Utterances, Has.Count.EqualTo(1));
            string playedText = firstSession.Utterances[0].Text;
            Assert.That(playedText, Is.Not.EqualTo(firstUnplayedText));
            Assert.That(
                fixture.Social.SocialGraph.TryGetRelationship(
                    ResidentIds.Yaya,
                    ResidentIds.Xiaosui,
                    out RelationshipState relationshipBeforeLoad).Succeeded,
                Is.True);
            int savedFamiliarity = relationshipBeforeLoad.Familiarity;
            int savedTrust = relationshipBeforeLoad.Trust;
            long savedRelationshipVersion = relationshipBeforeLoad.RelationVersion;

            var storage = new RecordingSaveStorage();
            SaveGameService saveService = fixture.CreateSaveService(storage);
            ActionResult saved = saveService.Save();

            Assert.That(saved.Succeeded, Is.True, saved.Message);
            SaveData captured = JsonUtility.FromJson<SaveData>(storage.Json);
            foreach (ResidentId participant in new[] { ResidentIds.Yaya, ResidentIds.Xiaosui })
            {
                ResidentSaveData savedResident = captured.residents.Single(resident =>
                    resident.residentId == participant.Value);
                MemorySaveData[] fragments = savedResident.recentMemories.Where(memory =>
                    memory.hasSourceEventKind &&
                    memory.sourceEventKind == (int)WorldEventKind.ConversationInterrupted &&
                    memory.sourceEventId == firstConversationId.Value &&
                    memory.tags.Contains("conversation-fragment")).ToArray();
                Assert.That(fragments, Has.Length.EqualTo(1));
                Assert.That(fragments[0].ownerResidentId, Is.EqualTo(participant.Value));
                Assert.That(fragments[0].text, Does.Contain(playedText));
                Assert.That(fragments[0].text, Does.Not.Contain(firstUnplayedText));
                Assert.That(fragments[0].isShareable, Is.False);
            }

            Assert.That(storage.Json, Does.Not.Contain("ConversationSession"));
            Assert.That(storage.Json, Does.Not.Contain("participantLock"));
            Assert.That(storage.Json, Does.Not.Contain("reservation"));
            Assert.That(storage.Json, Does.Not.Contain("DeadlineAtMonotonicSeconds"));
            ActionResult loaded = saveService.Load();

            Assert.That(loaded.Succeeded, Is.True, loaded.Message);
            Assert.That(firstSession.State, Is.EqualTo(ConversationState.Cancelled));
            Assert.That(fixture.Social.ActiveSceneConversationCount, Is.Zero);
            Assert.That(fixture.Social.ConversationCoordinator.ActiveSessionCount, Is.Zero);
            Assert.That(fixture.Social.ParticipantLock.LockedResidentCount, Is.Zero);
            Assert.That(fixture.Schedules.ReservationService.ReservationCount, Is.Zero);
            Assert.That(yaya.IsConversationSuspended, Is.False);
            Assert.That(xiaosui.IsConversationSuspended, Is.False);
            Assert.That(yaya.Navigator.IsMoving, Is.False);
            Assert.That(xiaosui.Navigator.IsMoving, Is.False);
            Assert.That(yaya.Runtime.State, Is.EqualTo(ResidentScheduleState.WaitingForSchedule));
            Assert.That(xiaosui.Runtime.State, Is.EqualTo(ResidentScheduleState.WaitingForSchedule));
            foreach (ResidentId participant in new[] { ResidentIds.Yaya, ResidentIds.Xiaosui })
            {
                Assert.That(
                    fixture.Bootstrap.ResidentRegistry.TryGetRuntimeState(
                        participant,
                        out ResidentRuntimeState runtime).Succeeded,
                    Is.True);
                Assert.That(
                    runtime.Memories.Entries.Where(memory =>
                        memory.SourceEventKind == WorldEventKind.ConversationInterrupted &&
                        memory.SourceEventId == firstConversationId.Value &&
                        memory.Tags.Contains("conversation-fragment")),
                    Has.Exactly(1).Matches<MemoryEntry>(memory =>
                        memory.OwnerResidentId == participant &&
                        !memory.IsShareable &&
                        memory.Text.Contains(playedText) &&
                        !memory.Text.Contains(firstUnplayedText)),
                    "Only the actually played fragment may survive an interrupted conversation.");
                Assert.That(
                    runtime.Memories.Entries.Any(memory =>
                        memory.SourceEventKind == WorldEventKind.ConversationCompleted &&
                        memory.SourceEventId == firstConversationId.Value),
                    Is.False);
            }
            Assert.That(firstSession.Outcome, Is.Null);
            Assert.That(
                fixture.Social.SocialGraph.TryGetRelationship(
                    ResidentIds.Yaya,
                    ResidentIds.Xiaosui,
                    out RelationshipState relationshipAfterLoad).Succeeded,
                Is.True);
            Assert.That(relationshipAfterLoad.Familiarity, Is.EqualTo(savedFamiliarity));
            Assert.That(relationshipAfterLoad.Trust, Is.EqualTo(savedTrust));
            Assert.That(
                relationshipAfterLoad.RelationVersion,
                Is.EqualTo(savedRelationshipVersion));

            MoveResidentToScheduledActivity(yaya, 17 * 60, firstMonotonicSeconds + 1d);
            MoveResidentToScheduledActivity(xiaosui, 17 * 60, firstMonotonicSeconds + 1d);
            ActionResult restarted = fixture.Social.TickSocial(
                0f,
                firstMonotonicSeconds + 100d,
                conversationGameSeconds + 1d);

            Assert.That(restarted.Succeeded, Is.True, restarted.Message);
            Assert.That(fixture.Social.ActiveSceneConversationCount, Is.EqualTo(1));
            Assert.That(fixture.Social.ParticipantLock.LockedResidentCount, Is.EqualTo(2));
            Assert.That(
                fixture.Social.ParticipantLock.TryGetConversationId(
                    ResidentIds.Yaya,
                    out ConversationId replacementConversationId),
                Is.True);
            Assert.That(replacementConversationId, Is.Not.EqualTo(firstConversationId));

            fixture.Bootstrap.NotifyAuthoritativeStateResetting();
            Assert.That(fixture.Social.ActiveSceneConversationCount, Is.Zero);
            Assert.That(fixture.Social.ParticipantLock.LockedResidentCount, Is.Zero);
            Assert.That(fixture.Schedules.ReservationService.ReservationCount, Is.Zero);
        }

        [Test]
        public void BuiltScene_SaveLoadWhileBackgroundResidentMoves_ClearsAndReplans()
        {
            BuiltSceneFixture fixture = BuildAndInitializeFixture();
            TownResidentScheduleController amu = fixture.Resident(ResidentIds.Amu);
            const double workGameSeconds = 8d * 60d * 60d;
            Assert.That(
                fixture.Bootstrap.Clock.Restore(
                    workGameSeconds,
                    fixture.Bootstrap.Clock.TimeScale,
                    false).Succeeded,
                Is.True);
            Assert.That(
                amu.Navigator.Configure(
                    amu.GetComponent<NavMeshAgent>(),
                    fixture.ArrivalPoints,
                    requireNavMesh: false,
                    movementSpeed: 0.1f).Succeeded,
                Is.True);

            ActionResult moving = amu.TickSchedule(8 * 60, 0d, 0f);

            Assert.That(moving.Succeeded, Is.True, moving.Message);
            Assert.That(amu.Runtime.State, Is.EqualTo(ResidentScheduleState.Moving));
            Assert.That(amu.Navigator.IsMoving, Is.True);
            Assert.That(amu.Navigator.CurrentTarget, Is.Not.Null);
            Assert.That(amu.Runtime.ActiveInteractionPointId, Is.Not.Empty);
            string stalePointId = amu.Runtime.ActiveInteractionPointId;
            Vector3 savedPosition = amu.transform.position;
            Assert.That(fixture.Schedules.ReservationService.ReservationCount, Is.EqualTo(1));
            Assert.That(
                fixture.Schedules.ReservationService.TryGetOwner(
                    stalePointId,
                    0d,
                    out ResidentId movingOwner),
                Is.True);
            Assert.That(movingOwner, Is.EqualTo(ResidentIds.Amu));

            var storage = new RecordingSaveStorage();
            SaveGameService saveService = fixture.CreateSaveService(storage);
            Assert.That(saveService.Save().Succeeded, Is.True);

            ActionResult loaded = saveService.Load();

            Assert.That(loaded.Succeeded, Is.True, loaded.Message);
            Assert.That(amu.transform.position, Is.EqualTo(savedPosition));
            Assert.That(amu.Runtime.State, Is.EqualTo(ResidentScheduleState.WaitingForSchedule));
            Assert.That(amu.Runtime.ActiveEntry, Is.Null);
            Assert.That(amu.Runtime.ActiveInteractionPointId, Is.Empty);
            Assert.That(amu.Navigator.IsMoving, Is.False);
            Assert.That(amu.Navigator.CurrentTarget, Is.Null);
            Assert.That(fixture.Schedules.ReservationService.ReservationCount, Is.Zero);
            Assert.That(
                fixture.Schedules.ReservationService.TryGetOwner(
                    stalePointId,
                    1d,
                    out _),
                Is.False);

            Assert.That(
                amu.Navigator.Configure(
                    amu.GetComponent<NavMeshAgent>(),
                    fixture.ArrivalPoints,
                    requireNavMesh: false,
                    movementSpeed: 1000f).Succeeded,
                Is.True);
            ActionResult replanned = amu.TickSchedule(8 * 60, 2d, 1f);

            Assert.That(replanned.Succeeded, Is.True, replanned.Message);
            Assert.That(amu.Runtime.State, Is.EqualTo(ResidentScheduleState.Working));
            Assert.That(amu.CurrentLocationId.Value, Is.EqualTo("location-workshop"));
            Assert.That(amu.Runtime.ActiveInteractionPointId, Is.Not.Empty);
            Assert.That(amu.Navigator.IsMoving, Is.False);
            Assert.That(fixture.Schedules.ReservationService.ReservationCount, Is.EqualTo(1));
            Assert.That(
                fixture.Schedules.ReservationService.TryGetOwner(
                    amu.Runtime.ActiveInteractionPointId,
                    2d,
                    out ResidentId replannedOwner),
                Is.True);
            Assert.That(replannedOwner, Is.EqualTo(ResidentIds.Amu));
        }

        [Test]
        public void BuiltScene_LoadWithDestroyedConversationParticipant_CleansTransientState()
        {
            BuiltSceneFixture fixture = BuildAndInitializeFixture();
            TownResidentScheduleController yaya = fixture.Resident(ResidentIds.Yaya);
            TownResidentScheduleController xiaosui = fixture.Resident(ResidentIds.Xiaosui);
            const double conversationGameSeconds = 17d * 60d * 60d;
            Assert.That(
                fixture.Bootstrap.Clock.Restore(
                    conversationGameSeconds,
                    fixture.Bootstrap.Clock.TimeScale,
                    false).Succeeded,
                Is.True);
            MoveResidentToScheduledActivity(yaya, 17 * 60, 1d);
            MoveResidentToScheduledActivity(xiaosui, 17 * 60, 1d);
            double monotonicSeconds =
                UnityEngine.Time.realtimeSinceStartupAsDouble + 100d;
            ActionResult started = fixture.Social.TickSocial(
                0f,
                monotonicSeconds,
                conversationGameSeconds);

            Assert.That(started.Succeeded, Is.True, started.Message);
            Assert.That(fixture.Social.ActiveSceneConversationCount, Is.EqualTo(1));
            Assert.That(fixture.Social.ParticipantLock.LockedResidentCount, Is.EqualTo(2));
            Assert.That(fixture.Schedules.ReservationService.ReservationCount, Is.EqualTo(2));
            Assert.That(
                fixture.Social.ParticipantLock.TryGetConversationId(
                    ResidentIds.Yaya,
                    out ConversationId conversationId),
                Is.True);
            Assert.That(
                fixture.Social.ConversationCoordinator.TryGetSession(
                    conversationId,
                    ResidentIds.Yaya,
                    out ConversationSession session).Succeeded,
                Is.True);

            var storage = new RecordingSaveStorage();
            SaveGameService saveService = fixture.CreateSaveService(storage);
            Assert.That(saveService.Save().Succeeded, Is.True);

            Object.DestroyImmediate(xiaosui.gameObject);
            Assert.That(xiaosui == null, Is.True);
            ActionResult loaded = default;
            Assert.DoesNotThrow(() => loaded = saveService.Load());

            Assert.That(loaded.Succeeded, Is.True, loaded.Message);
            Assert.That(session.State, Is.EqualTo(ConversationState.Cancelled));
            Assert.That(fixture.Social.ActiveSceneConversationCount, Is.Zero);
            Assert.That(fixture.Social.ConversationCoordinator.ActiveSessionCount, Is.Zero);
            Assert.That(fixture.Social.ParticipantLock.LockedResidentCount, Is.Zero);
            Assert.That(fixture.Schedules.ReservationService.ReservationCount, Is.Zero);
            Assert.That(yaya.IsConversationSuspended, Is.False);
            Assert.That(yaya.IsFarmingBusy, Is.False);
            Assert.That(yaya.Navigator.IsMoving, Is.False);
            Assert.That(yaya.Runtime.State, Is.EqualTo(ResidentScheduleState.WaitingForSchedule));
            Assert.That(fixture.Executor.IsBusy, Is.False);
            foreach (ResidentId residentId in ResidentIds.TownResidents)
            {
                Assert.That(
                    fixture.Bootstrap.ResidentRegistry.TryGetRuntimeState(
                        residentId,
                        out ResidentRuntimeState runtime).Succeeded,
                    Is.True);
                Assert.That(runtime.ResidentId, Is.EqualTo(residentId));
                Assert.That(runtime.Memories.OwnerResidentId, Is.EqualTo(residentId));
                Assert.That(
                    runtime.Memories.Entries.All(memory =>
                        memory.OwnerResidentId == residentId),
                    Is.True,
                    $"Loaded memory ownership crossed into {residentId}.");
            }
        }

        [Test]
        public void BuiltScene_LoadDuringGatheringWithDestroyedLocation_CancelsWithoutLeak()
        {
            BuiltSceneFixture fixture = BuildAndInitializeFixture();
            const double eventStartGameSeconds = 18d * 60d * 60d;
            Assert.That(
                fixture.Bootstrap.Clock.Restore(
                    eventStartGameSeconds,
                    fixture.Bootstrap.Clock.TimeScale,
                    false).Succeeded,
                Is.True);
            MoveResidentToScheduledActivity(
                fixture.Resident(ResidentIds.Yaya),
                18 * 60,
                1d);
            MoveResidentToScheduledActivity(
                fixture.Resident(ResidentIds.Xiaosui),
                18 * 60,
                1d);
            TownEventSession session = ScheduleHarvestDinner(
                fixture,
                proposedAtGameSeconds: 17d * 60d * 60d);
            double monotonicSeconds =
                UnityEngine.Time.realtimeSinceStartupAsDouble + 100d;
            ActionResult gathering = fixture.TownEvents.TickTownEvent(
                0f,
                monotonicSeconds,
                session.ScheduledStartGameSeconds);

            Assert.That(gathering.Succeeded, Is.True, gathering.Message);
            Assert.That(session.State, Is.EqualTo(TownEventState.Gathering));
            Assert.That(fixture.Schedules.ReservationService.ReservationCount, Is.EqualTo(2));
            foreach (ResidentId participant in session.ParticipantResidentIds)
            {
                TownResidentScheduleController resident = fixture.Resident(participant);
                Assert.That(resident.IsTownEventSuspended, Is.True);
                Assert.That(resident.Navigator.IsMoving, Is.True);
            }

            var storage = new RecordingSaveStorage();
            SaveGameService saveService = fixture.CreateSaveService(storage);
            Assert.That(saveService.Save().Succeeded, Is.True);
            ResidentId invalidatedResidentId = session.ParticipantResidentIds.First();
            Assert.That(
                session.TryGetReservation(
                    invalidatedResidentId,
                    out InteractionPointReservation reservation),
                Is.True);
            LocationArrivalPoint invalidatedPoint = fixture.ArrivalPoints.Single(point =>
                point != null &&
                point.InteractionPointId == reservation.InteractionPointId);

            Object.DestroyImmediate(invalidatedPoint.gameObject);
                Assert.That(invalidatedPoint == null, Is.True);
            ActionResult loaded = saveService.Load();

            Assert.That(loaded.Succeeded, Is.True, loaded.Message);
            Assert.That(session.State, Is.EqualTo(TownEventState.Cancelled));
            Assert.That(fixture.TownEvents.EventCoordinator.ActiveTownEventCount, Is.Zero);
            Assert.That(fixture.Schedules.ReservationService.ReservationCount, Is.Zero);
            foreach (ResidentId participant in session.ParticipantResidentIds)
            {
                TownResidentScheduleController resident = fixture.Resident(participant);
                Assert.That(resident.IsTownEventSuspended, Is.False);
                Assert.That(resident.Navigator.IsMoving, Is.False);
                Assert.That(resident.Runtime.State, Is.EqualTo(ResidentScheduleState.WaitingForSchedule));
            }

            foreach (ResidentId residentId in ResidentIds.TownResidents)
            {
                Assert.That(
                    fixture.Bootstrap.ResidentRegistry.TryGetRuntimeState(
                        residentId,
                        out ResidentRuntimeState runtime).Succeeded,
                    Is.True);
                Assert.That(
                    runtime.Memories.Entries.Any(memory =>
                        memory.SourceEventKind == WorldEventKind.TownEventCompleted &&
                        memory.Tags.Contains("attended")),
                    Is.False,
                    $"{residentId} must not remember attending an interrupted event.");
            }
        }

        private static BuiltSceneFixture BuildAndInitializeFixture()
        {
            Assert.That(DemoSceneBuilder.BuildDemoScene(promptToSaveCurrentScenes: false), Is.True);
            GameBootstrap bootstrap = GameObject.Find("GameBootstrap").GetComponent<GameBootstrap>();
            TownScheduleCoordinator schedules =
                bootstrap.GetComponent<TownScheduleCoordinator>();
            TownSocialCoordinator social = bootstrap.GetComponent<TownSocialCoordinator>();
            TownEventSceneCoordinator townEvents =
                bootstrap.GetComponent<TownEventSceneCoordinator>();
            ReplanController replanner = bootstrap.GetComponent<ReplanController>();
            GameObject yayaObject = GameObject.Find("NPC_Blockout_Capsule");
            NpcPlanExecutor executor = yayaObject.GetComponent<NpcPlanExecutor>();
            TownResidentScheduleController[] residents =
                Object.FindObjectsByType<TownResidentScheduleController>();
            LocationArrivalPoint[] arrivalPoints =
                Object.FindObjectsByType<LocationArrivalPoint>();

            Assert.That(residents, Has.Length.EqualTo(4));
            Assert.That(arrivalPoints.Length, Is.GreaterThan(0));
            Assert.That(bootstrap.Initialize().Succeeded, Is.True);
            Assert.That(executor.Initialize().Succeeded, Is.True);
            Assert.That(replanner.Initialize().Succeeded, Is.True);
            Assert.That(schedules.Initialize().Succeeded, Is.True);
            Assert.That(social.Initialize().Succeeded, Is.True);
            Assert.That(townEvents.Initialize().Succeeded, Is.True);
            foreach (TownResidentScheduleController resident in residents)
            {
                Assert.That(
                    resident.Navigator.Configure(
                        resident.GetComponent<NavMeshAgent>(),
                        arrivalPoints,
                        requireNavMesh: false,
                        movementSpeed: 1000f).Succeeded,
                    Is.True);
            }

            return new BuiltSceneFixture(
                bootstrap,
                schedules,
                social,
                townEvents,
                replanner,
                executor,
                yayaObject.transform,
                residents,
                arrivalPoints);
        }

        private static void MoveResidentToScheduledActivity(
            TownResidentScheduleController resident,
            int minuteOfDay,
            double elapsedSeconds)
        {
            ActionResult moved = resident.TickSchedule(minuteOfDay, elapsedSeconds, 1f);
            Assert.That(moved.Succeeded, Is.True, moved.Message);
            Assert.That(resident.Runtime.State, Is.EqualTo(ResidentScheduleState.Working));
            Assert.That(resident.Navigator.IsMoving, Is.False);
        }

        private static TownEventSession ScheduleHarvestDinner(
            BuiltSceneFixture fixture,
            double proposedAtGameSeconds)
        {
            const string conversationId = "conversation:destroyed-event-location";
            const string rootFactId = "fact:destroyed-event-location-harvest";
            Assert.That(
                fixture.Bootstrap.ResidentRegistry.TryGetRuntimeState(
                    ResidentIds.Yaya,
                    out ResidentRuntimeState yaya).Succeeded,
                Is.True);
            Assert.That(
                fixture.Bootstrap.ResidentRegistry.TryGetRuntimeState(
                    ResidentIds.Xiaosui,
                    out ResidentRuntimeState xiaosui).Succeeded,
                Is.True);
            Assert.That(
                yaya.Memories.AddObservation(
                    ResidentIds.Yaya,
                    proposedAtGameSeconds - 2d,
                    "I harvested the carrots.",
                    9,
                    WorldEventKind.ActionCompleted,
                    MemorySourceKind.Perception,
                    "farm-event:destroyed-event-location",
                    rootFactId,
                    string.Empty,
                    default,
                    new[] { "carrot", "harvest" },
                    true,
                    out MemoryEntry sourceKnowledge).Succeeded,
                Is.True);
            Assert.That(
                xiaosui.Memories.AddObservation(
                    ResidentIds.Xiaosui,
                    proposedAtGameSeconds - 1d,
                    "Yaya told me about the carrot harvest.",
                    9,
                    WorldEventKind.ConversationCompleted,
                    MemorySourceKind.Conversation,
                    conversationId,
                    rootFactId,
                    sourceKnowledge.KnowledgeId,
                    ResidentIds.Yaya,
                    new[] { "carrot", "harvest" },
                    true,
                    out MemoryEntry receivedKnowledge).Succeeded,
                Is.True);
            var decision = new ResidentDecisionSpec(
                ResidentIds.Xiaosui,
                ResidentHighLevelIntents.ProposeTownEvent,
                null,
                "Let's make carrot soup tonight.",
                "Test");
            Assert.That(
                TownEventProposal.TryCreateFromDecision(
                    decision,
                    receivedKnowledge.KnowledgeId,
                    conversationId,
                    proposedAtGameSeconds,
                    out TownEventProposal proposal).Succeeded,
                Is.True);
            Assert.That(
                fixture.TownEvents.TrySubmitProposal(
                    proposal,
                    out TownEventSession session).Succeeded,
                Is.True);
            return session;
        }

        private sealed class BuiltSceneFixture
        {
            public BuiltSceneFixture(
                GameBootstrap bootstrap,
                TownScheduleCoordinator schedules,
                TownSocialCoordinator social,
                TownEventSceneCoordinator townEvents,
                ReplanController replanner,
                NpcPlanExecutor executor,
                Transform yayaTransform,
                TownResidentScheduleController[] residents,
                LocationArrivalPoint[] arrivalPoints)
            {
                Bootstrap = bootstrap;
                Schedules = schedules;
                Social = social;
                TownEvents = townEvents;
                Replanner = replanner;
                Executor = executor;
                YayaTransform = yayaTransform;
                Residents = residents;
                ArrivalPoints = arrivalPoints;
            }

            public GameBootstrap Bootstrap { get; }

            public TownScheduleCoordinator Schedules { get; }

            public TownSocialCoordinator Social { get; }

            public TownEventSceneCoordinator TownEvents { get; }

            public ReplanController Replanner { get; }

            public NpcPlanExecutor Executor { get; }

            public Transform YayaTransform { get; }

            public TownResidentScheduleController[] Residents { get; }

            public LocationArrivalPoint[] ArrivalPoints { get; }

            public TownResidentScheduleController Resident(ResidentId residentId)
            {
                return Residents.Single(resident => resident.ResidentId == residentId);
            }

            public SaveGameService CreateSaveService(ISaveGameStorage storage)
            {
                return new SaveGameService(
                    Bootstrap,
                    Executor,
                    Replanner,
                    YayaTransform,
                    storage);
            }
        }

        private static void AssertGeneratedScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            Assert.That(scene.path, Is.EqualTo(DemoSceneBuilder.ScenePath));
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoSceneBuilder.ScenePath), Is.Not.Null);

            AssertSingleRoot(scene, "Environment");
            AssertSingleRoot(scene, "Farm_3x3");
            AssertSingleRoot(scene, "NPC_Blockout_Capsule");
            AssertSingleRoot(scene, "DirectionalLight_Key");
            AssertSingleRoot(scene, "MainCamera_TopOblique");
            AssertSingleRoot(scene, "GameBootstrap");
            AssertSingleRoot(scene, "UI_Canvas");
            AssertSingleRoot(scene, "EventSystem_InputSystem");
            AssertSingleRoot(scene, "Navigation_NavMeshSurface");
            AssertSingleRoot(scene, "Town_Locations");
            AssertSingleRoot(scene, "Resident_Amu");
            AssertSingleRoot(scene, "Resident_Xiaosui");
            AssertSingleRoot(scene, "Resident_Momo");

            GameObject plotsRoot = GameObject.Find("Farm_3x3/Plots_1_to_9");
            Assert.That(plotsRoot, Is.Not.Null);
            Assert.That(plotsRoot.transform.childCount, Is.EqualTo(9));
            for (int plotNumber = 1; plotNumber <= 9; plotNumber++)
            {
                GameObject plot = GameObject.Find($"Farm_3x3/Plots_1_to_9/Plot_{plotNumber:00}");
                Assert.That(plot, Is.Not.Null);
                Assert.That(plot.GetComponent<NavMeshModifier>().ignoreFromBuild, Is.True);
                Assert.That(plot.GetComponent<PlotBlockoutView>(), Is.Not.Null);
                Assert.That(GameObject.Find($"Farm_3x3/Plot_Number_Labels/PlotLabel_{plotNumber:00}"), Is.Not.Null);

                GameObject interactionObject =
                    GameObject.Find($"Farm_3x3/Plot_Interaction_Points/InteractionPoint_{plotNumber:00}");
                Assert.That(interactionObject, Is.Not.Null);
                Assert.That(interactionObject.GetComponent<PlotInteractionPoint>().PlotNumber, Is.EqualTo(plotNumber));
            }

            GameObject cameraObject = GameObject.Find("MainCamera_TopOblique");
            Camera camera = cameraObject.GetComponent<Camera>();
            Assert.That(camera, Is.Not.Null);
            Assert.That(camera.orthographic, Is.True);

            Light light = GameObject.Find("DirectionalLight_Key").GetComponent<Light>();
            Assert.That(light, Is.Not.Null);
            Assert.That(light.type, Is.EqualTo(LightType.Directional));

            GameObject npc = GameObject.Find("NPC_Blockout_Capsule");
            Assert.That(npc.GetComponent<CapsuleCollider>(), Is.Not.Null);
            Assert.That(npc.GetComponent<NavMeshAgent>(), Is.Not.Null);
            Assert.That(npc.GetComponent<NpcNavigator>(), Is.Not.Null);
            Assert.That(npc.GetComponent<BlockoutActionFeedback>(), Is.Not.Null);
            NpcPlanExecutor planExecutor = npc.GetComponent<NpcPlanExecutor>();
            Assert.That(planExecutor, Is.Not.Null);
            Assert.That(planExecutor.ResidentId, Is.EqualTo(ResidentIds.Yaya));
            Assert.That(npc.transform.Find("NPC_Visual_Capsule"), Is.Not.Null);
            Assert.That(npc.transform.Find("ActionFeedback_ProgressBar"), Is.Not.Null);
            Transform dialogueBubble = npc.transform.Find("NPC_DialogueBubble");
            Assert.That(dialogueBubble, Is.Not.Null);
            Assert.That(dialogueBubble.GetComponent<NpcDialogueBubble>(), Is.Not.Null);
            Assert.That(dialogueBubble.Find("DialogueText"), Is.Not.Null);
            Assert.That(dialogueBubble.Find("EmojiText"), Is.Not.Null);
            Assert.That(dialogueBubble.Find("MoodText"), Is.Not.Null);

            string[] residentObjectNames =
            {
                "NPC_Blockout_Capsule",
                "Resident_Amu",
                "Resident_Xiaosui",
                "Resident_Momo"
            };
            var residentIds = new HashSet<ResidentId>();
            var residentColors = new HashSet<string>();
            foreach (string residentObjectName in residentObjectNames)
            {
                GameObject resident = GameObject.Find(residentObjectName);
                Assert.That(resident, Is.Not.Null);
                Assert.That(resident.GetComponent<NavMeshAgent>(), Is.Not.Null);
                Assert.That(resident.GetComponent<TownResidentNavigator>(), Is.Not.Null);
                TownResidentScheduleController scheduleController =
                    resident.GetComponent<TownResidentScheduleController>();
                Assert.That(scheduleController, Is.Not.Null);
                Assert.That(residentIds.Add(scheduleController.ResidentId), Is.True);
                ResidentBlockoutView residentView = resident.GetComponent<ResidentBlockoutView>();
                Assert.That(residentView, Is.Not.Null);
                Assert.That(residentView.NameLabel.text, Is.Not.Empty);
                Assert.That(residentView.StatusIcon.text, Is.Not.Empty);
                Assert.That(residentView.ConversationText, Is.Not.Null);
                Assert.That(
                    resident.transform.Find("Resident_Conversation_Label"),
                    Is.Not.Null);
                Renderer bodyRenderer = resident.transform
                    .Find("NPC_Visual_Capsule")
                    .GetComponent<Renderer>();
                residentColors.Add(ColorUtility.ToHtmlStringRGB(bodyRenderer.sharedMaterial.color));
            }

            Assert.That(residentIds, Has.Count.EqualTo(4));
            Assert.That(residentColors, Has.Count.EqualTo(4));
            Assert.That(GameObject.Find("Resident_Amu").GetComponent<NpcPlanExecutor>(), Is.Null);
            Assert.That(GameObject.Find("Resident_Xiaosui").GetComponent<NpcPlanExecutor>(), Is.Null);
            Assert.That(GameObject.Find("Resident_Momo").GetComponent<NpcPlanExecutor>(), Is.Null);

            string[] townObjectNames =
            {
                "Workshop_Blockout",
                "Cafeteria_Blockout",
                "Library_Blockout",
                "Plaza_Blockout",
                "Well_Blockout",
                "Home_Yaya_Blockout",
                "Home_Amu_Blockout",
                "Home_Xiaosui_Blockout",
                "Home_Momo_Blockout"
            };
            foreach (string townObjectName in townObjectNames)
            {
                GameObject location = GameObject.Find($"Town_Locations/{townObjectName}");
                Assert.That(location, Is.Not.Null);
                Assert.That(location.transform.Find("Location_Label"), Is.Not.Null);
                Transform arrivalRoot = location.transform.Find("Arrival_Points");
                Assert.That(arrivalRoot, Is.Not.Null);
                Assert.That(arrivalRoot.childCount, Is.EqualTo(4));
                for (int index = 0; index < arrivalRoot.childCount; index++)
                {
                    Assert.That(
                        arrivalRoot.GetChild(index).GetComponent<LocationArrivalPoint>(),
                        Is.Not.Null);
                }
            }

            GameObject conversationAnchorObject = GameObject.Find(
                "Town_Locations/Plaza_Blockout/Conversation_Anchors");
            Assert.That(conversationAnchorObject, Is.Not.Null);
            Transform conversationAnchorRoot = conversationAnchorObject.transform;
            Assert.That(conversationAnchorRoot.childCount, Is.EqualTo(2));
            var conversationAnchorIds = new HashSet<string>();
            var conversationPointIds = new HashSet<string>();
            for (int index = 0; index < conversationAnchorRoot.childCount; index++)
            {
                ConversationAnchor anchor = conversationAnchorRoot
                    .GetChild(index)
                    .GetComponent<ConversationAnchor>();
                Assert.That(anchor, Is.Not.Null);
                Assert.That(conversationAnchorIds.Add(anchor.AnchorId), Is.True);
                Assert.That(anchor.LocationId.Value, Is.EqualTo("location-plaza"));
                Assert.That(anchor.FirstStandPoint, Is.Not.Null);
                Assert.That(anchor.SecondStandPoint, Is.Not.Null);
                Assert.That(
                    conversationPointIds.Add(anchor.FirstStandPoint.InteractionPointId),
                    Is.True);
                Assert.That(
                    conversationPointIds.Add(anchor.SecondStandPoint.InteractionPointId),
                    Is.True);
            }

            Assert.That(conversationAnchorIds, Has.Count.EqualTo(2));
            Assert.That(conversationPointIds, Has.Count.EqualTo(4));

            NavMeshSurface navMeshSurface =
                GameObject.Find("Navigation_NavMeshSurface").GetComponent<NavMeshSurface>();
            Assert.That(navMeshSurface, Is.Not.Null);
            Assert.That(navMeshSurface.navMeshData, Is.Not.Null);
            GameObject uiCanvas = GameObject.Find("UI_Canvas");
            Assert.That(uiCanvas.GetComponent<Canvas>(), Is.Not.Null);
            Assert.That(
                uiCanvas.GetComponent<DemoHud>().SelectedResidentId,
                Is.EqualTo(ResidentIds.Yaya));
            Assert.That(GameObject.Find("UI_Canvas/StatusPanel/TimeText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/StatusPanel/MoodText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/StatusPanel/EmojiText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/StatusPanel/AiModeText"), Is.Not.Null);
            Assert.That(
                GameObject.Find("UI_Canvas/StatusPanel/TimeControls/PauseButton").GetComponent<Button>(),
                Is.Not.Null);
            Assert.That(
                GameObject.Find("UI_Canvas/StatusPanel/TimeControls/Speed1Button").GetComponent<Button>(),
                Is.Not.Null);
            Assert.That(
                GameObject.Find("UI_Canvas/StatusPanel/TimeControls/Speed5Button").GetComponent<Button>(),
                Is.Not.Null);
            Assert.That(
                GameObject.Find("UI_Canvas/StatusPanel/TimeControls/Speed20Button").GetComponent<Button>(),
                Is.Not.Null);
            Assert.That(
                GameObject.Find("UI_Canvas/StatusPanel/TimeControls/ApiSettingsButton").GetComponent<Button>(),
                Is.Not.Null);
            Transform apiSetupPanel = uiCanvas.transform.Find("ApiGatewaySetupPanel");
            Assert.That(apiSetupPanel, Is.Not.Null);
            Assert.That(apiSetupPanel.gameObject.activeSelf, Is.False);
            InputField apiKeyInput = apiSetupPanel.Find("ApiKeyInput").GetComponent<InputField>();
            InputField modelInput = apiSetupPanel.Find("ModelInput").GetComponent<InputField>();
            Assert.That(apiKeyInput, Is.Not.Null);
            Assert.That(apiKeyInput.contentType, Is.EqualTo(InputField.ContentType.Password));
            Assert.That(apiKeyInput.text, Is.Empty);
            Assert.That(modelInput, Is.Not.Null);
            Assert.That(modelInput.text, Is.EqualTo(LocalAiGatewayProcess.DefaultModelId));
            Assert.That(apiSetupPanel.Find("StartGatewayButton").GetComponent<Button>(), Is.Not.Null);
            Assert.That(apiSetupPanel.Find("StopGatewayButton").GetComponent<Button>(), Is.Not.Null);
            Assert.That(apiSetupPanel.Find("GatewayStatusText").GetComponent<Text>(), Is.Not.Null);
            Assert.That(uiCanvas.GetComponent<ApiGatewaySetupPanel>(), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/BackpackPanel/InventoryText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/GoalPanel/GoalText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/GoalPanel/ActionText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/GoalPanel/ActionReasonText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/GoalPanel/ExpressionText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/WorldEventsPanel/WorldEventsText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/MemoryPanel/PersonaText"), Is.Not.Null);
            Assert.That(
                GameObject.Find("UI_Canvas/MemoryPanel/ResidentSelector/YayaButton")
                    .GetComponent<Button>(),
                Is.Not.Null);
            Assert.That(
                GameObject.Find("UI_Canvas/MemoryPanel/ResidentSelector/AmuButton")
                    .GetComponent<Button>(),
                Is.Not.Null);
            Assert.That(
                GameObject.Find("UI_Canvas/MemoryPanel/ResidentSelector/XiaosuiButton")
                    .GetComponent<Button>(),
                Is.Not.Null);
            Assert.That(
                GameObject.Find("UI_Canvas/MemoryPanel/ResidentSelector/MomoButton")
                    .GetComponent<Button>(),
                Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/MemoryPanel/RecentMemoriesText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/MemoryPanel/RecentReflectionsText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/CommandPanel/CommandInput").GetComponent<InputField>(), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/CommandPanel/SubmitButton").GetComponent<Button>(), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/SaveControlsPanel/SaveButton").GetComponent<Button>(), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/SaveControlsPanel/LoadButton").GetComponent<Button>(), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/SaveControlsPanel/NewDemoButton").GetComponent<Button>(), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/SaveControlsPanel/SaveStatusText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas").GetComponent<SaveGameController>(), Is.Not.Null);

            GameBootstrap bootstrap = GameObject.Find("GameBootstrap").GetComponent<GameBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.SceneConfig, Is.Not.Null);
            Assert.That(bootstrap.GetComponent<ReplanController>(), Is.Not.Null);
            Assert.That(bootstrap.GetComponent<TownScheduleCoordinator>(), Is.Not.Null);
            Assert.That(bootstrap.GetComponent<TownSocialCoordinator>(), Is.Not.Null);
            Assert.That(bootstrap.GetComponent<TownEventSceneCoordinator>(), Is.Not.Null);
            Assert.That(bootstrap.GetComponent<LocalAiGatewayProcess>(), Is.Not.Null);

            DemoInventoryConfig inventoryConfig =
                AssetDatabase.LoadAssetAtPath<DemoInventoryConfig>(DemoSceneBuilder.InventoryConfigPath);
            Assert.That(inventoryConfig, Is.Not.Null);
            Assert.That(inventoryConfig.CarrotSeeds, Is.EqualTo(9));
            Assert.That(inventoryConfig.Water, Is.GreaterThanOrEqualTo(9));
            Assert.That(inventoryConfig.Fertilizer, Is.EqualTo(9));
            Assert.That(inventoryConfig.Carrots, Is.Zero);

            DemoSceneConfig sceneConfig =
                AssetDatabase.LoadAssetAtPath<DemoSceneConfig>(DemoSceneBuilder.SceneConfigPath);
            Assert.That(sceneConfig, Is.Not.Null);
            Assert.That(sceneConfig.InventoryConfig, Is.SameAs(inventoryConfig));
            Assert.That(sceneConfig.TimeScale, Is.EqualTo(20f));
            Assert.That(sceneConfig.ExpressionCooldownSeconds, Is.GreaterThan(0f));
            Assert.That(sceneConfig.ExpressionDisplaySeconds, Is.GreaterThan(0f));
            Assert.That(sceneConfig.AiGatewayBaseUrl, Is.Not.Empty);
            Assert.That(sceneConfig.AiRequestTimeoutSeconds, Is.InRange(1, 3));
            Assert.That(sceneConfig.AiGatewayMode, Is.EqualTo(AiGatewayMode.Remote));
            Assert.That(sceneConfig.CreateDemoMode().IsAiServiceRequired, Is.False);

            Assert.That(
                AssetDatabase.FindAssets("t:DemoInventoryConfig", new[] { "Assets/AIFarm/Config" }),
                Has.Length.EqualTo(1));
            Assert.That(
                AssetDatabase.FindAssets("t:DemoSceneConfig", new[] { "Assets/AIFarm/Config" }),
                Has.Length.EqualTo(1));
            Assert.That(
                AssetDatabase.FindAssets(
                    "t:ResidentDefinitionAsset",
                    new[] { DemoSceneBuilder.ResidentConfigFolder }),
                Has.Length.EqualTo(4));
            Assert.That(
                AssetDatabase.FindAssets(
                    "t:DailyScheduleDefinitionAsset",
                    new[] { DemoSceneBuilder.ScheduleConfigFolder }),
                Has.Length.EqualTo(4));
            Assert.That(
                AssetDatabase.FindAssets(
                    "t:TownLocationDefinitionAsset",
                    new[] { DemoSceneBuilder.LocationConfigFolder }),
                Has.Length.EqualTo(9));
            Assert.That(
                AssetDatabase.LoadAssetAtPath<GameObject>(DemoSceneBuilder.ResidentPrefabPath),
                Is.Not.Null);
        }

        private static void AssertSingleRoot(Scene scene, string expectedName)
        {
            int matches = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == expectedName)
                {
                    matches++;
                }
            }

            Assert.That(matches, Is.EqualTo(1), $"Expected exactly one root named {expectedName}.");
        }

        private static void InvokeLifecycle(MonoBehaviour component, string methodName)
        {
            MethodInfo method = component.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing lifecycle method {methodName}.");
            method.Invoke(component, null);
        }

        private sealed class RecordingSaveStorage : ISaveGameStorage
        {
            public string SavePath => "memory://town-event-save-guard";

            public int WriteCount { get; private set; }

            public string Json { get; private set; } = string.Empty;

            public ActionResult Write(string json)
            {
                Json = json ?? string.Empty;
                WriteCount++;
                return ActionResult.Success("Recorded save JSON.");
            }

            public ActionResult Read(out string json)
            {
                json = Json;
                return string.IsNullOrWhiteSpace(json)
                    ? ActionResult.Failure(ActionFailureReason.InvalidState, "No save exists.")
                    : ActionResult.Success("Read save JSON.");
            }

            public ActionResult Delete()
            {
                Json = string.Empty;
                return ActionResult.Success("Deleted save JSON.");
            }
        }
    }
}
