using System.Collections;
using System.Linq;
using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AIFarm.Tests.PlayMode
{
    public sealed class DemoHudObservabilityPlayModeTests
    {
        private GameObject bootstrapObject;
        private GameObject hudObject;
        private DemoInventoryConfig inventoryConfig;
        private DemoSceneConfig sceneConfig;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (hudObject != null)
            {
                Object.Destroy(hudObject);
            }

            if (bootstrapObject != null)
            {
                Object.Destroy(bootstrapObject);
            }

            if (inventoryConfig != null)
            {
                Object.Destroy(inventoryConfig);
            }

            if (sceneConfig != null)
            {
                Object.Destroy(sceneConfig);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator TimeButtons_PauseResumeAndSelectOnlySupportedSpeeds()
        {
            bootstrapObject = new GameObject("HudTimeBootstrap");
            bootstrapObject.SetActive(false);
            inventoryConfig = ScriptableObject.CreateInstance<DemoInventoryConfig>();
            inventoryConfig.Configure(9, 9, 9, 0);
            sceneConfig = ScriptableObject.CreateInstance<DemoSceneConfig>();
            sceneConfig.Configure(
                inventoryConfig,
                day: 1,
                hour: 8,
                scale: 20f,
                size: 2f,
                spacing: 0.35f);

            GameBootstrap bootstrap = bootstrapObject.AddComponent<GameBootstrap>();
            bootstrap.Configure(sceneConfig);
            Assert.That(bootstrap.Initialize().Succeeded, Is.True);

            hudObject = new GameObject("HudTimeControls");
            DemoHud hud = hudObject.AddComponent<DemoHud>();
            Button pauseButton = CreateButton("PauseButton");
            Text pauseLabel = CreateText("PauseLabel");
            Button speed1Button = CreateButton("Speed1Button");
            Button speed5Button = CreateButton("Speed5Button");
            Button speed20Button = CreateButton("Speed20Button");
            hud.Configure(
                bootstrap,
                null,
                null,
                null,
                null,
                null,
                null,
                pauseControl: pauseButton,
                pauseControlLabel: pauseLabel,
                speed1Control: speed1Button,
                speed5Control: speed5Button,
                speed20Control: speed20Button);

            yield return null;

            pauseButton.onClick.Invoke();
            yield return null;
            Assert.That(bootstrap.Clock.IsPaused, Is.True);
            Assert.That(pauseLabel.text, Is.EqualTo("RESUME"));

            pauseButton.onClick.Invoke();
            speed1Button.onClick.Invoke();
            Assert.That(bootstrap.Clock.IsPaused, Is.False);
            Assert.That(bootstrap.Clock.TimeScale, Is.EqualTo(1d));

            speed5Button.onClick.Invoke();
            Assert.That(bootstrap.Clock.TimeScale, Is.EqualTo(5d));
            speed20Button.onClick.Invoke();
            Assert.That(bootstrap.Clock.TimeScale, Is.EqualTo(20d));

            ActionResult unsupported = hud.SetTimeScale(2d);
            Assert.That(unsupported.Failed, Is.True);
            Assert.That(bootstrap.Clock.TimeScale, Is.EqualTo(20d));
            Assert.That(
                bootstrap.Events.Entries.Count(
                    entry => entry.Kind == WorldEventKind.TimeControlChanged),
                Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator AiModeLabel_ShowsRemoteThenLocalAfterAutomaticFallback()
        {
            hudObject = new GameObject("HudAiMode");
            ReplanController controller = hudObject.AddComponent<ReplanController>();
            controller.enabled = false;
            var remoteClient = new RemoteAiGatewayClient(
                "http://127.0.0.1:8000",
                requestTimeoutSeconds: 2,
                gatewayTransport: new UnavailableTransport());
            Assert.That(controller.ConfigureAiGateway(remoteClient).Succeeded, Is.True);

            Text modeLabel = CreateText("AiModeLabel");
            DemoHud hud = hudObject.AddComponent<DemoHud>();
            hud.Configure(
                gameBootstrap: null,
                timeLabel: null,
                inventoryLabel: null,
                goalLabel: null,
                actionLabel: null,
                input: null,
                button: null,
                controller: controller,
                aiModeLabel: modeLabel);

            yield return null;
            Assert.That(modeLabel.text, Is.EqualTo("AI: REMOTE"));

            AiGatewayResult<FarmGoalSpec> fallbackResult = null;
            yield return remoteClient.InterpretCommand(
                "把地种满胡萝卜并照顾到收获。",
                result => fallbackResult = result);
            yield return null;

            Assert.That(fallbackResult, Is.Not.Null);
            Assert.That(fallbackResult.Source, Is.EqualTo(AiGatewayMode.Local));
            Assert.That(modeLabel.text, Is.EqualTo("AI: LOCAL"));
        }

        [UnityTest]
        public IEnumerator ResidentMemorySelector_IsOwnerScopedAndNeverRetainsPreviousPrivateText()
        {
            bootstrapObject = new GameObject("HudResidentMemoryBootstrap");
            bootstrapObject.SetActive(false);
            inventoryConfig = ScriptableObject.CreateInstance<DemoInventoryConfig>();
            inventoryConfig.Configure(9, 9, 9, 0);
            sceneConfig = ScriptableObject.CreateInstance<DemoSceneConfig>();
            sceneConfig.Configure(
                inventoryConfig,
                day: 1,
                hour: 8,
                scale: 20f,
                size: 2f,
                spacing: 0.35f);

            GameBootstrap bootstrap = bootstrapObject.AddComponent<GameBootstrap>();
            bootstrap.Configure(sceneConfig);
            Assert.That(bootstrap.Initialize().Succeeded, Is.True);
            Assert.That(
                bootstrap.ResidentRegistry.TryGetRuntimeState(
                    ResidentIds.Yaya,
                    out ResidentRuntimeState yaya).Succeeded,
                Is.True);
            Assert.That(
                bootstrap.ResidentRegistry.TryGetRuntimeState(
                    ResidentIds.Amu,
                    out ResidentRuntimeState amu).Succeeded,
                Is.True);
            Assert.That(
                yaya.Memories.AddObservation(
                    ResidentIds.Yaya,
                    1d,
                    "YAYA-PRIVATE-MARKER",
                    8,
                    WorldEventKind.System,
                    out _).Succeeded,
                Is.True);
            Assert.That(
                amu.Memories.AddObservation(
                    ResidentIds.Amu,
                    2d,
                    "AMU-PRIVATE-MARKER",
                    8,
                    WorldEventKind.System,
                    out _).Succeeded,
                Is.True);
            Assert.That(
                yaya.Memories.AddReflection(
                    ResidentIds.Yaya,
                    3d,
                    "YAYA-REFLECTION-MARKER",
                    out _).Succeeded,
                Is.True);
            Assert.That(
                amu.Memories.AddReflection(
                    ResidentIds.Amu,
                    4d,
                    "AMU-REFLECTION-MARKER",
                    out _).Succeeded,
                Is.True);

            hudObject = new GameObject("HudResidentMemory");
            DemoHud hud = hudObject.AddComponent<DemoHud>();
            Text goalLabel = CreateText("GoalLabel");
            Text actionLabel = CreateText("ActionLabel");
            Text expressionLabel = CreateText("ExpressionLabel");
            Text reasonLabel = CreateText("ReasonLabel");
            Text eventsLabel = CreateText("EventsLabel");
            Text moodLabel = CreateText("MoodLabel");
            Text emojiLabel = CreateText("EmojiLabel");
            Text memoryTitle = CreateText("MemoryTitle");
            Text personaLabel = CreateText("PersonaLabel");
            Text memoriesLabel = CreateText("MemoriesLabel");
            Text reflectionsLabel = CreateText("ReflectionsLabel");
            Button submitButton = CreateButton("SubmitButton");
            Button yayaButton = CreateButton("YayaButton");
            Button amuButton = CreateButton("AmuButton");
            Button xiaosuiButton = CreateButton("XiaosuiButton");
            Button momoButton = CreateButton("MomoButton");
            var inputObject = new GameObject("CommandInput", typeof(RectTransform));
            inputObject.transform.SetParent(hudObject.transform, false);
            InputField commandInput = inputObject.AddComponent<InputField>();
            hud.Configure(
                gameBootstrap: bootstrap,
                timeLabel: null,
                inventoryLabel: null,
                goalLabel: goalLabel,
                actionLabel: actionLabel,
                input: commandInput,
                button: submitButton,
                expressionLabel: expressionLabel,
                actionReasonLabel: reasonLabel,
                eventsLabel: eventsLabel,
                moodLabel: moodLabel,
                emojiLabel: emojiLabel,
                memoriesLabel: memoriesLabel,
                reflectionsLabel: reflectionsLabel,
                memoryTitleLabel: memoryTitle,
                personaLabel: personaLabel,
                yayaSelectionControl: yayaButton,
                amuSelectionControl: amuButton,
                xiaosuiSelectionControl: xiaosuiButton,
                momoSelectionControl: momoButton);

            yield return null;

            Assert.That(hud.SelectedResidentId, Is.EqualTo(ResidentIds.Yaya));
            Assert.That(memoryTitle.text, Does.StartWith("芽芽"));
            Assert.That(memoriesLabel.text, Does.Contain("YAYA-PRIVATE-MARKER"));
            Assert.That(memoriesLabel.text, Does.Not.Contain("AMU-PRIVATE-MARKER"));
            Assert.That(reflectionsLabel.text, Does.Contain("YAYA-REFLECTION-MARKER"));
            Assert.That(reflectionsLabel.text, Does.Not.Contain("AMU-REFLECTION-MARKER"));

            amuButton.onClick.Invoke();
            Assert.That(hud.SelectedResidentId, Is.EqualTo(ResidentIds.Amu));
            Assert.That(memoryTitle.text, Does.StartWith("阿木"));
            Assert.That(personaLabel.text, Does.Contain("小镇木工"));
            Assert.That(memoriesLabel.text, Does.Contain("AMU-PRIVATE-MARKER"));
            Assert.That(memoriesLabel.text, Does.Not.Contain("YAYA-PRIVATE-MARKER"));
            Assert.That(reflectionsLabel.text, Does.Contain("AMU-REFLECTION-MARKER"));
            Assert.That(reflectionsLabel.text, Does.Not.Contain("YAYA-REFLECTION-MARKER"));
            Assert.That(submitButton.interactable, Is.False);
            Assert.That(commandInput.interactable, Is.False);

            xiaosuiButton.onClick.Invoke();
            Assert.That(hud.SelectedResidentId, Is.EqualTo(ResidentIds.Xiaosui));
            Assert.That(memoryTitle.text, Does.StartWith("小穗"));
            Assert.That(memoriesLabel.text, Does.Not.Contain("AMU-PRIVATE-MARKER"));
            Assert.That(memoriesLabel.text, Does.Not.Contain("YAYA-PRIVATE-MARKER"));
            Assert.That(reflectionsLabel.text, Does.Not.Contain("AMU-REFLECTION-MARKER"));
            Assert.That(reflectionsLabel.text, Does.Not.Contain("YAYA-REFLECTION-MARKER"));

            momoButton.onClick.Invoke();
            Assert.That(hud.SelectedResidentId, Is.EqualTo(ResidentIds.Momo));
            Assert.That(memoryTitle.text, Does.StartWith("墨墨"));
            Assert.That(memoriesLabel.text, Does.Not.Contain("AMU-PRIVATE-MARKER"));
            Assert.That(memoriesLabel.text, Does.Not.Contain("YAYA-PRIVATE-MARKER"));
            Assert.That(reflectionsLabel.text, Does.Not.Contain("AMU-REFLECTION-MARKER"));
            Assert.That(reflectionsLabel.text, Does.Not.Contain("YAYA-REFLECTION-MARKER"));

            ActionResult missing = hud.SelectResident(new ResidentId("resident-999"));
            Assert.That(missing.Failed, Is.True);
            yield return null;
            Assert.That(memoryTitle.text, Is.Empty);
            Assert.That(personaLabel.text, Is.Empty);
            Assert.That(memoriesLabel.text, Is.Empty);
            Assert.That(reflectionsLabel.text, Is.Empty);
            Assert.That(goalLabel.text, Is.Empty);
            Assert.That(eventsLabel.text, Is.Empty);
        }

        private Button CreateButton(string name)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform));
            buttonObject.transform.SetParent(hudObject.transform, false);
            return buttonObject.AddComponent<Button>();
        }

        private Text CreateText(string name)
        {
            var textObject = new GameObject(name, typeof(RectTransform));
            textObject.transform.SetParent(hudObject.transform, false);
            return textObject.AddComponent<Text>();
        }

        private sealed class UnavailableTransport : IAiGatewayTransport
        {
            public IEnumerator PostJson(
                string url,
                string json,
                int timeoutSeconds,
                System.Action<AiGatewayHttpResult> completed)
            {
                completed(AiGatewayHttpResult.Failure(0, "Service unavailable."));
                yield break;
            }
        }
    }
}
