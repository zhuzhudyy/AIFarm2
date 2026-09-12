using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Npc;
using AIFarm.Presentation;
using AIFarm.Social;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AIFarm.Tests.PlayMode
{
    public sealed class TownLifeIntegrationPlayModeTests
    {
        [UnityTearDown]
        public IEnumerator UnloadOnlyTheDemoSceneOwnedByThisFixture()
        {
            Scene demo = SceneManager.GetSceneByName("DemoScene");
            if (!demo.IsValid() || !demo.isLoaded) yield break;
            Scene cleanup = SceneManager.CreateScene("TownLifeTestCleanup-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(cleanup);
            yield return SceneManager.UnloadSceneAsync(demo);
        }

        [UnityTest]
        public IEnumerator ProductionScene_AllResidentsHaveDistinctExecutorsAndAutonomousActions()
        {
            yield return SceneManager.LoadSceneAsync("DemoScene", LoadSceneMode.Single);
            yield return null;
            GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.LifeControllers.Count, Is.EqualTo(4));
            var owners = new HashSet<ResidentId>();
            foreach (TownLifeController resident in bootstrap.LifeControllers.Values)
            {
                Assert.That(resident.IsInitialized, Is.True);
                NpcPlanExecutor executor = resident.GetComponent<NpcPlanExecutor>();
                Assert.That(executor.IsInitialized, Is.True);
                Assert.That(executor.ResidentId, Is.EqualTo(resident.ResidentId));
                Assert.That(owners.Add(executor.ResidentId), Is.True);
            }
            double deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 15;
            var activeOwners = new HashSet<ResidentId>();
            while (UnityEngine.Time.realtimeSinceStartupAsDouble < deadline && activeOwners.Count < 4)
            {
                foreach (TownLifeController resident in bootstrap.LifeControllers.Values)
                    if (resident.IsBusy || resident.GetComponent<TownResidentScheduleController>().IsConversationSuspended)
                        activeOwners.Add(resident.ResidentId);
                yield return null;
            }
            Assert.That(activeOwners.Count, Is.EqualTo(4), "Each registered resident must actually enter an action without player input.");
        }

        [UnityTest]
        public IEnumerator SubmittedCommandsRemainBoundWhenSelectionChanges_AllResidentsFishPickAndCancel()
        {
            yield return SceneManager.LoadSceneAsync("DemoScene", LoadSceneMode.Single);
            yield return null;
            GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            DisableGatewayConfigurationLoop();
            foreach (TownLifeController resident in bootstrap.LifeControllers.Values) resident.StopTask();
            var transport = new ControlledModelTransport();
            bootstrap.AiRequests.ReplaceClient(new RemoteAiGatewayClient("http://contract.test", 30, transport));
            DemoHud hud = UnityEngine.Object.FindFirstObjectByType<DemoHud>();
            Assert.That(hud, Is.Not.Null);
            var fishOwners = new HashSet<ResidentId>();
            var fruitOwners = new HashSet<ResidentId>();
            bootstrap.Events.EntryRecorded += entry =>
            {
                if (!entry.ActorResidentId.HasValue || !entry.Tags.Contains("life-activity")) return;
                if (entry.Tags.Contains("fish")) fishOwners.Add(entry.ActorResidentId.Value);
                if (entry.Tags.Contains("fruit")) fruitOwners.Add(entry.ActorResidentId.Value);
            };
            foreach (ResidentId owner in ResidentIds.TownResidents)
            {
                Assert.That(hud.SelectResident(owner).Succeeded, Is.True);
                Assert.That(hud.SubmitCommand("钓鱼").Succeeded, Is.True);
                Assert.That(hud.SelectResident(owner == ResidentIds.Momo ? ResidentIds.Yaya : ResidentIds.Momo).Succeeded, Is.True);
            }
            double deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 65;
            while (UnityEngine.Time.realtimeSinceStartupAsDouble < deadline && fishOwners.Count < 4)
            { bootstrap.Simulation.Advance(.1); yield return null; }
            Assert.That(fishOwners.Count, Is.EqualTo(4), string.Join("\n", bootstrap.LifeControllers.Values.Select(life => life.Diagnostics)));
            foreach (ResidentId owner in ResidentIds.TownResidents)
                Assert.That(transport.CommandOwners.Contains(owner.Value), Is.True, "Transport did not receive the owner captured at HUD submission.");
            foreach (ResidentId owner in ResidentIds.TownResidents)
            {
                Assert.That(bootstrap.SubmitResidentCommand(owner, "摘果").Succeeded, Is.True);
            }
            deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 65;
            while (UnityEngine.Time.realtimeSinceStartupAsDouble < deadline && fruitOwners.Count < 4)
            { bootstrap.Simulation.Advance(.1); yield return null; }
            Assert.That(fruitOwners.Count, Is.EqualTo(4), string.Join("\n", bootstrap.LifeControllers.Values.Select(life => life.Diagnostics)));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.Fish), Is.GreaterThan(0));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.Fruit), Is.GreaterThan(0));
            foreach (ResidentId owner in ResidentIds.TownResidents)
                Assert.That(bootstrap.SubmitResidentCommand(owner, "持续钓鱼").Succeeded, Is.True);
            yield return new WaitForSecondsRealtime(2);
            foreach (ResidentId owner in ResidentIds.TownResidents)
                Assert.That(bootstrap.SubmitResidentCommand(owner, "停止").Succeeded, Is.True);
            Assert.That(bootstrap.Activities.IsAvailable("fishing-1"), Is.True);
            Assert.That(bootstrap.Activities.IsAvailable("fishing-2"), Is.True);
            Assert.That(bootstrap.LifeControllers.Values.All(life => !life.HasPlayerTask), Is.True);
        }

        [UnityTest]
        public IEnumerator ControlledRemoteHighLevelDecisionActuallyReachesExecutorAndProducesResources()
        {
            yield return SceneManager.LoadSceneAsync("DemoScene", LoadSceneMode.Single);
            yield return null;
            GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            DisableGatewayConfigurationLoop();
            var transport = new ControlledModelTransport();
            bootstrap.AiRequests.ReplaceClient(new RemoteAiGatewayClient("http://contract.test", 30, transport));
            foreach (TownLifeController life in bootstrap.LifeControllers.Values)
            { life.StopTask(); life.RefreshGateway(bootstrap.GatewayConfigurationVersion, true, ""); }
            double deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 55;
            bool applied = false;
            while (UnityEngine.Time.realtimeSinceStartupAsDouble < deadline)
            {
                // A stable daytime observation window; rendering speed must not make the test
                // jump into night before a real-time model cooldown has elapsed.
                bootstrap.Simulation.Advance(UnityEngine.Time.unscaledDeltaTime * 2d);
                applied |= bootstrap.LifeControllers.Values.Any(life => life.RemoteDecisionExecutionCount > 0);
                if (applied && bootstrap.Inventory.GetCount(InventoryItem.Fish) > 0) break;
                yield return null;
            }
            Assert.That(transport.Decisions, Is.GreaterThan(0));
            Assert.That(applied, Is.True, "Controlled remote decision must be consumed by the actual life executor.\n" +
                string.Join("\n", bootstrap.LifeControllers.Values.Select(life => life.Diagnostics)));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.Fish), Is.GreaterThan(0));
        }

        private static void DisableGatewayConfigurationLoop()
        {
            foreach (GatewayConnectionController connection in UnityEngine.Object.FindObjectsByType<GatewayConnectionController>(FindObjectsSortMode.None))
            { connection.StopAllCoroutines(); connection.enabled = false; }
        }

        [UnityTest]
        public IEnumerator FertilizerShortageUsesExistingWeedsBeforeWellAndResumesPlayerTask()
        {
            yield return SceneManager.LoadSceneAsync("DemoScene", LoadSceneMode.Single);
            yield return null;
            GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            DisableGatewayConfigurationLoop();
            bootstrap.AiRequests.ReplaceClient(new LocalAiGatewayClient());
            foreach (TownLifeController resident in bootstrap.LifeControllers.Values)
            {
                resident.StopTask();
                if (resident.ResidentId != ResidentIds.Yaya)
                { resident.enabled = false; resident.GetComponent<TownResidentScheduleController>().enabled = false; }
            }
            foreach (FarmPlot plot in bootstrap.Field.Plots)
                Assert.That(plot.RestoreState(PlotState.Empty, null, 0, false, false, false, 0).Succeeded, Is.True);
            Assert.That(bootstrap.Inventory.RestoreCounts(0, 9, 0, 0).Succeeded, Is.True);
            Assert.That(bootstrap.Field.GetPlot(1).RestoreState(PlotState.Growing, CropType.Carrot, 2, false, false, false, 0).Succeeded, Is.True);
            Assert.That(bootstrap.Field.GetPlot(9).RestoreState(PlotState.Growing, CropType.Carrot, 2, true, true, false, 10).Succeeded, Is.True);
            TownLifeController worker = bootstrap.LifeControllers[ResidentIds.Yaya];
            Assert.That(bootstrap.SubmitResidentCommand(ResidentIds.Yaya, "持续照料1号地和9号地").Succeeded, Is.True);
            double deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 35;
            bool weeded = false;
            while (UnityEngine.Time.realtimeSinceStartupAsDouble < deadline && !bootstrap.Field.GetPlot(1).IsFertilized)
            {
                weeded |= bootstrap.Field.GetPlot(9).HasBeenWeeded;
                yield return null;
            }
            Assert.That(weeded, Is.True, "The resident must produce compost before visiting an empty supply well.");
            Assert.That(bootstrap.Field.GetPlot(1).IsFertilized, Is.True, worker.Diagnostics);
            Assert.That(worker.HasPlayerTask, Is.True, "A recoverable supply wait must keep the loop task.");
        }

        [UnityTest]
        public IEnumerator FarmNavigationFailureCannotStrandBusyExecutor_RecoveryCompletesSubmittedTask()
        {
            yield return SceneManager.LoadSceneAsync("DemoScene", LoadSceneMode.Single);
            yield return null;
            GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            ConfigureLocalRecoveryScene(bootstrap);
            TownLifeController worker = bootstrap.LifeControllers[ResidentIds.Yaya];
            Assert.That(bootstrap.Field.GetPlot(1).RestoreState(PlotState.Empty, null, 0, false, false, false, 0).Succeeded, Is.True);
            Assert.That(bootstrap.Inventory.RestoreCounts(9, 9, 9, 0).Succeeded, Is.True);
            NavMeshAgent agent = worker.GetComponent<NavMeshAgent>();
            NpcPlanExecutor executor = worker.GetComponent<NpcPlanExecutor>();
            bool failed = false;
            executor.ActionFailed += (action, outcome) => { failed = true; agent.enabled = true; };
            agent.enabled = false;
            LogAssert.Expect(LogType.Warning, new Regex("NPC action .* failed:.*NavMesh"));
            Assert.That(bootstrap.SubmitResidentCommand(ResidentIds.Yaya, "播种1号地").Succeeded, Is.True);
            double deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 25;
            while (UnityEngine.Time.realtimeSinceStartupAsDouble < deadline &&
                (!failed || bootstrap.Field.GetPlot(1).State != PlotState.Growing || worker.HasPlayerTask)) yield return null;
            Assert.That(failed, Is.True, "The fixture must reproduce an actual NavMesh precondition failure.");
            Assert.That(executor.Status, Is.Not.EqualTo(NpcExecutionStatus.Failed), worker.Diagnostics);
            Assert.That(bootstrap.Field.GetPlot(1).State, Is.EqualTo(PlotState.Growing));
            Assert.That(worker.HasPlayerTask, Is.False, worker.Diagnostics);
        }

        [UnityTest]
        public IEnumerator UnreachableMoveHasBoundedRetriesAndReleasesTaskOwnership()
        {
            yield return SceneManager.LoadSceneAsync("DemoScene", LoadSceneMode.Single);
            yield return null;
            GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            ConfigureLocalRecoveryScene(bootstrap);
            TownLifeController worker = bootstrap.LifeControllers[ResidentIds.Yaya];
            worker.GetComponent<NavMeshAgent>().enabled = false;
            Assert.That(bootstrap.SubmitResidentCommand(ResidentIds.Yaya, "前往广场").Succeeded, Is.True);
            double deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 18;
            while (UnityEngine.Time.realtimeSinceStartupAsDouble < deadline && worker.HasPlayerTask) yield return null;
            Assert.That(worker.HasPlayerTask, Is.False, worker.Diagnostics);
            Assert.That(worker.LastError, Does.Contain("连续失败三次"));
            Assert.That(worker.IsBusy, Is.False);
            worker.GetComponent<NavMeshAgent>().enabled = true;
        }

        [UnityTest]
        public IEnumerator ResidentLeavingAfterConversationArrivalReleasesBothParticipantsImmediately()
        {
            yield return SceneManager.LoadSceneAsync("DemoScene", LoadSceneMode.Single);
            yield return null;
            GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            DisableGatewayConfigurationLoop();
            bootstrap.AiRequests.ReplaceClient(new LocalAiGatewayClient());
            foreach (TownLifeController life in bootstrap.LifeControllers.Values)
            {
                life.StopTask();
                if (life.ResidentId != ResidentIds.Yaya && life.ResidentId != ResidentIds.Amu)
                { life.enabled = false; life.GetComponent<TownResidentScheduleController>().enabled = false; }
            }
            yield return null;
            TownSocialCoordinator coordinator = UnityEngine.Object.FindFirstObjectByType<TownSocialCoordinator>();
            Assert.That(coordinator.RequestConversation(ResidentIds.Yaya, ResidentIds.Amu).Succeeded, Is.True);
            Assert.That(coordinator.ConversationCoordinator.TryGetSession(
                new ConversationId(coordinator.GetConversationId(ResidentIds.Yaya)), ResidentIds.Yaya, out ConversationSession session).Succeeded, Is.True);
            double deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 25;
            var first = bootstrap.LifeControllers[ResidentIds.Yaya];
            var second = bootstrap.LifeControllers[ResidentIds.Amu];
            while (UnityEngine.Time.realtimeSinceStartupAsDouble < deadline &&
                (first.GetComponent<TownResidentNavigator>().IsMoving || second.GetComponent<TownResidentNavigator>().IsMoving)) yield return null;
            Assert.That(first.GetComponent<TownResidentNavigator>().IsMoving, Is.False);
            Assert.That(second.GetComponent<TownResidentNavigator>().IsMoving, Is.False);
            Assert.That(coordinator.ParticipantLock.IsLocked(ResidentIds.Amu), Is.True,
                $"Conversation must have actually arrived before testing departure. State={session.State} EndReason={session.EndReason} Error={coordinator.LastConversationError}");
            first.gameObject.SetActive(false);
            yield return null;
            yield return null;
            Assert.That(coordinator.ParticipantLock.IsLocked(ResidentIds.Yaya), Is.False);
            Assert.That(coordinator.ParticipantLock.IsLocked(ResidentIds.Amu), Is.False);
            Assert.That(second.GetComponent<TownResidentScheduleController>().IsConversationSuspended, Is.False);
            Assert.That(coordinator.GetConversationId(ResidentIds.Amu), Is.Empty);
        }

        private static void ConfigureLocalRecoveryScene(GameBootstrap bootstrap)
        {
            DisableGatewayConfigurationLoop();
            bootstrap.AiRequests.ReplaceClient(new LocalAiGatewayClient());
            foreach (TownLifeController life in bootstrap.LifeControllers.Values)
            {
                life.StopTask();
                if (life.ResidentId != ResidentIds.Yaya)
                { life.enabled = false; life.GetComponent<TownResidentScheduleController>().enabled = false; }
            }
        }

        [UnityTest]
        public IEnumerator PlayerChatTaskCompletesOnlyAfterTheActualConversation()
        {
            yield return SceneManager.LoadSceneAsync("DemoScene", LoadSceneMode.Single);
            yield return null;
            GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            TownSocialCoordinator coordinator = ConfigurePlayerChatScene(bootstrap);
            TownLifeController worker = bootstrap.LifeControllers[ResidentIds.Yaya];
            yield return SubmitAndWaitForPlayerChat(bootstrap, coordinator, worker);
            Assert.That(coordinator.ConversationCoordinator.TryGetSession(
                new ConversationId(coordinator.GetConversationId(ResidentIds.Yaya)), ResidentIds.Yaya, out ConversationSession session).Succeeded, Is.True);
            Assert.That(session.IsTerminal, Is.False);
            Assert.That(worker.HasPlayerTask, Is.True, "An accepted invitation is not a completed Chat task.");
            double deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 35;
            while (worker.HasPlayerTask && UnityEngine.Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(session.State, Is.EqualTo(ConversationState.Completed), coordinator.LastConversationError);
            Assert.That(session.Utterances.Count, Is.EqualTo(2));
            Assert.That(worker.HasPlayerTask, Is.False, worker.Diagnostics);
            Assert.That(worker.TaskState, Does.Contain("任务完成"));
            Assert.That(worker.LastError, Is.Empty);
        }

        [UnityTest]
        public IEnumerator InterruptedPlayerChatFailsInsteadOfReportingCompletion()
        {
            yield return SceneManager.LoadSceneAsync("DemoScene", LoadSceneMode.Single);
            yield return null;
            GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            TownSocialCoordinator coordinator = ConfigurePlayerChatScene(bootstrap);
            TownLifeController worker = bootstrap.LifeControllers[ResidentIds.Yaya];
            yield return SubmitAndWaitForPlayerChat(bootstrap, coordinator, worker);
            coordinator.CancelResidentConversation(ResidentIds.Amu);
            InvokePrivate(worker, "TickLife");
            Assert.That(worker.HasPlayerTask, Is.False);
            Assert.That(worker.TaskState, Does.Contain("任务失败"));
            Assert.That(worker.LastError, Does.Contain("交谈中断"));
            Assert.That(coordinator.ParticipantLock.IsLocked(ResidentIds.Yaya), Is.False);
            Assert.That(coordinator.ParticipantLock.IsLocked(ResidentIds.Amu), Is.False);
        }

        [UnityTest]
        public IEnumerator RestoredChatHasNoStaleSessionAndStoppingItCannotCompleteNewTask()
        {
            yield return SceneManager.LoadSceneAsync("DemoScene", LoadSceneMode.Single);
            yield return null;
            GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            TownSocialCoordinator coordinator = ConfigurePlayerChatScene(bootstrap);
            TownLifeController worker = bootstrap.LifeControllers[ResidentIds.Yaya];
            yield return SubmitAndWaitForPlayerChat(bootstrap, coordinator, worker);
            Assert.That(coordinator.ConversationCoordinator.TryGetSession(
                new ConversationId(coordinator.GetConversationId(ResidentIds.Yaya)), ResidentIds.Yaya, out ConversationSession previous).Succeeded, Is.True);
            ResidentTaskSnapshot restored = JsonUtility.FromJson<ResidentTaskSnapshot>(JsonUtility.ToJson(worker.CaptureTask()));
            Assert.That(worker.RestoreTask(restored).Succeeded, Is.True);
            Assert.That(previous.State, Is.EqualTo(ConversationState.Cancelled));
            Assert.That(coordinator.GetConversationId(ResidentIds.Yaya), Is.Empty);
            InvokePrivate(worker, "TickLife");
            Assert.That(worker.HasPlayerTask, Is.True, "Restored chat must restart its invitation, not consume the old cancelled terminal state.");
            Assert.That(bootstrap.SubmitResidentCommand(ResidentIds.Yaya, "停止").Succeeded, Is.True);
            Assert.That(bootstrap.SubmitResidentCommand(ResidentIds.Yaya, "钓鱼3次").Succeeded, Is.True);
            double deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 5;
            while (UnityEngine.Time.realtimeSinceStartupAsDouble < deadline &&
                worker.CaptureTask().task?.task_type != "Fish") yield return null;
            ResidentTaskSpec current = worker.CaptureTask().task;
            Assert.That(current, Is.Not.Null, worker.Diagnostics);
            Assert.That(current.task_type, Is.EqualTo("Fish"));
            Assert.That(current.quantity, Is.EqualTo(3));
            Assert.That(current.task_id, Is.Not.EqualTo(restored.task.task_id));
            InvokePrivate(worker, "TickLife");
            Assert.That(worker.HasPlayerTask, Is.True);
            Assert.That(worker.CaptureTask().task.task_id, Is.EqualTo(current.task_id));
            worker.StopTask();
        }

        private static TownSocialCoordinator ConfigurePlayerChatScene(GameBootstrap bootstrap)
        {
            DisableGatewayConfigurationLoop();
            bootstrap.AiRequests.ReplaceClient(new LocalAiGatewayClient());
            foreach (TownLifeController life in bootstrap.LifeControllers.Values)
            {
                life.StopTask();
                if (life.ResidentId != ResidentIds.Yaya && life.ResidentId != ResidentIds.Amu)
                { life.enabled = false; life.GetComponent<TownResidentScheduleController>().enabled = false; }
            }
            TownSocialCoordinator coordinator = UnityEngine.Object.FindFirstObjectByType<TownSocialCoordinator>();
            typeof(TownSocialCoordinator).GetField("sentenceCount", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(coordinator, 2);
            return coordinator;
        }

        [UnityTest]
        public IEnumerator FiniteFishingRestoresActualCompletedCountAndOnlyProducesRemainingFish()
        {
            yield return SceneManager.LoadSceneAsync("DemoScene", LoadSceneMode.Single);
            yield return null;
            GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            ConfigureLocalRecoveryScene(bootstrap);
            TownLifeController worker = bootstrap.LifeControllers[ResidentIds.Yaya];
            int before = bootstrap.Inventory.GetCount(InventoryItem.Fish);
            Assert.That(bootstrap.SubmitResidentCommand(ResidentIds.Yaya, "钓鱼3次").Succeeded, Is.True);
            double deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 25;
            while (UnityEngine.Time.realtimeSinceStartupAsDouble < deadline && worker.CaptureTask().completedActivityCount < 1)
            {
                bootstrap.Simulation.Advance(UnityEngine.Time.unscaledDeltaTime * 5);
                yield return null;
            }
            ResidentTaskSnapshot snapshot = JsonUtility.FromJson<ResidentTaskSnapshot>(JsonUtility.ToJson(worker.CaptureTask()));
            Assert.That(snapshot.task, Is.Not.Null, worker.Diagnostics);
            Assert.That(snapshot.task.quantity, Is.EqualTo(3));
            Assert.That(snapshot.completedActivityCount, Is.EqualTo(1));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.Fish), Is.EqualTo(before + 1), "Progress must come from a real successful fish settlement.");
            worker.StopTask();
            Assert.That(worker.CaptureTask().completedActivityCount, Is.Zero);
            Assert.That(worker.RestoreTask(snapshot).Succeeded, Is.True);
            Assert.That(worker.CaptureTask().completedActivityCount, Is.EqualTo(1));
            deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 25;
            while (UnityEngine.Time.realtimeSinceStartupAsDouble < deadline && worker.HasPlayerTask)
            {
                bootstrap.Simulation.Advance(UnityEngine.Time.unscaledDeltaTime * 5);
                yield return null;
            }
            Assert.That(worker.HasPlayerTask, Is.False, worker.Diagnostics);
            Assert.That(worker.TaskState, Does.Contain("任务完成"));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.Fish), Is.EqualTo(before + 3), "Reload must execute only the two remaining catches, never all three again.");
            Assert.That(worker.CaptureTask().completedActivityCount, Is.Zero);
            Assert.That(bootstrap.SubmitResidentCommand(ResidentIds.Yaya, "钓鱼2次").Succeeded, Is.True);
            deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 5;
            while (UnityEngine.Time.realtimeSinceStartupAsDouble < deadline && worker.CaptureTask().task == null) yield return null;
            Assert.That(worker.CaptureTask().task.quantity, Is.EqualTo(2));
            Assert.That(worker.CaptureTask().completedActivityCount, Is.Zero);
            worker.StopTask();
        }

        private static IEnumerator SubmitAndWaitForPlayerChat(GameBootstrap bootstrap, TownSocialCoordinator coordinator, TownLifeController worker)
        {
            Assert.That(bootstrap.SubmitResidentCommand(ResidentIds.Yaya, "和阿木聊天").Succeeded, Is.True);
            double deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 8;
            while (UnityEngine.Time.realtimeSinceStartupAsDouble < deadline &&
                string.IsNullOrEmpty(coordinator.GetConversationId(ResidentIds.Yaya))) yield return null;
            Assert.That(coordinator.GetConversationId(ResidentIds.Yaya), Is.Not.Empty, worker.Diagnostics);
            Assert.That(worker.HasPlayerTask, Is.True, "Chat remains owned while its session is active.");
        }

        [UnityTest]
        public IEnumerator PlayerPauseDoesNotConsumeEitherNavigationDeadline()
        {
            yield return SceneManager.LoadSceneAsync("DemoScene", LoadSceneMode.Single);
            yield return null;
            GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            ConfigureLocalRecoveryScene(bootstrap);
            TownLifeController worker = bootstrap.LifeControllers[ResidentIds.Yaya];
            Assert.That(bootstrap.SubmitResidentCommand(ResidentIds.Yaya, "前往 fishing-2").Succeeded, Is.True);
            double deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 8;
            while (UnityEngine.Time.realtimeSinceStartupAsDouble < deadline &&
                !worker.GetComponent<TownResidentNavigator>().IsMoving) yield return null;
            Assert.That(worker.GetComponent<TownResidentNavigator>().IsMoving, Is.True, worker.Diagnostics);
            Assert.That(bootstrap.Clock.Pause().Succeeded, Is.True);
            AssertPauseExtendsDeadline(worker, "TickLife", "movementStarted");
            Assert.That(bootstrap.Clock.Resume().Succeeded, Is.True);
            InvokePrivate(worker, "TickLife");
            Assert.That(worker.HasPlayerTask, Is.True, worker.Diagnostics);
            Assert.That(worker.LastError, Is.Empty);

            worker.StopTask();
            Assert.That(bootstrap.Field.GetPlot(1).RestoreState(PlotState.Empty, null, 0, false, false, false, 0).Succeeded, Is.True);
            Assert.That(bootstrap.Inventory.RestoreCounts(9, 9, 9, 0).Succeeded, Is.True);
            Assert.That(bootstrap.SubmitResidentCommand(ResidentIds.Yaya, "播种1号地").Succeeded, Is.True);
            NpcPlanExecutor executor = worker.GetComponent<NpcPlanExecutor>();
            deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 8;
            while (UnityEngine.Time.realtimeSinceStartupAsDouble < deadline && executor.Status != NpcExecutionStatus.Moving) yield return null;
            Assert.That(executor.Status, Is.EqualTo(NpcExecutionStatus.Moving), worker.Diagnostics);
            Assert.That(bootstrap.Clock.Pause().Succeeded, Is.True);
            AssertPauseExtendsDeadline(executor, "Update", "navigationStartedAt");
            Assert.That(bootstrap.Clock.Resume().Succeeded, Is.True);
            InvokePrivate(worker, "TickLife");
            InvokePrivate(executor, "Update");
            Assert.That(executor.Status, Is.Not.EqualTo(NpcExecutionStatus.Failed), executor.LastFailureReason);
            Assert.That(executor.LastFailureReason, Is.Empty);
        }

        private static void AssertPauseExtendsDeadline(object component, string tickMethod, string startField)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            FieldInfo start = component.GetType().GetField(startField, flags);
            FieldInfo previous = component.GetType().GetField("previousRealSeconds", flags);
            Assert.That(start, Is.Not.Null);
            Assert.That(previous, Is.Not.Null);
            // Advance the observation clock by a minute without waiting a minute in production.
            // Both real movement deadlines would expire here without pause accounting.
            start.SetValue(component, now - 60);
            previous.SetValue(component, now - 60);
            InvokePrivate(component, tickMethod);
            Assert.That((double)start.GetValue(component), Is.GreaterThanOrEqualTo(now - .01));
        }

        private static void InvokePrivate(object component, string method) =>
            component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);

        private sealed class ControlledModelTransport : IAiGatewayTransport
        {
            [Serializable] private sealed class Request
            {
                public string resident_id;
                public string command;
                public string[] allowed_intents;
            }
            [Serializable] private sealed class Decision
            {
                public string resident_id;
                public string intent;
                public string target_resident_id;
                public string reason = "controlled-contract-server-selected-activity";
                public string provider = "openai";
                public string execution_source = "remote";
            }
            public readonly HashSet<string> CommandOwners = new HashSet<string>();
            public int Decisions;
            public IEnumerator PostJson(string url, string json, int timeoutSeconds, Action<AiGatewayHttpResult> completed)
            {
                // Deliberate asynchronous delay makes changing HUD selection meaningful.
                yield return null;
                yield return null;
                Request request = JsonUtility.FromJson<Request>(json);
                if (url.EndsWith("/v1/resident-task", StringComparison.Ordinal))
                {
                    CommandOwners.Add(request.resident_id);
                    ActionResult parsed = LocalResidentTaskParser.TryParse(new ResidentId(request.resident_id), request.command, out ResidentTaskSpec task);
                    Assert.That(parsed.Succeeded, Is.True, parsed.Message);
                    task.provider = "openai";
                    completed(AiGatewayHttpResult.Success(200, JsonUtility.ToJson(task)));
                }
                else if (url.EndsWith("/v1/resident-decision", StringComparison.Ordinal))
                {
                    Decisions++;
                    string intent = request.allowed_intents.Contains("Fish") ? "Fish" : request.allowed_intents.First();
                    // JsonUtility turns null string fields into empty strings, whereas the Python
                    // decision contract requires a JSON null for a target-free activity.
                    string response = JsonUtility.ToJson(new Decision { resident_id = request.resident_id, intent = intent })
                        .Replace("\"target_resident_id\":\"\"", "\"target_resident_id\":null");
                    Assert.That(response, Does.Contain("\"target_resident_id\":null"));
                    completed(AiGatewayHttpResult.Success(200, response));
                }
                else completed(AiGatewayHttpResult.Failure(503, "This controlled fixture only covers task and high-level decision contracts."));
            }
        }
    }
}
