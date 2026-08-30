using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Presentation;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AIFarm.Editor
{
    public static class DemoSceneBuilder
    {
        public const string ScenePath = "Assets/AIFarm/Scenes/DemoScene.unity";
        public const string InventoryConfigPath = "Assets/AIFarm/Config/DemoInventoryConfig.asset";
        public const string SceneConfigPath = "Assets/AIFarm/Config/DemoSceneConfig.asset";

        private const string MaterialFolder = "Assets/AIFarm/Art/Materials";
        private static readonly Vector3 CameraPosition = new Vector3(10f, 12f, -12f);
        private static readonly Vector3 CameraTarget = new Vector3(0f, 0f, 0.5f);

        [MenuItem("Tools/AIFarm/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            if (!BuildDemoScene(promptToSaveCurrentScenes: true))
            {
                return;
            }

            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Selection.activeObject = sceneAsset;
            EditorGUIUtility.PingObject(sceneAsset);
            Debug.Log($"AIFarm demo scene rebuilt at {ScenePath}.");
        }

        public static bool BuildDemoScene(bool promptToSaveCurrentScenes)
        {
            if (promptToSaveCurrentScenes && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }

            EnsureFolder("Assets/AIFarm/Scenes");
            EnsureFolder("Assets/AIFarm/Config");
            EnsureFolder(MaterialFolder);

            DemoInventoryConfig inventoryConfig = GetOrCreateInventoryConfig();
            DemoSceneConfig sceneConfig = GetOrCreateSceneConfig(inventoryConfig);
            Material groundMaterial = GetOrCreateMaterial(
                $"{MaterialFolder}/Ground_Blockout.mat",
                new Color(0.18f, 0.32f, 0.16f));
            Material plotMaterial = GetOrCreateMaterial(
                $"{MaterialFolder}/Plot_Soil_Blockout.mat",
                new Color(0.38f, 0.20f, 0.08f));
            Material npcMaterial = GetOrCreateMaterial(
                $"{MaterialFolder}/NPC_Blockout.mat",
                new Color(0.95f, 0.55f, 0.12f));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateEnvironment(groundMaterial);
            PlotInteractionPoint[] interactionPoints = CreateFarm(sceneConfig, plotMaterial);
            CreateLighting();
            CreateCamera();
            GameBootstrap bootstrap = CreateBootstrap(sceneConfig);
            NpcPlanExecutor executor = CreateNpc(
                npcMaterial,
                plotMaterial,
                bootstrap,
                interactionPoints);
            CreateUi(bootstrap, executor);
            NavMeshSurface navMeshSurface = CreateNavigation();
            navMeshSurface.BuildNavMesh();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new System.InvalidOperationException($"Unable to save demo scene to {ScenePath}.");
            }

            AssetDatabase.SaveAssets();
            return true;
        }

        private static DemoInventoryConfig GetOrCreateInventoryConfig()
        {
            DemoInventoryConfig config = AssetDatabase.LoadAssetAtPath<DemoInventoryConfig>(InventoryConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<DemoInventoryConfig>();
                config.name = "DemoInventoryConfig";
                AssetDatabase.CreateAsset(config, InventoryConfigPath);
            }

            config.Configure(seedCount: 9, waterCount: 9, fertilizerCount: 9, carrotCount: 0);
            EditorUtility.SetDirty(config);
            return config;
        }

        private static DemoSceneConfig GetOrCreateSceneConfig(DemoInventoryConfig inventoryConfig)
        {
            DemoSceneConfig config = AssetDatabase.LoadAssetAtPath<DemoSceneConfig>(SceneConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<DemoSceneConfig>();
                config.name = "DemoSceneConfig";
                AssetDatabase.CreateAsset(config, SceneConfigPath);
            }

            config.Configure(
                inventoryConfig,
                day: 1,
                hour: 8,
                scale: 60f,
                size: 2f,
                spacing: 0.35f);
            EditorUtility.SetDirty(config);
            return config;
        }

        private static Material GetOrCreateMaterial(string path, Color color)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader == null)
                {
                    throw new System.InvalidOperationException("No supported blockout shader is available.");
                }

                material = new Material(shader)
                {
                    name = System.IO.Path.GetFileNameWithoutExtension(path)
                };
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreateEnvironment(Material groundMaterial)
        {
            var environment = new GameObject("Environment");
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground_Blockout";
            ground.transform.SetParent(environment.transform, false);
            ground.transform.position = new Vector3(0f, -0.3f, 0.5f);
            ground.transform.localScale = new Vector3(18f, 0.5f, 14f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
        }

        private static PlotInteractionPoint[] CreateFarm(DemoSceneConfig config, Material plotMaterial)
        {
            var farmRoot = new GameObject("Farm_3x3");
            var plotsRoot = new GameObject("Plots_1_to_9");
            var labelsRoot = new GameObject("Plot_Number_Labels");
            var interactionRoot = new GameObject("Plot_Interaction_Points");
            plotsRoot.transform.SetParent(farmRoot.transform, false);
            labelsRoot.transform.SetParent(farmRoot.transform, false);
            interactionRoot.transform.SetParent(farmRoot.transform, false);

            float stride = config.PlotSize + config.PlotSpacing;
            var interactionPoints = new PlotInteractionPoint[FarmField.PlotCount];
            for (int row = 0; row < FarmField.RowCount; row++)
            {
                for (int column = 0; column < FarmField.ColumnCount; column++)
                {
                    int plotNumber = row * FarmField.ColumnCount + column + 1;
                    Vector3 position = new Vector3(
                        (column - 1) * stride,
                        0f,
                        (row - 1) * stride);

                    GameObject plot = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    plot.name = $"Plot_{plotNumber:00}";
                    plot.transform.SetParent(plotsRoot.transform, false);
                    plot.transform.position = position;
                    plot.transform.localScale = new Vector3(config.PlotSize, 0.28f, config.PlotSize);
                    plot.GetComponent<Renderer>().sharedMaterial = plotMaterial;
                    NavMeshModifier modifier = plot.AddComponent<NavMeshModifier>();
                    modifier.ignoreFromBuild = true;

                    CreatePlotLabel(labelsRoot.transform, plotNumber, position + Vector3.up * 0.45f);
                    interactionPoints[plotNumber - 1] = CreatePlotInteractionPoint(
                        interactionRoot.transform,
                        plot.transform,
                        plotNumber,
                        position + new Vector3(0f, -0.04f, -config.PlotSize * 0.42f));
                }
            }

            return interactionPoints;
        }

        private static PlotInteractionPoint CreatePlotInteractionPoint(
            Transform parent,
            Transform plot,
            int plotNumber,
            Vector3 position)
        {
            var interactionObject = new GameObject($"InteractionPoint_{plotNumber:00}");
            interactionObject.transform.SetParent(parent, false);
            interactionObject.transform.position = position;
            PlotInteractionPoint interactionPoint = interactionObject.AddComponent<PlotInteractionPoint>();
            EnsureSucceeded(interactionPoint.Configure(plotNumber, plot));
            return interactionPoint;
        }

        private static void CreatePlotLabel(Transform parent, int plotNumber, Vector3 position)
        {
            var labelObject = new GameObject($"PlotLabel_{plotNumber:00}");
            labelObject.transform.SetParent(parent, false);
            labelObject.transform.position = position;
            labelObject.transform.rotation = Quaternion.LookRotation(position - CameraPosition, Vector3.up);

            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = plotNumber.ToString("00");
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 64;
            label.characterSize = 0.08f;
            label.color = Color.white;
        }

        private static NpcPlanExecutor CreateNpc(
            Material npcMaterial,
            Material feedbackBackgroundMaterial,
            GameBootstrap bootstrap,
            PlotInteractionPoint[] interactionPoints)
        {
            var npc = new GameObject("NPC_Blockout_Capsule");
            npc.name = "NPC_Blockout_Capsule";
            npc.transform.position = new Vector3(-5f, -0.04f, 0f);

            CapsuleCollider capsuleCollider = npc.AddComponent<CapsuleCollider>();
            capsuleCollider.center = new Vector3(0f, 1.2f, 0f);
            capsuleCollider.height = 2.4f;
            capsuleCollider.radius = 0.6f;

            NavMeshAgent agent = npc.AddComponent<NavMeshAgent>();
            agent.height = 2.4f;
            agent.radius = 0.45f;
            agent.speed = 3.5f;
            agent.angularSpeed = 720f;
            agent.acceleration = 20f;
            agent.stoppingDistance = 0.05f;
            agent.autoBraking = true;

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "NPC_Visual_Capsule";
            visual.transform.SetParent(npc.transform, false);
            visual.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            visual.transform.localScale = Vector3.one * 1.2f;
            visual.GetComponent<Renderer>().sharedMaterial = npcMaterial;
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            CreateFeedbackBar(
                npc.transform,
                npcMaterial,
                feedbackBackgroundMaterial,
                out GameObject progressRoot,
                out Transform progressFill);

            NpcNavigator navigator = npc.AddComponent<NpcNavigator>();
            EnsureSucceeded(navigator.Configure(agent, interactionPoints, requireNavMesh: true, movementSpeed: 3.5f));

            BlockoutActionFeedback feedback = npc.AddComponent<BlockoutActionFeedback>();
            EnsureSucceeded(feedback.Configure(visual.transform, progressRoot, progressFill));

            NpcPlanExecutor executor = npc.AddComponent<NpcPlanExecutor>();
            EnsureSucceeded(executor.Configure(bootstrap, navigator, feedback));
            return executor;
        }

        private static void CreateFeedbackBar(
            Transform parent,
            Material fillMaterial,
            Material backgroundMaterial,
            out GameObject progressRoot,
            out Transform progressFill)
        {
            progressRoot = new GameObject("ActionFeedback_ProgressBar");
            progressRoot.transform.SetParent(parent, false);
            progressRoot.transform.localPosition = new Vector3(0f, 2.8f, 0f);

            GameObject background = GameObject.CreatePrimitive(PrimitiveType.Cube);
            background.name = "ProgressBar_Background";
            background.transform.SetParent(progressRoot.transform, false);
            background.transform.localScale = new Vector3(1.5f, 0.16f, 0.08f);
            background.GetComponent<Renderer>().sharedMaterial = backgroundMaterial;
            Object.DestroyImmediate(background.GetComponent<Collider>());

            GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = "ProgressBar_Fill";
            fill.transform.SetParent(progressRoot.transform, false);
            fill.transform.localPosition = new Vector3(0f, 0f, -0.07f);
            fill.transform.localScale = new Vector3(1.36f, 0.1f, 0.05f);
            fill.GetComponent<Renderer>().sharedMaterial = fillMaterial;
            Object.DestroyImmediate(fill.GetComponent<Collider>());
            progressFill = fill.transform;
        }

        private static NavMeshSurface CreateNavigation()
        {
            var navigationObject = new GameObject("Navigation_NavMeshSurface");
            NavMeshSurface surface = navigationObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.ignoreNavMeshAgent = true;
            surface.ignoreNavMeshObstacle = true;
            return surface;
        }

        private static void CreateLighting()
        {
            var lightObject = new GameObject("DirectionalLight_Key");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.color = new Color(1f, 0.95f, 0.84f);
            lightObject.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("MainCamera_TopOblique");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = CameraPosition;
            cameraObject.transform.rotation = Quaternion.LookRotation(CameraTarget - CameraPosition, Vector3.up);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.52f, 0.72f, 0.82f);
            cameraObject.AddComponent<AudioListener>();
        }

        private static GameBootstrap CreateBootstrap(DemoSceneConfig sceneConfig)
        {
            var bootstrapObject = new GameObject("GameBootstrap");
            GameBootstrap bootstrap = bootstrapObject.AddComponent<GameBootstrap>();
            bootstrap.Configure(sceneConfig);
            return bootstrap;
        }

        private static void CreateUi(GameBootstrap bootstrap, NpcPlanExecutor executor)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject canvasObject = new GameObject(
                "UI_Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject statusPanel = CreatePanel(
                "StatusPanel",
                canvasObject.transform,
                new Color(0.04f, 0.07f, 0.06f, 0.88f));
            SetRect(
                statusPanel.GetComponent<RectTransform>(),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(20f, -20f),
                new Vector2(380f, 280f));

            Text titleText = CreateText("TitleText", statusPanel.transform, "AI FARM // BLOCKOUT", font, 28, FontStyle.Bold);
            SetTopRow(titleText.rectTransform, -16f, 42f);

            Text timeText = CreateText("TimeText", statusPanel.transform, "Day 1  08:00", font, 24, FontStyle.Bold);
            SetTopRow(timeText.rectTransform, -62f, 34f);

            Text inventoryText = CreateText(
                "InventoryText",
                statusPanel.transform,
                "Seeds: 9\nWater: 9\nFertilizer: 9\nCarrots: 0",
                font,
                21,
                FontStyle.Normal);
            SetTopRow(inventoryText.rectTransform, -102f, 108f);
            inventoryText.alignment = TextAnchor.UpperLeft;

            Text goalText = CreateText("GoalText", statusPanel.transform, "Goal: Idle", font, 19, FontStyle.Normal);
            SetTopRow(goalText.rectTransform, -214f, 28f);

            Text actionText = CreateText("ActionText", statusPanel.transform, "Action: Idle", font, 19, FontStyle.Normal);
            SetTopRow(actionText.rectTransform, -246f, 28f);

            GameObject commandPanel = CreatePanel(
                "CommandPanel",
                canvasObject.transform,
                new Color(0.04f, 0.07f, 0.06f, 0.9f));
            SetRect(
                commandPanel.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 20f),
                new Vector2(920f, 92f));

            InputField commandInput = CreateInputField(commandPanel.transform, font);
            SetRect(
                commandInput.GetComponent<RectTransform>(),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(20f, 0f),
                new Vector2(680f, 54f));

            Button submitButton = CreateButton(commandPanel.transform, font);
            SetRect(
                submitButton.GetComponent<RectTransform>(),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-20f, 0f),
                new Vector2(180f, 54f));

            DemoHud hud = canvasObject.AddComponent<DemoHud>();
            hud.Configure(
                bootstrap,
                timeText,
                inventoryText,
                goalText,
                actionText,
                commandInput,
                submitButton,
                executor);

            var eventSystemObject = new GameObject("EventSystem_InputSystem");
            eventSystemObject.AddComponent<EventSystem>();
            InputSystemUIInputModule inputModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
        }

        private static GameObject CreatePanel(string name, Transform parent, Color color)
        {
            GameObject panel = CreateUiObject(name, parent);
            Image image = panel.AddComponent<Image>();
            image.color = color;
            return panel;
        }

        private static Text CreateText(
            string name,
            Transform parent,
            string content,
            Font font,
            int fontSize,
            FontStyle fontStyle)
        {
            GameObject textObject = CreateUiObject(name, parent);
            Text text = textObject.AddComponent<Text>();
            text.text = content;
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            return text;
        }

        private static InputField CreateInputField(Transform parent, Font font)
        {
            GameObject inputObject = CreateUiObject("CommandInput", parent);
            Image background = inputObject.AddComponent<Image>();
            background.color = new Color(0.93f, 0.95f, 0.91f, 1f);

            Text inputText = CreateText("Text", inputObject.transform, string.Empty, font, 21, FontStyle.Normal);
            inputText.color = new Color(0.08f, 0.10f, 0.09f);
            StretchRect(inputText.rectTransform, 16f, 12f);

            Text placeholder = CreateText(
                "Placeholder",
                inputObject.transform,
                "Atomic command: sow 1 / 播种 1 (also water, fertilize, weed, harvest)",
                font,
                20,
                FontStyle.Italic);
            placeholder.color = new Color(0.35f, 0.39f, 0.36f, 0.75f);
            StretchRect(placeholder.rectTransform, 16f, 12f);

            InputField inputField = inputObject.AddComponent<InputField>();
            inputField.targetGraphic = background;
            inputField.textComponent = inputText;
            inputField.placeholder = placeholder;
            inputField.lineType = InputField.LineType.SingleLine;
            return inputField;
        }

        private static Button CreateButton(Transform parent, Font font)
        {
            GameObject buttonObject = CreateUiObject("SubmitButton", parent);
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.25f, 0.65f, 0.36f, 1f);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;

            Text label = CreateText("Label", buttonObject.transform, "SUBMIT", font, 20, FontStyle.Bold);
            label.alignment = TextAnchor.MiddleCenter;
            StretchRect(label.rectTransform, 4f, 4f);
            return button;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static void SetTopRow(RectTransform rect, float y, float height)
        {
            SetRect(
                rect,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, y),
                new Vector2(-32f, height));
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static void StretchRect(RectTransform rect, float horizontalInset, float verticalInset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(horizontalInset, verticalInset);
            rect.offsetMax = new Vector2(-horizontalInset, -verticalInset);
        }

        private static void EnsureFolder(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = $"{current}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }

        private static void EnsureSucceeded(ActionResult result)
        {
            if (result.Failed)
            {
                throw new System.InvalidOperationException(result.Message);
            }
        }
    }
}
