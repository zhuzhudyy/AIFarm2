using System.Collections.Generic;
using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Npc;
using AIFarm.Presentation;
using AIFarm.Town;
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
        public const string ResidentPrefabPath = "Assets/AIFarm/Prefabs/Resident_Blockout.prefab";

        public const string ResidentConfigFolder = "Assets/AIFarm/Config/Residents";
        public const string ScheduleConfigFolder = "Assets/AIFarm/Config/Schedules";
        public const string LocationConfigFolder = "Assets/AIFarm/Config/TownLocations";

        private const string MaterialFolder = "Assets/AIFarm/Art/Materials";
        private static readonly Vector3 CameraPosition = new Vector3(12f, 15f, -16f);
        private static readonly Vector3 CameraTarget = new Vector3(0f, 0f, 0.5f);

        private sealed class TownLocationBuildSpec
        {
            public TownLocationBuildSpec(
                string id,
                string assetName,
                string objectName,
                string displayName,
                Vector3 position,
                Vector3 scale,
                PrimitiveType primitiveType = PrimitiveType.Cube,
                bool blocksNavigation = true)
            {
                Id = id;
                AssetName = assetName;
                ObjectName = objectName;
                DisplayName = displayName;
                Position = position;
                Scale = scale;
                PrimitiveType = primitiveType;
                BlocksNavigation = blocksNavigation;
            }

            public string Id { get; }

            public string AssetName { get; }

            public string ObjectName { get; }

            public string DisplayName { get; }

            public Vector3 Position { get; }

            public Vector3 Scale { get; }

            public PrimitiveType PrimitiveType { get; }

            public bool BlocksNavigation { get; }
        }

        private sealed class ResidentBuildSpec
        {
            public ResidentBuildSpec(
                ResidentDefinition definition,
                string assetName,
                string sceneObjectName,
                string statusIcon,
                Color color,
                string homeLocationId,
                string morningLocationId,
                string afternoonLocationId)
            {
                Definition = definition;
                AssetName = assetName;
                SceneObjectName = sceneObjectName;
                StatusIcon = statusIcon;
                Color = color;
                HomeLocationId = homeLocationId;
                MorningLocationId = morningLocationId;
                AfternoonLocationId = afternoonLocationId;
            }

            public ResidentDefinition Definition { get; }

            public string AssetName { get; }

            public string SceneObjectName { get; }

            public string StatusIcon { get; }

            public Color Color { get; }

            public string HomeLocationId { get; }

            public string MorningLocationId { get; }

            public string AfternoonLocationId { get; }
        }

        private static readonly TownLocationBuildSpec[] TownLocationSpecs =
        {
            new TownLocationBuildSpec(
                "location-workshop", "Workshop", "Workshop_Blockout", "工坊",
                new Vector3(-7.5f, 0.65f, 2.4f), new Vector3(2.6f, 1.4f, 2.2f)),
            new TownLocationBuildSpec(
                "location-cafeteria", "Cafeteria", "Cafeteria_Blockout", "食堂",
                new Vector3(-7.5f, 0.65f, -2.4f), new Vector3(2.6f, 1.4f, 2.2f)),
            new TownLocationBuildSpec(
                "location-library", "Library", "Library_Blockout", "图书馆",
                new Vector3(7.5f, 0.65f, 2.4f), new Vector3(2.6f, 1.4f, 2.2f)),
            new TownLocationBuildSpec(
                "location-plaza", "Plaza", "Plaza_Blockout", "广场",
                new Vector3(7.1f, 0.04f, -2.8f), new Vector3(3.4f, 0.12f, 3.0f),
                PrimitiveType.Cube, blocksNavigation: false),
            new TownLocationBuildSpec(
                "location-well", "Well", "Well_Blockout", "水井",
                new Vector3(0f, 0.45f, -6.2f), new Vector3(1.5f, 0.9f, 1.5f),
                PrimitiveType.Cylinder),
            new TownLocationBuildSpec(
                "location-home-yaya", "Home_Yaya", "Home_Yaya_Blockout", "芽芽的家",
                new Vector3(-7.2f, 0.6f, 6.2f), new Vector3(2.2f, 1.25f, 1.8f)),
            new TownLocationBuildSpec(
                "location-home-amu", "Home_Amu", "Home_Amu_Blockout", "阿木的家",
                new Vector3(-2.4f, 0.6f, 6.2f), new Vector3(2.2f, 1.25f, 1.8f)),
            new TownLocationBuildSpec(
                "location-home-xiaosui", "Home_Xiaosui", "Home_Xiaosui_Blockout", "小穗的家",
                new Vector3(2.4f, 0.6f, 6.2f), new Vector3(2.2f, 1.25f, 1.8f)),
            new TownLocationBuildSpec(
                "location-home-momo", "Home_Momo", "Home_Momo_Blockout", "墨墨的家",
                new Vector3(7.2f, 0.6f, 6.2f), new Vector3(2.2f, 1.25f, 1.8f))
        };

        private static readonly ResidentBuildSpec[] ResidentSpecs =
        {
            new ResidentBuildSpec(
                ResidentDefinition.Yaya, "Yaya", "NPC_Blockout_Capsule", "Y",
                new Color(0.31f, 0.78f, 0.34f),
                "location-home-yaya", "location-workshop", "location-well"),
            new ResidentBuildSpec(
                ResidentDefinition.Amu, "Amu", "Resident_Amu", "A",
                new Color(0.88f, 0.46f, 0.17f),
                "location-home-amu", "location-workshop", "location-workshop"),
            new ResidentBuildSpec(
                ResidentDefinition.Xiaosui, "Xiaosui", "Resident_Xiaosui", "S",
                new Color(0.93f, 0.74f, 0.20f),
                "location-home-xiaosui", "location-library", "location-library"),
            new ResidentBuildSpec(
                ResidentDefinition.Momo, "Momo", "Resident_Momo", "M",
                new Color(0.43f, 0.39f, 0.80f),
                "location-home-momo", "location-well", "location-plaza")
        };

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
            EnsureFolder("Assets/AIFarm/Prefabs");
            EnsureFolder(ResidentConfigFolder);
            EnsureFolder(ScheduleConfigFolder);
            EnsureFolder(LocationConfigFolder);
            EnsureFolder(MaterialFolder);

            DemoInventoryConfig inventoryConfig = GetOrCreateInventoryConfig();
            DemoSceneConfig sceneConfig = GetOrCreateSceneConfig(inventoryConfig);
            TownLocationDefinitionAsset[] locationAssets = GetOrCreateLocationAssets();
            DailyScheduleDefinitionAsset[] scheduleAssets =
                GetOrCreateScheduleAssets(locationAssets);
            ResidentDefinitionAsset[] residentAssets =
                GetOrCreateResidentAssets(scheduleAssets);
            Material groundMaterial = GetOrCreateMaterial(
                $"{MaterialFolder}/Ground_Blockout.mat",
                new Color(0.18f, 0.32f, 0.16f));
            Material plotMaterial = GetOrCreateMaterial(
                $"{MaterialFolder}/Plot_Soil_Blockout.mat",
                new Color(0.38f, 0.20f, 0.08f));
            Material townMaterial = GetOrCreateMaterial(
                $"{MaterialFolder}/Town_Building_Blockout.mat",
                new Color(0.62f, 0.48f, 0.31f));
            Material townAccentMaterial = GetOrCreateMaterial(
                $"{MaterialFolder}/Town_Accent_Blockout.mat",
                new Color(0.84f, 0.76f, 0.52f));
            Material[] residentMaterials = GetOrCreateResidentMaterials();
            GameObject residentPrefab = GetOrCreateResidentPrefab(residentMaterials[0]);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateEnvironment(groundMaterial);
            GameBootstrap bootstrap = CreateBootstrap(sceneConfig);
            PlotInteractionPoint[] interactionPoints = CreateFarm(sceneConfig, plotMaterial, bootstrap);
            LocationArrivalPoint[] arrivalPoints = CreateTownLocations(
                locationAssets,
                townMaterial,
                townAccentMaterial,
                out ConversationAnchor[] conversationAnchors);
            CreateLighting();
            CreateCamera();
            ReplanController replanController = bootstrap.gameObject.AddComponent<ReplanController>();
            TownResidentScheduleController[] residentControllers = CreateResidents(
                residentPrefab,
                residentAssets,
                residentMaterials,
                plotMaterial,
                bootstrap,
                interactionPoints,
                arrivalPoints,
                replanController,
                out NpcPlanExecutor executor);
            EnsureSucceeded(replanController.Configure(bootstrap, executor));
            TownScheduleCoordinator coordinator =
                bootstrap.gameObject.AddComponent<TownScheduleCoordinator>();
            EnsureSucceeded(coordinator.Configure(
                bootstrap,
                scheduleAssets,
                arrivalPoints,
                residentControllers));
            TownSocialCoordinator socialCoordinator =
                bootstrap.gameObject.AddComponent<TownSocialCoordinator>();
            EnsureSucceeded(socialCoordinator.Configure(
                bootstrap,
                coordinator,
                residentControllers,
                conversationAnchors));
            TownEventSceneCoordinator eventCoordinator =
                bootstrap.gameObject.AddComponent<TownEventSceneCoordinator>();
            EnsureSucceeded(eventCoordinator.Configure(
                bootstrap,
                coordinator,
                socialCoordinator,
                residentControllers,
                GetHarvestDinnerAttendancePoints(arrivalPoints)));
            CreateUi(bootstrap, executor, replanController, executor.transform);
            NavMeshSurface navMeshSurface = CreateNavigation();
            navMeshSurface.BuildNavMesh();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new System.InvalidOperationException($"Unable to save demo scene to {ScenePath}.");
            }

            AssetDatabase.SaveAssets();
            return true;
        }

        private static LocationArrivalPoint[] GetHarvestDinnerAttendancePoints(
            LocationArrivalPoint[] arrivalPoints)
        {
            var points = new List<LocationArrivalPoint>();
            foreach (LocationArrivalPoint point in arrivalPoints)
            {
                if (point != null && point.LocationId.Value == "location-plaza" &&
                    point.InteractionPointId.StartsWith("location-plaza-point-"))
                {
                    points.Add(point);
                }
            }

            points.Sort((left, right) => string.CompareOrdinal(
                left.InteractionPointId,
                right.InteractionPointId));
            if (points.Count != ResidentSpecs.Length)
            {
                throw new System.InvalidOperationException(
                    "HarvestDinner requires exactly four ordinary plaza arrival points.");
            }

            return points.ToArray();
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
                scale: 20f,
                size: 2f,
                spacing: 0.35f,
                waterDecaySeconds: 10f,
                weedDelaySeconds: 15f,
                maturitySeconds: 30f,
                actionSeconds: 0.2f,
                waitSeconds: 0.2f,
                expressionCooldown: 12f,
                expressionDisplay: 2.5f,
                gatewayMode: AiGatewayMode.Remote,
                gatewayBaseUrl: config.AiGatewayBaseUrl,
                requestTimeoutSeconds: config.AiRequestTimeoutSeconds);
            EditorUtility.SetDirty(config);
            return config;
        }

        private static TownLocationDefinitionAsset[] GetOrCreateLocationAssets()
        {
            var assets = new TownLocationDefinitionAsset[TownLocationSpecs.Length];
            for (int index = 0; index < TownLocationSpecs.Length; index++)
            {
                TownLocationBuildSpec spec = TownLocationSpecs[index];
                string path = $"{LocationConfigFolder}/TownLocation_{spec.AssetName}.asset";
                TownLocationDefinitionAsset asset =
                    AssetDatabase.LoadAssetAtPath<TownLocationDefinitionAsset>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<TownLocationDefinitionAsset>();
                    asset.name = $"TownLocation_{spec.AssetName}";
                    AssetDatabase.CreateAsset(asset, path);
                }

                EnsureSucceeded(asset.Configure(spec.Id, spec.DisplayName));
                EditorUtility.SetDirty(asset);
                assets[index] = asset;
            }

            return assets;
        }

        private static DailyScheduleDefinitionAsset[] GetOrCreateScheduleAssets(
            TownLocationDefinitionAsset[] locations)
        {
            var assets = new DailyScheduleDefinitionAsset[ResidentSpecs.Length];
            for (int index = 0; index < ResidentSpecs.Length; index++)
            {
                ResidentBuildSpec spec = ResidentSpecs[index];
                string path = $"{ScheduleConfigFolder}/DailySchedule_{spec.AssetName}.asset";
                DailyScheduleDefinitionAsset asset =
                    AssetDatabase.LoadAssetAtPath<DailyScheduleDefinitionAsset>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<DailyScheduleDefinitionAsset>();
                    asset.name = $"DailySchedule_{spec.AssetName}";
                    AssetDatabase.CreateAsset(asset, path);
                }

                bool isHarvestStoryPair =
                    spec.Definition.ResidentId == ResidentIds.Yaya ||
                    spec.Definition.ResidentId == ResidentIds.Xiaosui;
                string lateAfternoonLocationId = isHarvestStoryPair
                    ? "location-plaza"
                    : spec.AfternoonLocationId;
                ResidentActivityKind lateAfternoonActivity = isHarvestStoryPair
                    ? ResidentActivityKind.Gather
                    : ResidentActivityKind.Work;
                var slots = new[]
                {
                    CreateScheduleSlot("00:00", "07:00", spec.HomeLocationId, ResidentActivityKind.Home, locations),
                    CreateScheduleSlot("07:00", "08:00", "location-cafeteria", ResidentActivityKind.Meal, locations),
                    CreateScheduleSlot("08:00", "12:00", spec.MorningLocationId, ResidentActivityKind.Work, locations),
                    CreateScheduleSlot("12:00", "13:00", "location-cafeteria", ResidentActivityKind.Meal, locations),
                    CreateScheduleSlot("13:00", "17:00", spec.AfternoonLocationId, ResidentActivityKind.Work, locations),
                    CreateScheduleSlot(
                        "17:00",
                        "18:00",
                        lateAfternoonLocationId,
                        lateAfternoonActivity,
                        locations),
                    CreateScheduleSlot("18:00", "19:00", "location-well", ResidentActivityKind.FetchWater, locations),
                    CreateScheduleSlot("19:00", "24:00", spec.HomeLocationId, ResidentActivityKind.Home, locations)
                };
                EnsureSucceeded(asset.Configure(spec.Definition.ResidentId.Value, slots));
                EditorUtility.SetDirty(asset);
                assets[index] = asset;
            }

            return assets;
        }

        private static DailyScheduleSlotAsset CreateScheduleSlot(
            string startTime,
            string endTime,
            string locationId,
            ResidentActivityKind activity,
            TownLocationDefinitionAsset[] locations)
        {
            TownLocationDefinitionAsset location = FindLocationAsset(locations, locationId);
            if (location == null)
            {
                throw new System.InvalidOperationException(
                    $"No town location asset exists for '{locationId}'.");
            }

            return new DailyScheduleSlotAsset(startTime, endTime, location, activity);
        }

        private static ResidentDefinitionAsset[] GetOrCreateResidentAssets(
            DailyScheduleDefinitionAsset[] schedules)
        {
            var assets = new ResidentDefinitionAsset[ResidentSpecs.Length];
            for (int index = 0; index < ResidentSpecs.Length; index++)
            {
                ResidentBuildSpec spec = ResidentSpecs[index];
                string path = $"{ResidentConfigFolder}/ResidentDefinition_{spec.AssetName}.asset";
                ResidentDefinitionAsset asset =
                    AssetDatabase.LoadAssetAtPath<ResidentDefinitionAsset>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<ResidentDefinitionAsset>();
                    asset.name = $"ResidentDefinition_{spec.AssetName}";
                    AssetDatabase.CreateAsset(asset, path);
                }

                EnsureSucceeded(asset.Configure(
                    spec.Definition.ResidentId.Value,
                    spec.Definition.DisplayName,
                    spec.Color,
                    spec.StatusIcon,
                    schedules[index]));
                EditorUtility.SetDirty(asset);
                assets[index] = asset;
            }

            return assets;
        }

        private static Material[] GetOrCreateResidentMaterials()
        {
            var materials = new Material[ResidentSpecs.Length];
            for (int index = 0; index < ResidentSpecs.Length; index++)
            {
                ResidentBuildSpec spec = ResidentSpecs[index];
                materials[index] = GetOrCreateMaterial(
                    $"{MaterialFolder}/Resident_{spec.AssetName}_Blockout.mat",
                    spec.Color);
            }

            return materials;
        }

        private static GameObject GetOrCreateResidentPrefab(Material defaultMaterial)
        {
            var root = new GameObject("Resident_Blockout");
            try
            {
                CapsuleCollider capsuleCollider = root.AddComponent<CapsuleCollider>();
                capsuleCollider.center = new Vector3(0f, 1.2f, 0f);
                capsuleCollider.height = 2.4f;
                capsuleCollider.radius = 0.6f;

                NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
                agent.height = 2.4f;
                agent.radius = 0.45f;
                agent.speed = 3.2f;
                agent.angularSpeed = 720f;
                agent.acceleration = 20f;
                agent.stoppingDistance = 0.08f;
                agent.autoBraking = true;

                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                visual.name = "NPC_Visual_Capsule";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = new Vector3(0f, 1.2f, 0f);
                visual.transform.localScale = Vector3.one * 1.2f;
                visual.GetComponent<Renderer>().sharedMaterial = defaultMaterial;
                Object.DestroyImmediate(visual.GetComponent<Collider>());

                GameObject accent = GameObject.CreatePrimitive(PrimitiveType.Cube);
                accent.name = "Resident_Accent_Block";
                accent.transform.SetParent(visual.transform, false);
                accent.transform.localPosition = new Vector3(0f, 0.55f, 0.48f);
                accent.transform.localScale = new Vector3(0.55f, 0.2f, 0.18f);
                accent.GetComponent<Renderer>().sharedMaterial = defaultMaterial;
                Object.DestroyImmediate(accent.GetComponent<Collider>());

                TextMesh nameLabel = CreateResidentWorldLabel(
                    "Resident_Name_Label",
                    root.transform,
                    "居民",
                    new Vector3(0f, 3.0f, 0f),
                    56,
                    0.065f);
                nameLabel.color = Color.white;
                TextMesh statusIcon = CreateResidentWorldLabel(
                    "Resident_Status_Icon",
                    root.transform,
                    "?.",
                    new Vector3(0f, 3.45f, 0f),
                    54,
                    0.065f);
                statusIcon.color = new Color(1f, 0.95f, 0.58f);
                TextMesh conversationText = CreateResidentWorldLabel(
                    "Resident_Conversation_Label",
                    root.transform,
                    string.Empty,
                    new Vector3(0f, 4.05f, 0f),
                    42,
                    0.045f);
                conversationText.anchor = TextAnchor.LowerCenter;
                conversationText.color = Color.white;
                conversationText.gameObject.SetActive(false);

                root.AddComponent<ResidentBlockoutView>();
                root.AddComponent<TownResidentNavigator>();
                root.AddComponent<TownResidentScheduleController>();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ResidentPrefabPath);
                if (saved == null)
                {
                    throw new System.InvalidOperationException(
                        $"Unable to save resident prefab at {ResidentPrefabPath}.");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(ResidentPrefabPath);
        }

        private static TextMesh CreateResidentWorldLabel(
            string name,
            Transform parent,
            string content,
            Vector3 localPosition,
            int fontSize,
            float characterSize)
        {
            var labelObject = new GameObject(name);
            labelObject.transform.SetParent(parent, false);
            labelObject.transform.localPosition = localPosition;
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = content;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = fontSize;
            label.characterSize = characterSize;
            labelObject.AddComponent<WorldSpaceBillboard>();
            return label;
        }

        private static TownLocationDefinitionAsset FindLocationAsset(
            TownLocationDefinitionAsset[] locations,
            string locationId)
        {
            foreach (TownLocationDefinitionAsset location in locations)
            {
                if (location != null && location.LocationIdValue == locationId)
                {
                    return location;
                }
            }

            return null;
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
            ground.transform.localScale = new Vector3(22f, 0.5f, 16f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
        }

        private static PlotInteractionPoint[] CreateFarm(
            DemoSceneConfig config,
            Material plotMaterial,
            GameBootstrap bootstrap)
        {
            var farmRoot = new GameObject("Farm_3x3");
            var plotsRoot = new GameObject("Plots_1_to_9");
            var labelsRoot = new GameObject("Plot_Number_Labels");
            var interactionRoot = new GameObject("Plot_Interaction_Points");
            var stateVisualsRoot = new GameObject("Plot_State_Visuals");
            plotsRoot.transform.SetParent(farmRoot.transform, false);
            labelsRoot.transform.SetParent(farmRoot.transform, false);
            interactionRoot.transform.SetParent(farmRoot.transform, false);
            stateVisualsRoot.transform.SetParent(farmRoot.transform, false);

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
                    Renderer soilRenderer = plot.GetComponent<Renderer>();
                    soilRenderer.sharedMaterial = plotMaterial;
                    NavMeshModifier modifier = plot.AddComponent<NavMeshModifier>();
                    modifier.ignoreFromBuild = true;

                    CreatePlotLabel(labelsRoot.transform, plotNumber, position + Vector3.up * 0.45f);
                    interactionPoints[plotNumber - 1] = CreatePlotInteractionPoint(
                        interactionRoot.transform,
                        plot.transform,
                        plotNumber,
                        position + new Vector3(0f, -0.04f, -config.PlotSize * 0.42f));
                    CreatePlotStateVisuals(
                        stateVisualsRoot.transform,
                        plot,
                        soilRenderer,
                        plotMaterial,
                        bootstrap,
                        plotNumber,
                        position);
                }
            }

            return interactionPoints;
        }

        private static void CreatePlotStateVisuals(
            Transform parent,
            GameObject plot,
            Renderer soilRenderer,
            Material material,
            GameBootstrap bootstrap,
            int plotNumber,
            Vector3 position)
        {
            GameObject crop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crop.name = $"CropVisual_{plotNumber:00}";
            crop.transform.SetParent(parent, false);
            crop.transform.position = position + Vector3.up * 0.55f;
            crop.transform.localScale = new Vector3(0.55f, 0.85f, 0.55f);
            Renderer cropRenderer = crop.GetComponent<Renderer>();
            cropRenderer.sharedMaterial = material;
            Object.DestroyImmediate(crop.GetComponent<Collider>());

            var weeds = new GameObject($"WeedVisual_{plotNumber:00}");
            weeds.transform.SetParent(parent, false);
            weeds.transform.position = position + Vector3.up * 0.36f;
            for (int index = 0; index < 3; index++)
            {
                GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blade.name = $"WeedBlade_{index + 1:00}";
                blade.transform.SetParent(weeds.transform, false);
                blade.transform.localPosition = new Vector3((index - 1) * 0.45f, 0f, (index % 2) * 0.3f);
                blade.transform.localRotation = Quaternion.Euler(0f, index * 35f, (index - 1) * 18f);
                blade.transform.localScale = new Vector3(0.12f, 0.65f, 0.12f);
                blade.GetComponent<Renderer>().sharedMaterial = material;
                Object.DestroyImmediate(blade.GetComponent<Collider>());
            }

            PlotBlockoutView view = plot.AddComponent<PlotBlockoutView>();
            EnsureSucceeded(view.Configure(bootstrap, plotNumber, soilRenderer, crop, cropRenderer, weeds));
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

        private static LocationArrivalPoint[] CreateTownLocations(
            TownLocationDefinitionAsset[] locationAssets,
            Material buildingMaterial,
            Material accentMaterial,
            out ConversationAnchor[] conversationAnchors)
        {
            var townRoot = new GameObject("Town_Locations");
            var points = new List<LocationArrivalPoint>();
            var anchors = new List<ConversationAnchor>();
            for (int index = 0; index < TownLocationSpecs.Length; index++)
            {
                TownLocationBuildSpec spec = TownLocationSpecs[index];
                TownLocationDefinitionAsset location = locationAssets[index];
                GameObject locationRoot = new GameObject(spec.ObjectName);
                locationRoot.transform.SetParent(townRoot.transform, false);

                GameObject structure = GameObject.CreatePrimitive(spec.PrimitiveType);
                structure.name = "Structure";
                structure.transform.SetParent(locationRoot.transform, false);
                structure.transform.position = spec.Position;
                structure.transform.localScale = spec.Scale;
                structure.GetComponent<Renderer>().sharedMaterial = buildingMaterial;
                if (!spec.BlocksNavigation)
                {
                    Object.DestroyImmediate(structure.GetComponent<Collider>());
                }

                GameObject accent = GameObject.CreatePrimitive(PrimitiveType.Cube);
                accent.name = "Accent";
                accent.transform.SetParent(locationRoot.transform, false);
                accent.transform.position = spec.Position +
                    Vector3.up * (spec.Scale.y * 0.5f + 0.18f);
                accent.transform.localScale = new Vector3(
                    Mathf.Max(0.65f, spec.Scale.x * 0.72f),
                    0.22f,
                    Mathf.Max(0.65f, spec.Scale.z * 0.72f));
                accent.GetComponent<Renderer>().sharedMaterial = accentMaterial;
                Object.DestroyImmediate(accent.GetComponent<Collider>());

                Vector3 labelPosition = spec.Position +
                    Vector3.up * (spec.Scale.y * 0.5f + 0.75f);
                var labelObject = new GameObject("Location_Label");
                labelObject.transform.SetParent(locationRoot.transform, false);
                labelObject.transform.position = labelPosition;
                labelObject.transform.rotation = Quaternion.LookRotation(
                    labelPosition - CameraPosition,
                    Vector3.up);
                TextMesh label = labelObject.AddComponent<TextMesh>();
                label.text = spec.DisplayName;
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.fontSize = 58;
                label.characterSize = 0.07f;
                label.color = Color.white;

                Vector3 inward = new Vector3(-spec.Position.x, 0f, -spec.Position.z);
                if (inward.sqrMagnitude < 0.01f)
                {
                    inward = Vector3.back;
                }

                inward.Normalize();
                Vector3 tangent = new Vector3(-inward.z, 0f, inward.x);
                float distance = Mathf.Max(spec.Scale.x, spec.Scale.z) * 0.5f + 0.75f;
                Vector3 pointCenter = new Vector3(spec.Position.x, -0.04f, spec.Position.z) +
                    inward * distance;
                var arrivalRoot = new GameObject("Arrival_Points");
                arrivalRoot.transform.SetParent(locationRoot.transform, false);
                for (int pointIndex = 0; pointIndex < ResidentSpecs.Length; pointIndex++)
                {
                    string pointId = $"{spec.Id}-point-{pointIndex + 1:00}";
                    var pointObject = new GameObject($"ArrivalPoint_{pointIndex + 1:00}");
                    pointObject.transform.SetParent(arrivalRoot.transform, false);
                    pointObject.transform.position = pointCenter +
                        tangent * ((pointIndex - 1.5f) * 0.55f);
                    LocationArrivalPoint point = pointObject.AddComponent<LocationArrivalPoint>();
                    EnsureSucceeded(point.Configure(location, pointId, structure.transform));
                    points.Add(point);

                    GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    marker.name = "Marker";
                    marker.transform.SetParent(pointObject.transform, false);
                    marker.transform.localPosition = new Vector3(0f, 0.045f, 0f);
                    marker.transform.localScale = new Vector3(0.16f, 0.06f, 0.16f);
                    marker.GetComponent<Renderer>().sharedMaterial = accentMaterial;
                    Object.DestroyImmediate(marker.GetComponent<Collider>());
                }

                if (spec.Id == "location-plaza")
                {
                    CreateConversationAnchors(
                        locationRoot.transform,
                        location,
                        spec,
                        inward,
                        tangent,
                        accentMaterial,
                        points,
                        anchors);
                }
            }

            conversationAnchors = anchors.ToArray();
            return points.ToArray();
        }

        private static void CreateConversationAnchors(
            Transform locationRoot,
            TownLocationDefinitionAsset location,
            TownLocationBuildSpec spec,
            Vector3 inward,
            Vector3 tangent,
            Material markerMaterial,
            List<LocationArrivalPoint> points,
            List<ConversationAnchor> anchors)
        {
            var anchorRoot = new GameObject("Conversation_Anchors");
            anchorRoot.transform.SetParent(locationRoot, false);
            for (int anchorIndex = 0; anchorIndex < 2; anchorIndex++)
            {
                string anchorId = $"conversation-anchor-{anchorIndex + 1:00}";
                string firstPointId =
                    $"social-plaza-anchor-{anchorIndex + 1:00}-seat-01";
                string secondPointId =
                    $"social-plaza-anchor-{anchorIndex + 1:00}-seat-02";
                var anchorObject = new GameObject($"ConversationAnchor_{anchorIndex + 1:00}");
                anchorObject.transform.SetParent(anchorRoot.transform, false);
                Vector3 center = new Vector3(spec.Position.x, -0.04f, spec.Position.z) +
                    tangent * (anchorIndex == 0 ? -0.75f : 0.75f);
                LocationArrivalPoint firstPoint = CreateConversationStandPoint(
                    anchorObject.transform,
                    center - inward * 0.42f,
                    markerMaterial,
                    "StandPoint_01");
                LocationArrivalPoint secondPoint = CreateConversationStandPoint(
                    anchorObject.transform,
                    center + inward * 0.42f,
                    markerMaterial,
                    "StandPoint_02");
                EnsureSucceeded(firstPoint.Configure(
                    location,
                    firstPointId,
                    secondPoint.transform));
                EnsureSucceeded(secondPoint.Configure(
                    location,
                    secondPointId,
                    firstPoint.transform));

                ConversationAnchor anchor = anchorObject.AddComponent<ConversationAnchor>();
                EnsureSucceeded(anchor.Configure(anchorId, firstPoint, secondPoint));
                points.Add(firstPoint);
                points.Add(secondPoint);
                anchors.Add(anchor);
            }
        }

        private static LocationArrivalPoint CreateConversationStandPoint(
            Transform parent,
            Vector3 position,
            Material markerMaterial,
            string objectName)
        {
            var pointObject = new GameObject(objectName);
            pointObject.transform.SetParent(parent, false);
            pointObject.transform.position = position;
            LocationArrivalPoint point = pointObject.AddComponent<LocationArrivalPoint>();

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "ConversationMarker";
            marker.transform.SetParent(pointObject.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.025f, 0f);
            marker.transform.localScale = new Vector3(0.18f, 0.025f, 0.18f);
            marker.GetComponent<Renderer>().sharedMaterial = markerMaterial;
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            return point;
        }

        private static TownResidentScheduleController[] CreateResidents(
            GameObject residentPrefab,
            ResidentDefinitionAsset[] residentAssets,
            Material[] residentMaterials,
            Material feedbackBackgroundMaterial,
            GameBootstrap bootstrap,
            PlotInteractionPoint[] interactionPoints,
            LocationArrivalPoint[] arrivalPoints,
            ReplanController replanController,
            out NpcPlanExecutor yayaExecutor)
        {
            if (residentPrefab == null || residentAssets == null || residentMaterials == null ||
                residentAssets.Length != ResidentSpecs.Length ||
                residentMaterials.Length != ResidentSpecs.Length)
            {
                throw new System.InvalidOperationException(
                    "Resident prefab and four matching resident assets are required.");
            }

            yayaExecutor = null;
            var controllers = new TownResidentScheduleController[ResidentSpecs.Length];
            for (int index = 0; index < ResidentSpecs.Length; index++)
            {
                ResidentBuildSpec spec = ResidentSpecs[index];
                GameObject resident = (GameObject)PrefabUtility.InstantiatePrefab(residentPrefab);
                resident.name = spec.SceneObjectName;
                LocationArrivalPoint homePoint = FindArrivalPoint(
                    arrivalPoints,
                    spec.HomeLocationId,
                    preferredIndex: index);
                if (homePoint == null)
                {
                    throw new System.InvalidOperationException(
                        $"No home arrival point exists for {spec.Definition.DisplayName}.");
                }

                resident.transform.position = homePoint.Position;
                NavMeshAgent agent = resident.GetComponent<NavMeshAgent>();
                agent.speed = 3.2f;
                agent.angularSpeed = 720f;
                agent.acceleration = 20f;
                agent.stoppingDistance = 0.08f;

                Transform visual = resident.transform.Find("NPC_Visual_Capsule");
                Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
                TextMesh nameLabel = resident.transform
                    .Find("Resident_Name_Label")
                    .GetComponent<TextMesh>();
                TextMesh statusIcon = resident.transform
                    .Find("Resident_Status_Icon")
                    .GetComponent<TextMesh>();
                TextMesh conversationText = resident.transform
                    .Find("Resident_Conversation_Label")
                    .GetComponent<TextMesh>();
                ResidentBlockoutView view = resident.GetComponent<ResidentBlockoutView>();
                EnsureSucceeded(view.Configure(
                    residentAssets[index],
                    renderers,
                    nameLabel,
                    statusIcon,
                    visual,
                    residentMaterials[index],
                    conversationText));

                TownResidentNavigator townNavigator =
                    resident.GetComponent<TownResidentNavigator>();
                EnsureSucceeded(townNavigator.Configure(
                    agent,
                    arrivalPoints,
                    requireNavMesh: true,
                    movementSpeed: 3.2f));

                NpcPlanExecutor farmExecutor = null;
                if (spec.Definition.ResidentId == ResidentIds.Yaya)
                {
                    CreateFeedbackBar(
                        resident.transform,
                        residentMaterials[index],
                        feedbackBackgroundMaterial,
                        out GameObject progressRoot,
                        out Transform progressFill);
                    CreateNpcDialogueBubble(resident.transform, replanController);

                    NpcNavigator farmNavigator = resident.AddComponent<NpcNavigator>();
                    EnsureSucceeded(farmNavigator.Configure(
                        agent,
                        interactionPoints,
                        requireNavMesh: true,
                        movementSpeed: 3.5f));
                    BlockoutActionFeedback feedback =
                        resident.AddComponent<BlockoutActionFeedback>();
                    EnsureSucceeded(feedback.Configure(
                        visual,
                        progressRoot,
                        progressFill));
                    farmExecutor = resident.AddComponent<NpcPlanExecutor>();
                    EnsureSucceeded(farmExecutor.Configure(
                        ResidentIds.Yaya,
                        bootstrap,
                        farmNavigator,
                        feedback));
                    yayaExecutor = farmExecutor;
                }

                TownResidentScheduleController controller =
                    resident.GetComponent<TownResidentScheduleController>();
                EnsureSucceeded(controller.Configure(
                    residentAssets[index],
                    townNavigator,
                    view,
                    farmExecutor));
                controllers[index] = controller;
            }

            if (yayaExecutor == null)
            {
                throw new System.InvalidOperationException("The scene requires the 芽芽 farm executor.");
            }

            return controllers;
        }

        private static LocationArrivalPoint FindArrivalPoint(
            LocationArrivalPoint[] points,
            string locationId,
            int preferredIndex)
        {
            var matches = new List<LocationArrivalPoint>();
            foreach (LocationArrivalPoint point in points)
            {
                if (point != null && point.LocationId.Value == locationId)
                {
                    matches.Add(point);
                }
            }

            matches.Sort((left, right) => string.CompareOrdinal(
                left.InteractionPointId,
                right.InteractionPointId));
            if (matches.Count == 0)
            {
                return null;
            }

            return matches[Mathf.Clamp(preferredIndex, 0, matches.Count - 1)];
        }

        private static void CreateNpcDialogueBubble(
            Transform npc,
            ReplanController replanController)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject bubbleObject = new GameObject(
                "NPC_DialogueBubble",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(Image));
            bubbleObject.transform.SetParent(npc, false);
            RectTransform bubbleRect = bubbleObject.GetComponent<RectTransform>();
            bubbleRect.localPosition = new Vector3(0f, 3.65f, 0f);
            bubbleRect.localScale = Vector3.one * 0.006f;
            bubbleRect.sizeDelta = new Vector2(430f, 150f);

            Canvas canvas = bubbleObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;
            CanvasScaler scaler = bubbleObject.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 20f;
            bubbleObject.GetComponent<Image>().color = new Color(0.98f, 0.96f, 0.85f, 0.94f);

            Text emojiText = CreateText(
                "EmojiText",
                bubbleObject.transform,
                "…",
                font,
                42,
                FontStyle.Bold);
            emojiText.color = new Color(0.12f, 0.14f, 0.12f);
            emojiText.alignment = TextAnchor.MiddleCenter;
            SetRect(
                emojiText.rectTransform,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(12f, 8f),
                new Vector2(72f, 90f));

            Text dialogueText = CreateText(
                "DialogueText",
                bubbleObject.transform,
                "等待你的种田目标。",
                font,
                25,
                FontStyle.Normal);
            dialogueText.color = new Color(0.12f, 0.14f, 0.12f);
            dialogueText.alignment = TextAnchor.MiddleLeft;
            SetRect(
                dialogueText.rectTransform,
                new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(38f, 10f),
                new Vector2(-120f, 92f));

            Text moodText = CreateText(
                "MoodText",
                bubbleObject.transform,
                "Focused",
                font,
                18,
                FontStyle.Italic);
            moodText.color = new Color(0.28f, 0.35f, 0.28f);
            SetRect(
                moodText.rectTransform,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(38f, 10f),
                new Vector2(-120f, 32f));

            NpcDialogueBubble bubble = bubbleObject.AddComponent<NpcDialogueBubble>();
            EnsureSucceeded(bubble.Configure(
                replanController,
                bubbleObject,
                dialogueText,
                emojiText,
                moodText,
                bubbleObject.transform));
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
            camera.orthographicSize = 10.5f;
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

        private static void CreateUi(
            GameBootstrap bootstrap,
            NpcPlanExecutor executor,
            ReplanController replanController,
            Transform npcTransform)
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
                new Color(0.04f, 0.07f, 0.06f, 0.9f));
            SetRect(
                statusPanel.GetComponent<RectTransform>(),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(20f, -20f),
                new Vector2(560f, 180f));

            Text titleText = CreateText("TitleText", statusPanel.transform, "AI FARM // GATEWAY", font, 27, FontStyle.Bold);
            SetTopRow(titleText.rectTransform, -10f, 38f);

            Text emojiText = CreateText("EmojiText", statusPanel.transform, "…", font, 36, FontStyle.Bold);
            emojiText.alignment = TextAnchor.MiddleCenter;
            SetRect(
                emojiText.rectTransform,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-16f, -10f),
                new Vector2(64f, 42f));

            Text timeText = CreateText(
                "TimeText",
                statusPanel.transform,
                "Day 1  08:00  [20x]",
                font,
                23,
                FontStyle.Bold);
            SetTopRow(timeText.rectTransform, -50f, 32f);

            Text moodText = CreateText(
                "MoodText",
                statusPanel.transform,
                "Mood: Focused",
                font,
                18,
                FontStyle.Italic);
            SetTopRow(moodText.rectTransform, -82f, 26f);

            Text aiModeText = CreateText(
                "AiModeText",
                statusPanel.transform,
                "AI: LOCAL",
                font,
                18,
                FontStyle.Bold);
            aiModeText.alignment = TextAnchor.MiddleRight;
            SetRect(
                aiModeText.rectTransform,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-16f, -82f),
                new Vector2(200f, 26f));

            GameObject timeControls = CreateUiObject("TimeControls", statusPanel.transform);
            SetTopRow(timeControls.GetComponent<RectTransform>(), -116f, 48f);
            Button pauseButton = CreateControlButton(
                "PauseButton",
                "PAUSE",
                timeControls.transform,
                font,
                out Text pauseButtonLabel);
            SetControlRect(pauseButton.GetComponent<RectTransform>(), 0f, 122f);
            Button speed1Button = CreateControlButton("Speed1Button", "1x", timeControls.transform, font, out _);
            SetControlRect(speed1Button.GetComponent<RectTransform>(), 132f, 76f);
            Button speed5Button = CreateControlButton("Speed5Button", "5x", timeControls.transform, font, out _);
            SetControlRect(speed5Button.GetComponent<RectTransform>(), 218f, 76f);
            Button speed20Button = CreateControlButton("Speed20Button", "20x", timeControls.transform, font, out _);
            SetControlRect(speed20Button.GetComponent<RectTransform>(), 304f, 86f);
            Button apiSettingsButton = CreateControlButton(
                "ApiSettingsButton",
                "API SETUP",
                timeControls.transform,
                font,
                out _);
            SetControlRect(apiSettingsButton.GetComponent<RectTransform>(), 400f, 128f);

            LocalAiGatewayProcess gatewayProcess =
                bootstrap.gameObject.GetComponent<LocalAiGatewayProcess>() ??
                bootstrap.gameObject.AddComponent<LocalAiGatewayProcess>();
            EnsureSucceeded(gatewayProcess.Configure(
                bootstrap.SceneConfig.AiGatewayBaseUrl));

            GameObject backpackPanel = CreatePanel(
                "BackpackPanel",
                canvasObject.transform,
                new Color(0.04f, 0.07f, 0.06f, 0.88f));
            SetRect(
                backpackPanel.GetComponent<RectTransform>(),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(20f, -220f),
                new Vector2(300f, 240f));
            Text backpackTitle = CreateText("TitleText", backpackPanel.transform, "BACKPACK", font, 22, FontStyle.Bold);
            SetTopRow(backpackTitle.rectTransform, -12f, 34f);
            Text inventoryText = CreateText(
                "InventoryText",
                backpackPanel.transform,
                "Seeds: 9\nWater: 9\nFertilizer: 9\nCarrots: 0\nMoisture drops: 0  Weeds: 0",
                font,
                20,
                FontStyle.Normal);
            SetTopRow(inventoryText.rectTransform, -52f, 172f);
            inventoryText.alignment = TextAnchor.UpperLeft;

            GameObject goalPanel = CreatePanel(
                "GoalPanel",
                canvasObject.transform,
                new Color(0.04f, 0.07f, 0.06f, 0.88f));
            SetRect(
                goalPanel.GetComponent<RectTransform>(),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(340f, -220f),
                new Vector2(600f, 270f));
            Text goalTitle = CreateText("TitleText", goalPanel.transform, "CURRENT GOAL", font, 22, FontStyle.Bold);
            SetTopRow(goalTitle.rectTransform, -12f, 34f);
            Text goalText = CreateText("GoalText", goalPanel.transform, "Goal: Idle", font, 19, FontStyle.Normal);
            SetTopRow(goalText.rectTransform, -50f, 46f);
            Text actionText = CreateText("ActionText", goalPanel.transform, "Action: Idle", font, 19, FontStyle.Bold);
            SetTopRow(actionText.rectTransform, -100f, 30f);
            Text actionReasonText = CreateText(
                "ActionReasonText",
                goalPanel.transform,
                "Reason: waiting for a goal",
                font,
                18,
                FontStyle.Normal);
            SetTopRow(actionReasonText.rectTransform, -134f, 50f);
            Text expressionText = CreateText(
                "ExpressionText",
                goalPanel.transform,
                "NPC: 等待你的种田目标。",
                font,
                18,
                FontStyle.Italic);
            SetTopRow(expressionText.rectTransform, -190f, 58f);

            GameObject eventsPanel = CreatePanel(
                "WorldEventsPanel",
                canvasObject.transform,
                new Color(0.04f, 0.07f, 0.06f, 0.9f));
            SetRect(
                eventsPanel.GetComponent<RectTransform>(),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-20f, -20f),
                new Vector2(700f, 400f));
            Text eventsTitle = CreateText(
                "TitleText",
                eventsPanel.transform,
                "RECENT WORLD EVENTS // 10",
                font,
                22,
                FontStyle.Bold);
            SetTopRow(eventsTitle.rectTransform, -12f, 34f);
            Text worldEventsText = CreateText(
                "WorldEventsText",
                eventsPanel.transform,
                "No world events yet.",
                font,
                18,
                FontStyle.Normal);
            SetTopRow(worldEventsText.rectTransform, -52f, 330f);
            worldEventsText.alignment = TextAnchor.UpperLeft;

            GameObject memoryPanel = CreatePanel(
                "MemoryPanel",
                canvasObject.transform,
                new Color(0.04f, 0.07f, 0.06f, 0.9f));
            SetRect(
                memoryPanel.GetComponent<RectTransform>(),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-20f, -440f),
                new Vector2(700f, 510f));
            Text memoryTitle = CreateText(
                "TitleText",
                memoryPanel.transform,
                "芽芽 // MEMORY & REFLECTION",
                font,
                22,
                FontStyle.Bold);
            SetTopRow(memoryTitle.rectTransform, -12f, 34f);

            GameObject residentSelector = CreateUiObject(
                "ResidentSelector",
                memoryPanel.transform);
            SetTopRow(residentSelector.GetComponent<RectTransform>(), -48f, 38f);
            Button yayaResidentButton = CreateControlButton(
                "YayaButton",
                "芽芽",
                residentSelector.transform,
                font,
                out _);
            SetControlRect(yayaResidentButton.GetComponent<RectTransform>(), 0f, 116f);
            Button amuResidentButton = CreateControlButton(
                "AmuButton",
                "阿木",
                residentSelector.transform,
                font,
                out _);
            SetControlRect(amuResidentButton.GetComponent<RectTransform>(), 126f, 116f);
            Button xiaosuiResidentButton = CreateControlButton(
                "XiaosuiButton",
                "小穗",
                residentSelector.transform,
                font,
                out _);
            SetControlRect(xiaosuiResidentButton.GetComponent<RectTransform>(), 252f, 116f);
            Button momoResidentButton = CreateControlButton(
                "MomoButton",
                "墨墨",
                residentSelector.transform,
                font,
                out _);
            SetControlRect(momoResidentButton.GetComponent<RectTransform>(), 378f, 116f);

            Text personaText = CreateText(
                "PersonaText",
                memoryPanel.transform,
                "农场助手｜认真、乐观｜优先生长问题｜喜欢整齐，讨厌杂草和浪费",
                font,
                16,
                FontStyle.Italic);
            SetTopRow(personaText.rectTransform, -92f, 40f);
            Text memoriesTitle = CreateText(
                "MemoriesTitleText",
                memoryPanel.transform,
                "RECENT OBSERVATIONS",
                font,
                18,
                FontStyle.Bold);
            SetTopRow(memoriesTitle.rectTransform, -138f, 28f);
            Text recentMemoriesText = CreateText(
                "RecentMemoriesText",
                memoryPanel.transform,
                "芽芽还没有新的观察。",
                font,
                15,
                FontStyle.Normal);
            SetTopRow(recentMemoriesText.rectTransform, -170f, 188f);
            recentMemoriesText.alignment = TextAnchor.UpperLeft;
            Text reflectionsTitle = CreateText(
                "ReflectionsTitleText",
                memoryPanel.transform,
                "RECENT REFLECTIONS",
                font,
                18,
                FontStyle.Bold);
            SetTopRow(reflectionsTitle.rectTransform, -370f, 28f);
            Text recentReflectionsText = CreateText(
                "RecentReflectionsText",
                memoryPanel.transform,
                "反思：完成一轮种植后生成。",
                font,
                16,
                FontStyle.Normal);
            SetTopRow(recentReflectionsText.rectTransform, -402f, 92f);
            recentReflectionsText.alignment = TextAnchor.UpperLeft;

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
                new Vector2(1120f, 92f));

            InputField commandInput = CreateInputField(commandPanel.transform, font);
            SetRect(
                commandInput.GetComponent<RectTransform>(),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(20f, 0f),
                new Vector2(880f, 54f));

            Button submitButton = CreateButton(commandPanel.transform, font);
            SetRect(
                submitButton.GetComponent<RectTransform>(),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-20f, 0f),
                new Vector2(180f, 54f));

            GameObject savePanel = CreatePanel(
                "SaveControlsPanel",
                canvasObject.transform,
                new Color(0.04f, 0.07f, 0.06f, 0.9f));
            SetRect(
                savePanel.GetComponent<RectTransform>(),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(20f, 20f),
                new Vector2(360f, 116f));
            Text saveStatusText = CreateText(
                "SaveStatusText",
                savePanel.transform,
                "SAVE DATA // READY",
                font,
                15,
                FontStyle.Normal);
            SetTopRow(saveStatusText.rectTransform, -8f, 44f);
            saveStatusText.alignment = TextAnchor.UpperLeft;

            Button saveButton = CreateControlButton(
                "SaveButton",
                "SAVE",
                savePanel.transform,
                font,
                out _);
            SetRect(
                saveButton.GetComponent<RectTransform>(),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(12f, 12f),
                new Vector2(100f, 42f));
            Button loadButton = CreateControlButton(
                "LoadButton",
                "LOAD",
                savePanel.transform,
                font,
                out _);
            SetRect(
                loadButton.GetComponent<RectTransform>(),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(124f, 12f),
                new Vector2(100f, 42f));
            Button newDemoButton = CreateControlButton(
                "NewDemoButton",
                "NEW DEMO",
                savePanel.transform,
                font,
                out _);
            SetRect(
                newDemoButton.GetComponent<RectTransform>(),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(236f, 12f),
                new Vector2(112f, 42f));

            GameObject apiSetupPanel = CreatePanel(
                "ApiGatewaySetupPanel",
                canvasObject.transform,
                new Color(0.025f, 0.045f, 0.04f, 0.98f));
            SetRect(
                apiSetupPanel.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(720f, 450f));

            Text apiSetupTitle = CreateText(
                "TitleText",
                apiSetupPanel.transform,
                "CONNECT SHARED AI GATEWAY",
                font,
                25,
                FontStyle.Bold);
            SetTopRow(apiSetupTitle.rectTransform, -16f, 38f);

            Text apiSetupHelp = CreateText(
                "HelpText",
                apiSetupPanel.transform,
                "输入一次 API Key 与模型后，游戏会自动启动本机网关。" +
                "Key 不进入命令行、场景、存档或 PlayerPrefs。",
                font,
                16,
                FontStyle.Normal);
            SetTopRow(apiSetupHelp.rectTransform, -58f, 54f);
            apiSetupHelp.alignment = TextAnchor.UpperLeft;

            Text apiKeyLabel = CreateText(
                "ApiKeyLabel",
                apiSetupPanel.transform,
                "DEEPSEEK API KEY",
                font,
                17,
                FontStyle.Bold);
            SetTopRow(apiKeyLabel.rectTransform, -120f, 26f);
            InputField apiKeyInput = CreateInputField(
                "ApiKeyInput",
                apiSetupPanel.transform,
                font,
                "输入 API Key（提交后立即清空）",
                string.Empty);
            SetRect(
                apiKeyInput.GetComponent<RectTransform>(),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -152f),
                new Vector2(-48f, 50f));
            apiKeyInput.contentType = InputField.ContentType.Password;
            apiKeyInput.characterLimit = LocalAiGatewayLaunchSpec.MaximumApiKeyLength;

            Text modelLabel = CreateText(
                "ModelLabel",
                apiSetupPanel.transform,
                "MODEL ID",
                font,
                17,
                FontStyle.Bold);
            SetTopRow(modelLabel.rectTransform, -216f, 26f);
            InputField modelInput = CreateInputField(
                "ModelInput",
                apiSetupPanel.transform,
                font,
                LocalAiGatewayProcess.DefaultModelId,
                LocalAiGatewayProcess.DefaultModelId);
            SetRect(
                modelInput.GetComponent<RectTransform>(),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -248f),
                new Vector2(-48f, 50f));
            modelInput.characterLimit = LocalAiGatewayLaunchSpec.MaximumModelIdLength;

            Text gatewayStatusText = CreateText(
                "GatewayStatusText",
                apiSetupPanel.transform,
                "未启动；居民继续使用本地 AI。",
                font,
                16,
                FontStyle.Normal);
            SetTopRow(gatewayStatusText.rectTransform, -310f, 54f);
            gatewayStatusText.alignment = TextAnchor.UpperLeft;
            gatewayStatusText.color = new Color(0.72f, 0.92f, 0.76f, 1f);

            Button startGatewayButton = CreateControlButton(
                "StartGatewayButton",
                "START & CONNECT",
                apiSetupPanel.transform,
                font,
                out _);
            SetRect(
                startGatewayButton.GetComponent<RectTransform>(),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(24f, 22f),
                new Vector2(260f, 48f));
            Button stopGatewayButton = CreateControlButton(
                "StopGatewayButton",
                "STOP OWNED",
                apiSetupPanel.transform,
                font,
                out _);
            SetRect(
                stopGatewayButton.GetComponent<RectTransform>(),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(296f, 22f),
                new Vector2(176f, 48f));
            Button closeApiSetupButton = CreateControlButton(
                "CloseButton",
                "CLOSE",
                apiSetupPanel.transform,
                font,
                out _);
            SetRect(
                closeApiSetupButton.GetComponent<RectTransform>(),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(484f, 22f),
                new Vector2(212f, 48f));

            ApiGatewaySetupPanel apiSetupController =
                canvasObject.AddComponent<ApiGatewaySetupPanel>();
            EnsureSucceeded(apiSetupController.Configure(
                gatewayProcess,
                apiSettingsButton,
                apiSetupPanel,
                apiKeyInput,
                modelInput,
                startGatewayButton,
                stopGatewayButton,
                closeApiSetupButton,
                gatewayStatusText));
            apiSetupPanel.SetActive(false);

            DemoHud hud = canvasObject.AddComponent<DemoHud>();
            hud.Configure(
                bootstrap,
                timeText,
                inventoryText,
                goalText,
                actionText,
                commandInput,
                submitButton,
                executor,
                replanController,
                expressionText,
                actionReasonText,
                worldEventsText,
                moodText,
                emojiText,
                pauseButton,
                pauseButtonLabel,
                speed1Button,
                speed5Button,
                speed20Button,
                aiModeText,
                recentMemoriesText,
                recentReflectionsText,
                null,
                memoryTitle,
                personaText,
                yayaResidentButton,
                amuResidentButton,
                xiaosuiResidentButton,
                momoResidentButton);

            SaveGameController saveController = canvasObject.AddComponent<SaveGameController>();
            EnsureSucceeded(saveController.Configure(
                bootstrap,
                executor,
                replanController,
                npcTransform,
                saveButton,
                loadButton,
                newDemoButton,
                saveStatusText));

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
            return CreateInputField(
                "CommandInput",
                parent,
                font,
                "把地种满胡萝卜并照顾到收获。",
                string.Empty);
        }

        private static InputField CreateInputField(
            string name,
            Transform parent,
            Font font,
            string placeholderText,
            string initialValue)
        {
            GameObject inputObject = CreateUiObject(name, parent);
            Image background = inputObject.AddComponent<Image>();
            background.color = new Color(0.93f, 0.95f, 0.91f, 1f);

            Text inputText = CreateText(
                "Text",
                inputObject.transform,
                initialValue ?? string.Empty,
                font,
                21,
                FontStyle.Normal);
            inputText.color = new Color(0.08f, 0.10f, 0.09f);
            StretchRect(inputText.rectTransform, 16f, 12f);

            Text placeholder = CreateText(
                "Placeholder",
                inputObject.transform,
                placeholderText ?? string.Empty,
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
            inputField.text = initialValue ?? string.Empty;
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

        private static Button CreateControlButton(
            string name,
            string content,
            Transform parent,
            Font font,
            out Text label)
        {
            GameObject buttonObject = CreateUiObject(name, parent);
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.20f, 0.48f, 0.30f, 1f);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;

            label = CreateText("Label", buttonObject.transform, content, font, 17, FontStyle.Bold);
            label.alignment = TextAnchor.MiddleCenter;
            StretchRect(label.rectTransform, 4f, 4f);
            return button;
        }

        private static void SetControlRect(RectTransform rect, float x, float width)
        {
            SetRect(
                rect,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(x, 0f),
                new Vector2(width, 40f));
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
