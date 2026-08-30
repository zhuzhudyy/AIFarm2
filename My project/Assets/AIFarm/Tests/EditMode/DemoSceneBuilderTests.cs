using AIFarm.Editor;
using AIFarm.Presentation;
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
            Assert.That(npc.GetComponent<NpcPlanExecutor>(), Is.Not.Null);
            Assert.That(npc.transform.Find("NPC_Visual_Capsule"), Is.Not.Null);
            Assert.That(npc.transform.Find("ActionFeedback_ProgressBar"), Is.Not.Null);

            NavMeshSurface navMeshSurface =
                GameObject.Find("Navigation_NavMeshSurface").GetComponent<NavMeshSurface>();
            Assert.That(navMeshSurface, Is.Not.Null);
            Assert.That(navMeshSurface.navMeshData, Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas").GetComponent<Canvas>(), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/StatusPanel/TimeText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/StatusPanel/InventoryText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/StatusPanel/ExpressionText"), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/CommandPanel/CommandInput").GetComponent<InputField>(), Is.Not.Null);
            Assert.That(GameObject.Find("UI_Canvas/CommandPanel/SubmitButton").GetComponent<Button>(), Is.Not.Null);

            GameBootstrap bootstrap = GameObject.Find("GameBootstrap").GetComponent<GameBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.SceneConfig, Is.Not.Null);
            Assert.That(bootstrap.GetComponent<ReplanController>(), Is.Not.Null);

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
            Assert.That(sceneConfig.TimeScale, Is.GreaterThanOrEqualTo(240f));
            Assert.That(sceneConfig.CreateDemoMode().IsAiServiceRequired, Is.False);

            Assert.That(
                AssetDatabase.FindAssets("t:DemoInventoryConfig", new[] { "Assets/AIFarm/Config" }),
                Has.Length.EqualTo(1));
            Assert.That(
                AssetDatabase.FindAssets("t:DemoSceneConfig", new[] { "Assets/AIFarm/Config" }),
                Has.Length.EqualTo(1));
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
