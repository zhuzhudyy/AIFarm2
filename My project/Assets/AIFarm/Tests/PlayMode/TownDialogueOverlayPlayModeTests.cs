using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Presentation;
using AIFarm.Town;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AIFarm.Tests.PlayMode
{
    public sealed class TownDialogueOverlayPlayModeTests
    {
        private GameObject root;
        private GameBootstrap bootstrap;
        private DemoHud hud;
        private TownDialogueOverlay overlay;
        private Canvas canvas;
        private readonly List<ScriptableObject> assets = new List<ScriptableObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            root = new GameObject("DialogueCanvasPlayModeFixture");
            root.SetActive(false);
            var inventory = Asset<DemoInventoryConfig>();
            var config = Asset<DemoSceneConfig>();
            config.Configure(inventory, 1, 8, 1, 2, .35f);
            bootstrap = Child("Bootstrap").AddComponent<GameBootstrap>();
            bootstrap.Configure(config);
            hud = Child("Hud").AddComponent<DemoHud>();
            hud.Configure(bootstrap, null, null, null, null, null, null);
            GameObject cameraObject = Child("Dialogue Test Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 9, -16);
            camera.transform.LookAt(new Vector3(0, 1.2f, 0));
            camera.orthographic = true;
            camera.orthographicSize = 9;
            Child("EventSystem").AddComponent<EventSystem>();
            var location = Asset<TownLocationDefinitionAsset>();
            Success(location.Configure("dialogue-test-plaza", "测试广场"));
            int index = 0;
            foreach (ResidentDefinition definition in ResidentDefinition.TownResidents)
            {
                var schedule = Asset<DailyScheduleDefinitionAsset>();
                Success(schedule.Configure(definition.ResidentId.Value,
                    new[] { new DailyScheduleSlotAsset("00:00", "23:59", location, ResidentActivityKind.Read) }));
                var resident = Asset<ResidentDefinitionAsset>();
                Success(resident.Configure(definition.ResidentId.Value, definition.DisplayName, Color.green, "*", schedule));
                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = definition.ResidentId.Value;
                body.transform.SetParent(root.transform, false);
                body.transform.position = new Vector3(-3 + index++ * 2, 1, 0);
                var name = new GameObject("Name").AddComponent<TextMesh>();
                name.transform.SetParent(body.transform, false);
                var status = new GameObject("Status").AddComponent<TextMesh>();
                status.transform.SetParent(body.transform, false);
                ResidentBlockoutView view = body.AddComponent<ResidentBlockoutView>();
                Success(view.Configure(resident, new[] { body.GetComponent<Renderer>() }, name, status, body.transform));
            }
            root.SetActive(true);
            yield return null;
            Assert.That(bootstrap.IsInitialized, Is.True);
            Success(hud.SelectResident(ResidentIds.Yaya));
            overlay = Child("Overlay").AddComponent<TownDialogueOverlay>();
            canvas = overlay.GetComponentInChildren<Canvas>();
            yield return null;
            Canvas.ForceUpdateCanvases();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) Object.Destroy(root);
            yield return null;
            foreach (ScriptableObject asset in assets) if (asset != null) Object.Destroy(asset);
            assets.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator LongChinese_UsesReadableScreenCanvas_NoClippingOrClickInterception()
        {
            // 300 characters is the supported resident conversation limit. It must
            // remain legible without reducing font size or silently truncating it.
            const string sentence = "今天的胡萝卜需要浇水，池塘里钓到了鱼，果园成熟的水果已经放进公共仓库。";
            string message = string.Concat(Enumerable.Repeat(sentence, 10)).Substring(0, 300);
            TownDialogueOverlay.Publish(ResidentIds.Yaya, message, "readability-long", ResidentIds.Amu, "controlled-test");
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            RectTransform bubble = Bubble(ResidentIds.Yaya);
            Text body = bubble.GetComponentInChildren<Text>();
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(bubble.gameObject.activeInHierarchy, Is.True);
            Assert.That(body.text, Does.Contain(message));
            Assert.That(body.fontSize, Is.EqualTo(24));
            Assert.That(body.resizeTextForBestFit, Is.False);
            Assert.That(body.preferredHeight, Is.LessThanOrEqualTo(body.rectTransform.rect.height + .5f),
                $"A full 300-character Chinese line needs {body.preferredHeight}px but has only {body.rectTransform.rect.height}px.");
            Assert.That(body.horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Wrap));
            foreach (Graphic graphic in bubble.GetComponentsInChildren<Graphic>(true))
                Assert.That(graphic.raycastTarget, Is.False, graphic.name + " must not intercept resident selection.");

            var pointer = new PointerEventData(EventSystem.current)
            { position = RectTransformUtility.WorldToScreenPoint(null, bubble.TransformPoint(bubble.rect.center)) };
            var hits = new List<RaycastResult>();
            canvas.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
            Assert.That(hits.Any(hit => hit.gameObject.transform.IsChildOf(bubble)), Is.False,
                "A real UI raycast through a noninteractive bubble must reach the scene.");

            foreach (Text label in overlay.GetComponentsInChildren<Text>(true))
            {
                Assert.That(label.font, Is.Not.Null, label.name);
                label.font.RequestCharactersInTexture(label.text, label.fontSize, label.fontStyle);
                foreach (char character in label.text.Where(c => c >= '\u4e00' && c <= '\u9fff').Distinct())
                    Assert.That(label.font.HasCharacter(character), Is.True,
                        $"Font for {label.name} has no Chinese glyph '{character}'.");
                Assert.That(label.resizeTextForBestFit, Is.False, label.name);
            }
            Assert.That(overlay.History.Single().text, Is.EqualTo(message));
            Button("HistoryToggle").onClick.Invoke();
            yield return null;
            Text transcript = Text("Transcript");
            Assert.That(transcript.text, Does.Contain(message));
            Assert.That(transcript.preferredHeight, Is.LessThanOrEqualTo(transcript.rectTransform.rect.height + .5f));
            Assert.That(transcript.GetComponentInParent<ScrollRect>(), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator SimultaneousBubbles_DoNotCoverHudOrCommandTarget()
        {
            foreach (ResidentId resident in ResidentIds.TownResidents)
                TownDialogueOverlay.Publish(resident,
                    string.Concat(Enumerable.Repeat("今天检查农田缺水，钓鱼和摘果需要真正等待。", 5)),
                    "layout-" + resident.Value, source: "UI fixture");
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            int visible = 0;
            foreach (ResidentId resident in ResidentIds.TownResidents)
            {
                RectTransform bubble = Bubble(resident);
                if (!bubble.gameObject.activeInHierarchy) continue;
                visible++;
                var rect = new Rect(bubble.anchoredPosition.x, -bubble.anchoredPosition.y,
                    bubble.rect.width, bubble.rect.height);
                Assert.That(rect.Overlaps(new Rect(0, 0, 455, 370)), Is.False, "HUD remains readable");
                Assert.That(rect.Overlaps(new Rect(390, 864, 1140, 108)), Is.False, "Command target remains readable");
            }
            Assert.That(visible, Is.GreaterThan(0));
            Assert.That(overlay.History.Count, Is.EqualTo(4), "Space prioritization must not discard transcript entries");
        }

        [UnityTest]
        public IEnumerator ConversationTurns_StayOrderedAndReadableDuringPauseAndTwentyTimesSpeed()
        {
            Success(bootstrap.Clock.SetTimeScale(20));
            Success(bootstrap.Clock.Pause());
            double pausedGameTime = bootstrap.Clock.ElapsedGameSeconds;
            TownDialogueOverlay.Publish(ResidentIds.Yaya, "我去浇水。", "ordered-pair", ResidentIds.Amu, "controlled-test");
            TownDialogueOverlay.Publish(ResidentIds.Amu, "我去钓鱼。", "ordered-pair", ResidentIds.Yaya, "controlled-test");
            yield return null;
            yield return null;
            RectTransform first = Bubble(ResidentIds.Yaya);
            RectTransform second = Bubble(ResidentIds.Amu);
            Assert.That(first.gameObject.activeInHierarchy, Is.True);
            Assert.That(second.gameObject.activeInHierarchy, Is.False);
            Assert.That(overlay.History.Select(entry => entry.text), Is.EqualTo(new[] { "我去浇水。", "我去钓鱼。" }));
            double firstObservedAt = UnityEngine.Time.realtimeSinceStartupAsDouble;
            while (UnityEngine.Time.realtimeSinceStartupAsDouble - firstObservedAt < 2d)
            {
                Assert.That(first.gameObject.activeInHierarchy, Is.True, "Pause must preserve a readable bubble.");
                Assert.That(second.gameObject.activeInHierarchy, Is.False, "The other participant must wait their turn.");
                Assert.That(bootstrap.Clock.ElapsedGameSeconds, Is.EqualTo(pausedGameTime));
                yield return null;
            }
            Success(bootstrap.Clock.Resume());
            double transitionDeadline = firstObservedAt + 8d;
            while (!second.gameObject.activeInHierarchy && UnityEngine.Time.realtimeSinceStartupAsDouble < transitionDeadline)
            {
                Assert.That(first.gameObject.activeInHierarchy, Is.True);
                yield return null;
            }
            Assert.That(second.gameObject.activeInHierarchy, Is.True, "The second turn must eventually be shown.");
            Assert.That(first.gameObject.activeInHierarchy, Is.False, "Conversation turns must not overlap across speakers.");
            double secondObservedAt = UnityEngine.Time.realtimeSinceStartupAsDouble;
            Assert.That(secondObservedAt - firstObservedAt, Is.GreaterThan(4d),
                "20× simulation speed cannot shorten the first turn to an unreadable flash.");
            Assert.That(bootstrap.Clock.ElapsedGameSeconds, Is.GreaterThan(pausedGameTime + 3600d));
            while (UnityEngine.Time.realtimeSinceStartupAsDouble - secondObservedAt < 4d)
            {
                Assert.That(second.gameObject.activeInHierarchy, Is.True, "Second speaker also needs real reading time.");
                yield return null;
            }
            Assert.That(overlay.History.Count, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator HistoryButtons_FilterBySpeakerOrRecipientWithoutLosingFullMessages()
        {
            const string addressed = "芽芽对阿木说：农田已经浇过水，下一次可以去果园看看。";
            const string privatePair = "小穗对默默说：刚才钓到的鱼已经存入公共仓库。";
            TownDialogueOverlay.Publish(ResidentIds.Yaya, addressed, "farm-pair", ResidentIds.Amu, "controlled-test");
            TownDialogueOverlay.Publish(ResidentIds.Xiaosui, privatePair, "fish-pair", ResidentIds.Momo, "controlled-test");
            yield return null;
            Button("HistoryToggle").onClick.Invoke();
            Button("ResidentFilter").onClick.Invoke();
            yield return null;
            Assert.That(Text("Transcript").text, Does.Contain(addressed));
            Assert.That(Text("Transcript").text, Does.Not.Contain(privatePair));
            Button("Select_" + ResidentIds.Amu.Value).onClick.Invoke();
            yield return null;
            Assert.That(hud.SelectedResidentId, Is.EqualTo(ResidentIds.Amu));
            Assert.That(Text("Transcript").text, Does.Contain(addressed), "Recipients must see their conversation too.");
            Button("Select_" + ResidentIds.Momo.Value).onClick.Invoke();
            yield return null;
            Assert.That(Text("Transcript").text, Does.Contain(privatePair));
            Assert.That(Text("Transcript").text, Does.Not.Contain(addressed));
            Button("ResidentFilter").onClick.Invoke();
            yield return null;
            Assert.That(Text("Transcript").text, Does.Contain(addressed).And.Contain(privatePair));
            Assert.That(Text("Transcript").text, Does.Contain("会话 farm-pair").And.Contain("会话 fish-pair"));
            Assert.That(overlay.History.Select(entry => entry.text), Is.EqualTo(new[] { addressed, privatePair }));
            Button("CloseHistory").onClick.Invoke();
            Assert.That(Text("Transcript").gameObject.activeInHierarchy, Is.False);
            Button("HistoryToggle").onClick.Invoke();
            Assert.That(Text("Transcript").text, Does.Contain(addressed).And.Contain(privatePair));
        }

        private T Asset<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            assets.Add(asset);
            return asset;
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            return child;
        }

        private RectTransform Bubble(ResidentId resident) => overlay.GetComponentsInChildren<RectTransform>(true)
            .Single(rect => rect.name == "Bubble_" + resident.Value);
        private Button Button(string name) => overlay.GetComponentsInChildren<Button>(true).Single(button => button.name == name);
        private Text Text(string name) => overlay.GetComponentsInChildren<Text>(true).Single(text => text.name == name);
        private static void Success(ActionResult result) => Assert.That(result.Succeeded, Is.True, result.Message);
    }
}
