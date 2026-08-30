using System.Collections;
using System.Linq;
using AIFarm.Core;
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
    }
}
