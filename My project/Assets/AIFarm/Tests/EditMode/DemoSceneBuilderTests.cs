using System.Collections.Generic;
using AIFarm.Ai;
using AIFarm.Editor;
using AIFarm.Npc;
using AIFarm.Presentation;
using AIFarm.Town;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
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
            Assert.That(GameObject.Find("UI_Canvas/BackpackPanel/InventoryText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/GoalPanel/GoalText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/GoalPanel/ActionText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/GoalPanel/ActionReasonText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/GoalPanel/ExpressionText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/WorldEventsPanel/WorldEventsText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/MemoryPanel/PersonaText"), Is.Not.Null);
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
            Assert.That(sceneConfig.AiGatewayMode, Is.EqualTo(AiGatewayMode.Local));
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
    }
}
