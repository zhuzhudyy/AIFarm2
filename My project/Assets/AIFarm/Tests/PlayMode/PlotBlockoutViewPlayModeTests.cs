using System.Collections;
using System.Reflection;
using AIFarm.Farming;
using AIFarm.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AIFarm.Tests.PlayMode
{
    public sealed class PlotBlockoutViewPlayModeTests
    {
        private GameObject testRoot;
        private DemoInventoryConfig inventoryConfig;
        private DemoSceneConfig sceneConfig;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (testRoot != null)
            {
                Object.Destroy(testRoot);
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
        public IEnumerator SerializedView_RebuildsWeedRendererCacheOnFirstRefresh()
        {
            testRoot = new GameObject("PlotBlockoutViewTestRoot");
            testRoot.SetActive(false);
            inventoryConfig = ScriptableObject.CreateInstance<DemoInventoryConfig>();
            inventoryConfig.Configure(seedCount: 1, waterCount: 1, fertilizerCount: 1, carrotCount: 0);
            sceneConfig = ScriptableObject.CreateInstance<DemoSceneConfig>();
            sceneConfig.Configure(inventoryConfig, 1, 8, 240f, 2f, 0.35f);

            GameBootstrap bootstrap = testRoot.AddComponent<GameBootstrap>();
            bootstrap.Configure(sceneConfig);
            testRoot.SetActive(true);
            Assert.That(bootstrap.IsInitialized, Is.True);

            GameObject soil = GameObject.CreatePrimitive(PrimitiveType.Cube);
            soil.transform.SetParent(testRoot.transform, false);
            GameObject crop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crop.transform.SetParent(testRoot.transform, false);
            GameObject weeds = new GameObject("Weeds");
            weeds.transform.SetParent(testRoot.transform, false);
            GameObject weedBlade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            weedBlade.transform.SetParent(weeds.transform, false);

            PlotBlockoutView view = soil.AddComponent<PlotBlockoutView>();
            Assert.That(
                view.Configure(
                    bootstrap,
                    1,
                    soil.GetComponent<Renderer>(),
                    crop,
                    crop.GetComponent<Renderer>(),
                    weeds).Succeeded,
                Is.True);

            FarmPlot plot = bootstrap.Field.GetPlot(1);
            Assert.That(plot.Sow(bootstrap.Inventory).Succeeded, Is.True);
            Assert.That(plot.Fertilize(bootstrap.Inventory).Succeeded, Is.True);
            Assert.That(plot.IntroduceWeeds().Succeeded, Is.True);

            FieldInfo cacheField = typeof(PlotBlockoutView).GetField(
                "weedRenderers",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(cacheField, Is.Not.Null);
            cacheField.SetValue(view, null);

            Assert.DoesNotThrow(view.Refresh);
            Assert.That(weeds.activeSelf, Is.True);
            yield return null;
        }
    }
}
