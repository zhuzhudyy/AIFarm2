using System;
using System.Collections;
using AIFarm.Ai;
using AIFarm.Npc;
using AIFarm.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace AIFarm.Tests.EditMode
{
    public sealed class ResidentTaskProtocolTests
    {
        [TestCase("播种 1号地", "Sow")]
        [TestCase("浇水地块2", "Water")]
        [TestCase("给3号地施肥", "Fertilize")]
        [TestCase("给4号地除草", "Weed")]
        [TestCase("收获5号地", "Harvest")]
        [TestCase("持续照料这片农田", "TendFarm")]
        [TestCase("把地种满胡萝卜并照顾到收获。", "TendFarm")]
        [TestCase("去钓鱼", "Fish")]
        [TestCase("去果园摘果", "PickFruit")]
        [TestCase("前往水井", "Move")]
        [TestCase("和芽芽聊天", "Chat")]
        [TestCase("停止", "Stop")]
        public void CommandsRemainBoundToExplicitResidentAndTaskType(string command, string expectedType)
        {
            foreach (ResidentId owner in ResidentIds.TownResidents)
            {
                if (command.Contains("芽芽") && owner == ResidentIds.Yaya) continue;
                var outcome = LocalResidentTaskParser.TryParse(owner, command, out ResidentTaskSpec task);
                Assert.That(outcome.Succeeded, Is.True, outcome.Message);
                Assert.That(task.resident_id, Is.EqualTo(owner.Value));
                Assert.That(task.task_type, Is.EqualTo(expectedType));
            }
        }

        [Test]
        public void UnknownCommandDoesNotTurnIntoFullFieldFarming()
        {
            Assert.That(LocalResidentTaskParser.TryParse(ResidentIds.Momo, "帮我造一架宇宙飞船", out _).Failed, Is.True);
            Assert.That(LocalResidentTaskParser.TryParse(ResidentIds.Momo, "去不存在的地点", out _).Failed, Is.True);
        }

        [Test]
        public void JsonOwnerMismatchAndDuplicatePlotAreRejected()
        {
            ResidentTaskSpec task = ResidentTaskSpec.Create(ResidentIds.Amu, "Water");
            Assert.That(AiGatewayJsonCodec.TryParseResidentTask(JsonUtility.ToJson(task), ResidentIds.Momo, out _).Failed, Is.True);
            task.target_plot_numbers = new[] { 1, 1 };
            Assert.That(task.Validate(ResidentIds.Amu).Failed, Is.True);
        }

        [Test]
        public void FarmCommandKeepsExplicitPlotAndRejectsStateMutationFields()
        {
            Assert.That(LocalResidentTaskParser.TryParse(ResidentIds.Amu, "播种 1", out ResidentTaskSpec task).Succeeded, Is.True);
            Assert.That(task.target_plot_numbers, Is.EqualTo(new[] { 1 }));
            string injected = JsonUtility.ToJson(task).TrimEnd('}') + ",\"inventory\":999}";
            Assert.That(AiGatewayJsonCodec.TryParseResidentTask(injected, ResidentIds.Amu, out _).Failed, Is.True);
        }

        [TestCase("钓鱼3次", "Fish", 3)]
        [TestCase("钓3条鱼", "Fish", 3)]
        [TestCase("摘3个果", "PickFruit", 3)]
        [TestCase("摘三个苹果", "PickFruit", 3)]
        [TestCase("采摘两颗水果", "PickFruit", 2)]
        [TestCase("摘果九十九次", "PickFruit", 99)]
        [TestCase("fish 12 times", "Fish", 12)]
        [TestCase("持续钓鱼3次", "Fish", 3)]
        public void ExplicitActivityQuantityIsFiniteAndRemainsBoundToOwner(string command, string type, int quantity)
        {
            foreach (ResidentId owner in ResidentIds.TownResidents)
            {
                var result = LocalResidentTaskParser.TryParse(owner, command, out ResidentTaskSpec task);
                Assert.That(result.Succeeded, Is.True, result.Message);
                Assert.That(task.resident_id, Is.EqualTo(owner.Value));
                Assert.That(task.task_type, Is.EqualTo(type));
                Assert.That(task.quantity, Is.EqualTo(quantity));
                Assert.That(task.repeat, Is.False);
                Assert.That(task.target_plot_numbers, Is.Empty);
            }
        }

        [TestCase("钓位2钓鱼3次", "fishing-2", 3)]
        [TestCase("去第2号钓位钓鱼", "fishing-2", 1)]
        [TestCase("去3号果树摘2个果", "fruit-3", 2)]
        [TestCase("fruit-4 摘果五次", "fruit-4", 5)]
        public void ActivityTargetNumberIsNeverItsQuantity(string command, string target, int quantity)
        {
            var result = LocalResidentTaskParser.TryParse(ResidentIds.Momo, command, out ResidentTaskSpec task);
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(task.target_id, Is.EqualTo(target));
            Assert.That(task.quantity, Is.EqualTo(quantity));
        }

        [TestCase("钓鱼0次")]
        [TestCase("钓鱼100次")]
        [TestCase("钓鱼-1次")]
        [TestCase("摘1.5个果")]
        [TestCase("摘零个果")]
        [TestCase("摘一百个果")]
        [TestCase("钓鱼3次然后2次")]
        public void InvalidActivityQuantitiesAreRejectedInsteadOfSilentlyReduced(string command)
        {
            Assert.That(LocalResidentTaskParser.TryParse(ResidentIds.Xiaosui, command, out _).Failed, Is.True);
        }

        [Test]
        public void EndlessActivityOnlyWithoutAnExplicitTotal()
        {
            Assert.That(LocalResidentTaskParser.TryParse(ResidentIds.Yaya, "持续钓鱼", out ResidentTaskSpec task).Succeeded, Is.True);
            Assert.That(task.repeat, Is.True);
            Assert.That(task.quantity, Is.EqualTo(1));
        }

        [TestCase("播种0号地")]
        [TestCase("播种10号地")]
        [TestCase("浇水地块11")]
        [TestCase("播种100")]
        [TestCase("浇水3次")]
        public void FarmTargetsRejectOutOfRangeAndDoNotConsumeActivityCounts(string command)
        {
            Assert.That(LocalResidentTaskParser.TryParse(ResidentIds.Amu, command, out _).Failed, Is.True);
        }

        [Test]
        public void ExactFarmTargetSurvivesAnExplicitOneTimeQualifier()
        {
            Assert.That(LocalResidentTaskParser.TryParse(ResidentIds.Amu, "给第1号地和第9号地浇水1次", out ResidentTaskSpec task).Succeeded, Is.True);
            Assert.That(task.target_plot_numbers, Is.EqualTo(new[] { 1, 9 }));
        }

        [TestCase("和芽芽聊天3次")]
        [TestCase("持续和芽芽聊天")]
        public void UnsupportedChatCountsAndLoopsAreExplicitlyRejected(string command)
        {
            Assert.That(LocalResidentTaskParser.TryParse(ResidentIds.Amu, command, out _).Failed, Is.True);
        }

        [Test]
        public void ModelChatCannotBypassTheSingleConversationContract()
        {
            ResidentTaskSpec task = ResidentTaskSpec.Create(ResidentIds.Amu, "Chat");
            task.target_resident_id = ResidentIds.Yaya.Value;
            task.quantity = 2;
            Assert.That(task.Validate(ResidentIds.Amu).Failed, Is.True);
            task.quantity = 1;
            task.repeat = true;
            Assert.That(task.Validate(ResidentIds.Amu).Failed, Is.True);
            task.repeat = false;
            Assert.That(task.Validate(ResidentIds.Amu).Succeeded, Is.True);
        }

        [Test]
        public void HttpSuccessFromMockIsTruthfullyLocal()
        {
            Assert.That(AiGatewayJsonCodec.ResponseSource("{\"provider\":\"mock\",\"execution_source\":\"local\"}"), Is.EqualTo(AiGatewayMode.Local));
            Assert.That(AiGatewayJsonCodec.ResponseSource("{\"provider\":\"openai\",\"execution_source\":\"remote\"}"), Is.EqualTo(AiGatewayMode.Remote));
            Assert.That(AiGatewayJsonCodec.ResponseSource("{\"provider\":\"mock\",\"execution_source\":\"fallback\"}"), Is.EqualTo(AiGatewayMode.Local));
        }

        [Test]
        public void DecisionAndConversationWirePreserveNullableMemoryProvenance()
        {
            var owner = ResidentIds.Yaya;
            var other = ResidentIds.Amu;
            var context = new ResidentContext(owner, ResidentPersonaSnapshot.FromDefinition(ResidentDefinition.Yaya), "正在观察农田",
                new[] { new RelationshipSnapshot(owner, other, 0, 0) },
                new[] { new ResidentMemorySnapshot(owner, "今天刚播下一颗种子。", 5) });
            var peer = new ResidentContext(other, ResidentPersonaSnapshot.FromDefinition(ResidentDefinition.Amu), "在广场等候",
                new[] { new RelationshipSnapshot(other, owner, 0, 0) }, Array.Empty<ResidentMemorySnapshot>());
            string decision = AiGatewayJsonCodec.SerializeResidentDecisionRequest(new ResidentDecisionRequest(owner, context,
                "下一步做什么", new[] { "Fish", "Rest" }, new[] { other }));
            string conversation = AiGatewayJsonCodec.SerializeConversationScriptRequest(new ConversationScriptRequest(owner,
                new[] { owner, other }, new[] { context, peer }, "今天的劳动", 4));
            foreach (string wire in new[] { decision, conversation })
            {
                Assert.That(wire, Does.Contain("\"immediate_source_resident_id\":null"));
                Assert.That(wire, Does.Contain("\"knowledge_id\":null"));
                Assert.That(wire, Does.Contain("\"root_fact_id\":null"));
                Assert.That(wire, Does.Not.Contain("\"immediate_source_resident_id\":\"\""));
                Assert.That(wire, Does.Contain("\"owner_resident_id\":\"" + owner.Value + "\""));
            }
        }

        [Test]
        public void SharedGatewayRoutesTaskThroughTransportAndKeepsSubmittedOwner()
        {
            ResidentTaskSpec expected = ResidentTaskSpec.Create(ResidentIds.Xiaosui, "Fish", "fishing-2");
            expected.provider = "openai";
            expected.config_version = 12;
            var transport = new TaskTransport(JsonUtility.ToJson(expected));
            var coordinator = new AiRequestCoordinator(new RemoteAiGatewayClient("http://127.0.0.1:8000", 30, transport));
            AiGatewayResult<ResidentTaskSpec> received = null;
            Drain(coordinator.InterpretResidentTask(ResidentIds.Xiaosui, "去2号钓位钓鱼", new[] { "fishing-2" },
                new[] { ResidentIds.Yaya.Value }, result => received = result));
            Assert.That(received, Is.Not.Null);
            Assert.That(received.Succeeded, Is.True, received.Outcome.Message);
            Assert.That(received.ResidentId, Is.EqualTo(ResidentIds.Xiaosui));
            Assert.That(received.Value.task_type, Is.EqualTo("Fish"));
            Assert.That(transport.Url, Does.EndWith("/v1/resident-task"));
            Assert.That(transport.Body, Does.Contain(ResidentIds.Xiaosui.Value));
            Assert.That(coordinator.ActiveRequestCount, Is.Zero);
        }

        [Test]
        public void ReplacingSharedConfigurationInvalidatesInFlightOwnerBeforeNewTask()
        {
            ResidentTaskSpec expected = ResidentTaskSpec.Create(ResidentIds.Amu, "Fish", "fishing-1");
            expected.provider = "openai";
            var coordinator = new AiRequestCoordinator(new RemoteAiGatewayClient("http://127.0.0.1:8000", 30,
                new TaskTransport(JsonUtility.ToJson(expected))));
            bool staleDelivered = false;
            IEnumerator old = coordinator.InterpretResidentTask(ResidentIds.Amu, "钓鱼", Array.Empty<string>(),
                Array.Empty<string>(), result => staleDelivered = true);
            Assert.That(old.MoveNext(), Is.True);
            Assert.That(old.MoveNext(), Is.True);
            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(1));
            coordinator.ReplaceClient(new LocalAiGatewayClient());
            Assert.That(coordinator.ActiveRequestCount, Is.Zero);
            Drain(old);
            Assert.That(staleDelivered, Is.False);
            AiGatewayResult<ResidentTaskSpec> fresh = null;
            Drain(coordinator.InterpretResidentTask(ResidentIds.Amu, "摘果", Array.Empty<string>(),
                Array.Empty<string>(), result => fresh = result));
            Assert.That(fresh.Succeeded, Is.True);
            Assert.That(fresh.Value.task_type, Is.EqualTo("PickFruit"));
            Assert.That(fresh.ResidentId, Is.EqualTo(ResidentIds.Amu));
        }

        private static void Drain(IEnumerator routine)
        {
            int steps = 0;
            while (routine.MoveNext())
            {
                Assert.That(++steps, Is.LessThan(100));
                if (routine.Current is IEnumerator nested) Drain(nested);
            }
        }

        private sealed class TaskTransport : IAiGatewayTransport
        {
            private readonly string response;
            public TaskTransport(string response) { this.response = response; }
            public string Url;
            public string Body;
            public IEnumerator PostJson(string url, string json, int timeoutSeconds, Action<AiGatewayHttpResult> completed)
            {
                Url = url; Body = json;
                yield return null;
                completed(AiGatewayHttpResult.Success(200, response));
            }
        }
    }
}
