using System;
using System.IO;
using System.Linq;
using AIFarm.Presentation;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AIFarm.Editor
{
    /// <summary>Reproducible Blender art layer over the existing simulation scene.</summary>
    public static class MeadowSceneUpgrade
    {
        public const string ModelFolder = "Assets/AIFarm/Art/Models";
        public const string MaterialFolder = "Assets/AIFarm/Art/Materials/Meadow";
        public const string EnvironmentPath = ModelFolder + "/Meadow/MeadowVillage.fbx";
        public const string RootName = "Meadow_Village_Art";

        [Serializable]
        private sealed class Palette
        {
            public Swatch[] materials;
        }

        [Serializable]
        private sealed class Swatch
        {
            public string name;
            public float[] color;
        }

        [MenuItem("AIFarm/Art/Apply Blender Meadow Village")]
        public static void ApplyAndSave()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Stop Play Mode before applying scene art.");
            }

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != DemoSceneBuilder.ScenePath)
            {
                throw new InvalidOperationException("Open DemoScene before applying its art layer.");
            }

            Apply();
            NavMeshSurface surface = Object.FindFirstObjectByType<NavMeshSurface>();
            ConfigureNavigation(surface);
            surface.BuildNavMesh();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        public static void Apply()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath) == null)
            {
                throw new InvalidOperationException("Generate the Blender assets with Tools/Art/build_meadow.py first.");
            }

            ImportPaletteMaterials();
            GameObject previous = GameObject.Find(RootName);
            if (previous != null)
            {
                Object.DestroyImmediate(previous);
            }

            var artRoot = new GameObject(RootName);
            GameObject meadow = InstantiateModel(EnvironmentPath, artRoot.transform);
            meadow.name = "Blender_MeadowVillage";
            // Blender's architectural +Y is north; FBX import uses Unity -Z.
            meadow.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            Transform ground = GameObject.Find("Environment").transform.Find("Ground_Blockout");
            ground.position = new Vector3(0f, -0.3f, 2f);
            ground.localScale = new Vector3(55.4f, 0.5f, 43.4f);
            ground.GetComponent<Renderer>().enabled = false;
            HideTownBlockouts();
            foreach (TextMesh label in GameObject.Find("Farm_3x3").GetComponentsInChildren<TextMesh>())
            {
                label.characterSize = 0.05f;
                if (label.GetComponent<WorldSpaceBillboard>() == null)
                {
                    label.gameObject.AddComponent<WorldSpaceBillboard>();
                }
            }

            AddLandscapeColliders(artRoot.transform);
            UpgradeResidents();
            UpgradeCrops();
            UpgradeLighting();
            UpgradeCamera();
            AddWelcomeLettering(artRoot.transform);
            AddCameraHint();
            ConfigureCompactHud();
        }

        public static void ConfigureNavigation(NavMeshSurface surface)
        {
            // Expanded bounds need finer voxels to retain the existing 8 cm arrival tolerance.
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.075f;
        }

        private static void ImportPaletteMaterials()
        {
            Directory.CreateDirectory(MaterialFolder);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException("The project URP Lit shader is unavailable.");
            }

            foreach (string path in Directory.GetFiles(ModelFolder, "*palette*.json", SearchOption.AllDirectories))
            {
                Palette palette = JsonUtility.FromJson<Palette>(File.ReadAllText(path));
                if (palette == null || palette.materials == null)
                {
                    throw new InvalidOperationException("Invalid Blender palette: " + path);
                }

                foreach (Swatch swatch in palette.materials)
                {
                    string materialPath = MaterialFolder + "/" + swatch.name + ".mat";
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material == null)
                    {
                        material = new Material(shader);
                        AssetDatabase.CreateAsset(material, materialPath);
                    }

                    material.shader = shader;
                    material.SetColor("_BaseColor", new Color(swatch.color[0], swatch.color[1], swatch.color[2], 1f));
                    material.SetFloat("_Smoothness", swatch.name.Contains("Water") ? 0.55f : 0.18f);
                    material.SetFloat("_Metallic", 0f);
                    if (swatch.name.Contains("Glow"))
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor("_EmissionColor", new Color(1f, 0.63f, 0.24f) * 0.25f);
                    }

                    EditorUtility.SetDirty(material);
                }
            }

            foreach (string file in Directory.GetFiles(ModelFolder, "*.fbx", SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(path);
                bool changed = false;
                if (importer.importCameras || importer.importLights || importer.importAnimation)
                {
                    importer.importCameras = false;
                    importer.importLights = false;
                    importer.importAnimation = false;
                    changed = true;
                }

                var remaps = importer.GetExternalObjectMap();
                foreach (Material source in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                {
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + source.name + ".mat");
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), source.name);
                    if (material != null && (!remaps.TryGetValue(id, out Object existing) || existing != material))
                    {
                        importer.AddRemap(id, material);
                        changed = true;
                    }
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }
        }

        private static GameObject InstantiateModel(string path, Transform parent)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                throw new InvalidOperationException("Missing Blender model: " + path);
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            instance.transform.localPosition = Vector3.zero;
            // Preserve the FBX root's axis correction, if any.
            instance.transform.localRotation = asset.transform.localRotation;
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        private static void HideTownBlockouts()
        {
            foreach (Transform location in GameObject.Find("Town_Locations").transform)
            {
                foreach (string child in new[] { "Structure", "Accent" })
                {
                    Renderer renderer = location.Find(child).GetComponent<Renderer>();
                    renderer.enabled = false;
                }

                foreach (Renderer marker in location.GetComponentsInChildren<Renderer>())
                {
                    if (marker.name == "Marker")
                    {
                        marker.enabled = false;
                    }
                }

                TextMesh label = location.Find("Location_Label").GetComponent<TextMesh>();
                Vector3 position = location.Find("Structure").position;
                label.transform.position = new Vector3(position.x, 0.13f, position.z - 1.6f);
                label.characterSize = 0.034f;
                label.color = new Color(0.26f, 0.24f, 0.18f);
                if (label.GetComponent<WorldSpaceBillboard>() == null)
                {
                    label.gameObject.AddComponent<WorldSpaceBillboard>();
                }
            }
        }

        private static void AddLandscapeColliders(Transform parent)
        {
            var root = new GameObject("Landscape_Navigation_Bounds");
            root.transform.SetParent(parent, false);
            AddBox(root.transform, "Pond", new Vector3(17f, 0.1f, -3f), new Vector3(10.6f, 0.4f, 12f));
            AddBox(root.transform, "Barn", new Vector3(16f, 1.7f, 12.6f), new Vector3(5.3f, 3.5f, 3.7f));
            AddBox(root.transform, "Windmill", new Vector3(-16f, 2.1f, 12f), new Vector3(2.5f, 4.3f, 2.5f));
            AddBox(root.transform, "Orchard fence", new Vector3(-19.8f, 0.5f, -1.5f), new Vector3(0.15f, 1f, 13f));
            AddBox(root.transform, "Cottage garden fence", new Vector3(0f, 0.5f, 9.3f), new Vector3(20.3f, 1f, 0.15f));
            AddBox(root.transform, "Entrance west fence", new Vector3(-5.75f, 0.5f, -12.5f), new Vector3(6.7f, 1f, 0.15f));
            AddBox(root.transform, "Entrance east fence", new Vector3(5.25f, 0.5f, -12.5f), new Vector3(5.7f, 1f, 0.15f));
        }

        private static void AddBox(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var obstacle = new GameObject(name);
            obstacle.transform.SetParent(parent, false);
            obstacle.transform.position = center;
            BoxCollider collider = obstacle.AddComponent<BoxCollider>();
            collider.size = size;
            NavMeshModifier modifier = obstacle.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = NavMesh.GetAreaFromName("Not Walkable");
        }

        private static void UpgradeResidents()
        {
            string[] objects = { "NPC_Blockout_Capsule", "Resident_Amu", "Resident_Xiaosui", "Resident_Momo" };
            string[] models = { "Yaya", "Amu", "Xiaosui", "Momo" };
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject resident = GameObject.Find(objects[i]);
                Vector3 facing = -resident.transform.position;
                facing.y = 0f;
                resident.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
                Transform visual = resident.transform.Find("NPC_Visual_Capsule");
                Transform previous = visual.Find("Blender_Resident");
                if (previous != null)
                {
                    Object.DestroyImmediate(previous.gameObject);
                }

                foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = false;
                }

                GameObject model = InstantiateModel(ModelFolder + "/Residents/" + models[i] + ".fbx", visual);
                model.name = "Blender_Resident";
                model.transform.localPosition = new Vector3(0f, -1f, 0f);
                model.transform.localScale = Vector3.one / 1.2f;
                model.AddComponent<ResidentModelAnimation>();
                foreach (string name in new[] { "Resident_Name_Label", "Resident_Status_Icon" })
                {
                    TextMesh label = resident.transform.Find(name).GetComponent<TextMesh>();
                    label.characterSize = 0.046f;
                    label.color = name == "Resident_Name_Label" ? new Color(0.20f, 0.24f, 0.18f) : new Color(0.46f, 0.31f, 0.15f);
                }
            }
        }

        private static void UpgradeLighting()
        {
            Light sun = GameObject.Find("DirectionalLight_Key").GetComponent<Light>();
            sun.color = new Color(1f, 0.92f, 0.78f);
            sun.intensity = 1.55f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.65f;
            sun.shadowBias = 0.03f;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.68f, 0.76f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.57f, 0.47f);
            RenderSettings.ambientGroundColor = new Color(0.34f, 0.36f, 0.26f);
            RenderSettings.fog = false;
            // The overview camera sits beyond the original pipeline's 50 m shadow range.
            var pipeline = new SerializedObject(GraphicsSettings.currentRenderPipeline);
            pipeline.FindProperty("m_ShadowDistance").floatValue = 120f;
            pipeline.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void UpgradeCrops()
        {
            Transform stateRoot = GameObject.Find("Farm_3x3").transform.Find("Plot_State_Visuals");
            foreach (Transform crop in stateRoot)
            {
                if (!crop.name.StartsWith("CropVisual_", StringComparison.Ordinal))
                {
                    continue;
                }

                Transform old = crop.Find("Blender_Carrot_Cluster");
                if (old != null)
                {
                    Object.DestroyImmediate(old.gameObject);
                }

                crop.GetComponent<Renderer>().enabled = false;
                Vector3 position = crop.position;
                position.y = 0.14f;
                crop.position = position;
                var cluster = new GameObject("Blender_Carrot_Cluster");
                cluster.transform.SetParent(crop, false);
                cluster.transform.localScale = new Vector3(1f / 0.55f, 1f / 0.85f, 1f / 0.55f);
                foreach (float x in new[] { -0.42f, 0.42f })
                {
                    foreach (float z in new[] { -0.42f, 0.42f })
                    {
                        GameObject plant = InstantiateModel(ModelFolder + "/Crops/CarrotPlant.fbx", cluster.transform);
                        plant.transform.localPosition = new Vector3(x, 0f, z);
                    }
                }
            }
        }

        private static void UpgradeCamera()
        {
            Camera camera = GameObject.Find("MainCamera_TopOblique").GetComponent<Camera>();
            Vector3 focus = new Vector3(0f, 0f, 2f);
            camera.transform.position = focus + new Vector3(30f, 38f, -43f);
            camera.transform.LookAt(focus);
            camera.orthographic = true;
            camera.orthographicSize = 14.5f;
            camera.backgroundColor = new Color(0.72f, 0.81f, 0.79f);
            camera.farClipPlane = 250f;
            MeadowCameraController controller = camera.GetComponent<MeadowCameraController>() ??
                camera.gameObject.AddComponent<MeadowCameraController>();
            controller.Configure(camera, focus, 14.5f);
        }

        private static void AddWelcomeLettering(Transform parent)
        {
            var sign = new GameObject("Meadow_Welcome_Lettering");
            sign.transform.SetParent(parent, false);
            sign.transform.position = new Vector3(0f, 2.32f, -12.73f);
            sign.transform.rotation = Quaternion.identity;
            TextMesh text = sign.AddComponent<TextMesh>();
            text.text = "MEADOW VILLAGE";
            text.fontSize = 48;
            text.characterSize = 0.048f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(0.29f, 0.34f, 0.24f);
        }

        private static void AddCameraHint()
        {
            Transform canvas = GameObject.Find("UI_Canvas").transform;
            Transform previous = canvas.Find("Meadow_Camera_Hint");
            if (previous != null)
            {
                Object.DestroyImmediate(previous.gameObject);
            }

            var hint = new GameObject("Meadow_Camera_Hint", typeof(RectTransform));
            hint.transform.SetParent(canvas, false);
            Text text = hint.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 16;
            text.text = "MEADOW VILLAGE   |   Scroll: zoom   Right drag: orbit   Middle drag / WASD: pan   Home: reset";
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.22f, 0.28f, 0.23f);
            text.raycastTarget = false;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = rect.anchorMin;
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 118f);
            rect.sizeDelta = new Vector2(1100f, 28f);
        }

        private static void ConfigureCompactHud()
        {
            Transform canvas = GameObject.Find("UI_Canvas").transform;
            RectTransform status = (RectTransform)canvas.Find("StatusPanel");
            status.localScale = Vector3.one * 0.75f;
            status.Find("TitleText").GetComponent<Text>().text = "MEADOW VILLAGE";
            RectTransform goal = (RectTransform)canvas.Find("GoalPanel");
            goal.localScale = Vector3.one * 0.7f;
            goal.anchoredPosition = new Vector2(20f, -168f);
            Transform old = canvas.Find("Meadow_Details_Button");
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            var buttonObject = new GameObject("Meadow_Details_Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(canvas, false);
            RectTransform rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-20f, -20f);
            rect.sizeDelta = new Vector2(190f, 46f);
            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.13f, 0.27f, 0.21f, 0.95f);
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = background;
            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(buttonObject.transform, false);
            Text label = labelObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 20;
            label.color = new Color(1f, 0.95f, 0.82f);
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            string[] names = { "WorldEventsPanel", "MemoryPanel", "BackpackPanel", "SaveControlsPanel" };
            GameObject[] panels = names.Select(name => canvas.Find(name).gameObject).ToArray();
            MeadowHudPresentation presentation = canvas.GetComponent<MeadowHudPresentation>() ??
                canvas.gameObject.AddComponent<MeadowHudPresentation>();
            presentation.Configure(panels, button, label);
            // Keep authoring controls discoverable in edit mode; Awake enters the compact runtime view.
            foreach (GameObject panel in panels)
            {
                panel.SetActive(true);
            }
        }
    }
}
