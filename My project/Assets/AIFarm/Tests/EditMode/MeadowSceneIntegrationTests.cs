using System.Linq;
using AIFarm.Editor;
using AIFarm.Presentation;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace AIFarm.Tests.EditMode
{
    /// <summary>Checks the saved art and baked navigation without regenerating the scene.</summary>
    public sealed class MeadowSceneIntegrationTests
    {
        private SceneSetup[] previousSetup;
        private Scene demoScene;

        [SetUp]
        public void OpenSavedDemoScene()
        {
            previousSetup = null;
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                if (SceneManager.GetSceneAt(index).isDirty)
                {
                    Assert.Ignore("Save scene edits before running saved-scene integration checks.");
                }
            }

            previousSetup = EditorSceneManager.GetSceneManagerSetup();
            demoScene = EditorSceneManager.OpenScene(DemoSceneBuilder.ScenePath, OpenSceneMode.Single);
        }

        [TearDown]
        public void RestoreOriginalSceneSetup()
        {
            if (previousSetup == null)
            {
                return;
            }

            SceneSetup[] setupToRestore = previousSetup;
            previousSetup = null;
            bool hasLoadedScene = setupToRestore.Any(setup =>
                setup.isLoaded && !string.IsNullOrEmpty(setup.path));
            bool hasActiveScene = setupToRestore.Any(setup =>
                setup.isLoaded && setup.isActive && !string.IsNullOrEmpty(setup.path));
            if (hasLoadedScene && hasActiveScene)
            {
                EditorSceneManager.RestoreSceneManagerSetup(setupToRestore);
            }
            else
            {
                // The Test Runner can begin with no scene, or an unnamed empty scene.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void SavedScene_ContainsFourVisibleBlenderResidentsAndNineFarmPlots()
        {
            TownResidentScheduleController[] residents = InScene<TownResidentScheduleController>();
            Assert.That(residents, Has.Length.EqualTo(4));
            Assert.That(residents.All(resident => resident.ResidentId.IsValid), Is.True);
            Assert.That(residents.Select(resident => resident.ResidentId).Distinct().Count(), Is.EqualTo(4));
            Assert.That(InScene<PlotBlockoutView>(), Has.Length.EqualTo(9));
            Assert.That(
                InScene<PlotInteractionPoint>().Select(point => point.PlotNumber).OrderBy(number => number),
                Is.EqualTo(Enumerable.Range(1, 9)));
            GameObject artRoot = demoScene.GetRootGameObjects()
                .SingleOrDefault(root => root.name == MeadowSceneUpgrade.RootName);
            Assert.That(artRoot, Is.Not.Null, "The saved scene must include the Blender environment.");

            foreach (TownResidentScheduleController resident in residents)
            {
                Transform model = resident.transform.Find("NPC_Visual_Capsule/Blender_Resident");
                Assert.That(model, Is.Not.Null, $"Missing imported visual for {resident.ResidentId}.");
                Assert.That(model.gameObject.activeInHierarchy, Is.True);
                Assert.That(model.GetComponent<ResidentModelAnimation>(), Is.Not.Null);
                string modelPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(model.gameObject);
                Assert.That(modelPath, Does.StartWith(MeadowSceneUpgrade.ModelFolder + "/Residents/"));
                Assert.That(modelPath, Does.EndWith(".fbx"));

                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers.Length, Is.GreaterThan(4), "The imported resident should contain modeled body parts.");
                Assert.That(renderers.All(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy), Is.True);
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                    Assert.That(renderer.sharedMaterials, Is.Not.Empty);
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        AssertUsableMaterial(material, resident.ResidentId.ToString());
                    }
                }

                Assert.That(bounds.size.y, Is.InRange(1.6f, 3.3f), "Check the FBX scale and vertical axis.");
                Assert.That(Mathf.Max(bounds.size.x, bounds.size.z), Is.InRange(0.5f, 2.5f));
                Assert.That(Mathf.Abs(bounds.min.y - resident.transform.position.y), Is.LessThan(0.18f),
                    "Character feet must meet the navigation ground rather than float or sink.");
                Assert.That(renderers.SelectMany(renderer => renderer.sharedMaterials).Distinct().Count(),
                    Is.GreaterThanOrEqualTo(4), "A single blockout material must not overwrite the character palette.");

                string[] pivots = model.GetComponentsInChildren<Transform>(true).Select(part => part.name).ToArray();
                foreach (string limb in new[] { "LeftLeg", "RightLeg", "LeftArm", "RightArm" })
                {
                    Assert.That(pivots.Count(name => name == limb), Is.EqualTo(1));
                }
            }
        }

        [Test]
        public void SavedNavMesh_EveryResidentCanReachAllFarmTownAndConversationPoints()
        {
            NavMeshSurface[] surfaces = InScene<NavMeshSurface>();
            Assert.That(surfaces, Has.Length.EqualTo(1));
            Assert.That(surfaces[0].navMeshData, Is.Not.Null, "The saved scene must retain its baked NavMesh.");
            TownResidentScheduleController[] residents = InScene<TownResidentScheduleController>();
            PlotInteractionPoint[] farmPoints = InScene<PlotInteractionPoint>();
            LocationArrivalPoint[] townPoints = InScene<LocationArrivalPoint>();
            Assert.That(residents, Has.Length.EqualTo(4));
            Assert.That(farmPoints, Has.Length.EqualTo(9));
            Assert.That(townPoints.Count(point => point.InteractionPointId.StartsWith("location-")), Is.EqualTo(36));
            Assert.That(townPoints.Count(point => point.InteractionPointId.StartsWith("social-")), Is.EqualTo(4));
            Assert.That(townPoints.Select(point => point.InteractionPointId).Distinct().Count(), Is.EqualTo(40));

            foreach (TownResidentScheduleController resident in residents)
            {
                NavMeshAgent agent = resident.GetComponent<NavMeshAgent>();
                Assert.That(agent, Is.Not.Null);
                var filter = new NavMeshQueryFilter
                {
                    agentTypeID = agent.agentTypeID,
                    areaMask = agent.areaMask
                };
                Vector3 start = SampleReachablePoint(resident.transform.position, filter, resident.name + " spawn");
                foreach (PlotInteractionPoint point in farmPoints)
                {
                    AssertCompletePath(start, point.Position, filter, resident.name + " -> plot " + point.PlotNumber);
                }

                foreach (LocationArrivalPoint point in townPoints)
                {
                    AssertCompletePath(start, point.Position, filter, resident.name + " -> " + point.InteractionPointId);
                }
            }
        }

        private T[] InScene<T>() where T : Component
        {
            return demoScene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
        }

        private static void AssertUsableMaterial(Material material, string residentId)
        {
            Assert.That(material, Is.Not.Null, residentId + " has an unassigned material.");
            Assert.That(material.shader, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                Assert.That(material.shader.isSupported, Is.True, material.name + " cannot render on this device.");
            }

            Assert.That(AssetDatabase.GetAssetPath(material), Does.StartWith(MeadowSceneUpgrade.MaterialFolder + "/"),
                "Palette remapping must survive saving and reopening the scene.");
            Assert.That(material.HasProperty("_BaseColor"), Is.True);
            Color color = material.GetColor("_BaseColor");
            Assert.That(color.a, Is.GreaterThan(0.99f));
            Assert.That(float.IsNaN(color.r) || float.IsNaN(color.g) || float.IsNaN(color.b), Is.False);
        }

        private static Vector3 SampleReachablePoint(Vector3 point, NavMeshQueryFilter filter, string context)
        {
            Assert.That(NavMesh.SamplePosition(point, out NavMeshHit hit, 0.2f, filter), Is.True,
                context + " is outside the saved navigation surface.");
            Vector3 horizontalOffset = hit.position - point;
            horizontalOffset.y = 0f;
            Assert.That(horizontalOffset.magnitude, Is.LessThanOrEqualTo(0.08f),
                context + " is obstructed beyond the navigators' arrival tolerance.");
            return hit.position;
        }

        private static void AssertCompletePath(Vector3 start, Vector3 target, NavMeshQueryFilter filter, string context)
        {
            Vector3 destination = SampleReachablePoint(target, filter, context);
            var path = new NavMeshPath();
            Assert.That(NavMesh.CalculatePath(start, destination, filter, path), Is.True, context);
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete), context);
        }
    }
}
