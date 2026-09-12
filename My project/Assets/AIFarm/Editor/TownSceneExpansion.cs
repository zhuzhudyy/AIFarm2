using System;
using System.Linq;
using AIFarm.Activities;
using AIFarm.Presentation;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AIFarm.Editor
{
    /// <summary>Editable, reproducible functional extension of the existing meadow.</summary>
    public static class TownSceneExpansion
    {
        public const string RootName = "Town_Expanded_Life";
        private const string LocationFolder = "Assets/AIFarm/Config/TownActivities";
        private const string MaterialFolder = "Assets/AIFarm/Art/Materials/Meadow/";

        [MenuItem("AIFarm/Town/Expand Playable Town and Save")]
        public static void ApplyAndSave()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != DemoSceneBuilder.ScenePath) throw new InvalidOperationException("Open DemoScene first.");
            Apply();
            NavMeshSurface surface = Object.FindFirstObjectByType<NavMeshSurface>();
            if (surface == null) throw new InvalidOperationException("DemoScene is missing its NavMeshSurface.");
            MeadowSceneUpgrade.ConfigureNavigation(surface);
            surface.BuildNavMesh();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        public static void Apply()
        {
            GameObject previous = GameObject.Find(RootName);
            if (previous != null) Object.DestroyImmediate(previous);
            Transform root = new GameObject(RootName).transform;
            EnsureFolder(LocationFolder);

            // 100 x 82 = 3.33 times the original 56 x 44 usable footprint.
            // Keep the existing art layer slightly above the new continuous floor.
            Box(root, "Expanded Meadow Foundation 100x82", new Vector3(0, -0.42f, 2),
                new Vector3(100, .7f, 82), "Grass", true);
            Box(root, "Western Garden", new Vector3(-37, -.025f, 3), new Vector3(21, .025f, 51), "Grass", false);
            Box(root, "Eastern Meadow", new Vector3(37, -.025f, 3), new Vector3(21, .025f, 51), "Grass", false);
            Box(root, "Northern Meadow", new Vector3(0, -.025f, 33), new Vector3(100, .025f, 20), "Grass", false);
            Box(root, "Southern Meadow", new Vector3(0, -.025f, -30), new Vector3(100, .025f, 18), "Grass", false);

            Path(root, "South Walk", new Vector3(-36, 0, -24), new Vector3(35, 0, -24), 2.8f);
            Path(root, "West Walk", new Vector3(-32, 0, -24), new Vector3(-32, 0, 30), 2.6f);
            Path(root, "North Walk", new Vector3(-32, 0, 30), new Vector3(33, 0, 30), 2.6f);
            Path(root, "East Walk", new Vector3(33, 0, 30), new Vector3(33, 0, -24), 2.6f);
            Path(root, "Entrance Extension", new Vector3(0, 0, -17), new Vector3(0, 0, -24), 2.6f);
            Path(root, "Orchard Garden Link", new Vector3(-19, 0, -10), new Vector3(-32, 0, -10), 2.6f);
            Path(root, "Lake East Link", new Vector3(23, 0, -11), new Vector3(33, 0, -14), 2.6f);
            Path(root, "Fishing Bank", new Vector3(10.4f, 0, -9), new Vector3(10.4f, 0, 1), 1.8f);
            Path(root, "Fishing Approach", new Vector3(5, 0, -9), new Vector3(10.4f, 0, -9), 1.8f);

            Transform garden = new GameObject("Garden_Rest_Area").transform;
            garden.SetParent(root, false);
            Box(garden, "Garden Plaza", new Vector3(-36, .005f, 3), new Vector3(10, .03f, 10), "Sand", false);
            Bench(garden, new Vector3(-38, 0, 1));
            Bench(garden, new Vector3(-38, 0, 5));
            Transform gardenFacing = Marker(garden, "Garden Center", new Vector3(-35, 0, 3));
            Arrival(root, "rest-garden", "林间休息区", new Vector3(-36, 0, 3), gardenFacing);
            Arrival(root, "forest-walk", "林荫步道", new Vector3(31, 0, 27),
                Marker(root, "Forest Outlook", new Vector3(37, 0, 32)));

            for (int i = 0; i < 9; i++)
            {
                Tree(root, new Vector3(-44 + i * 11, 0, 37), 1.25f + (i % 3) * .15f);
                Tree(root, new Vector3(-43 + i * 10.5f, 0, -33), 1.1f + (i % 2) * .2f);
            }
            for (int i = 0; i < 6; i++)
            {
                Tree(root, new Vector3(-45, 0, -16 + i * 8), 1.3f);
                Tree(root, new Vector3(44, 0, -15 + i * 8), 1.4f);
            }

            AddFishing(root, TownActivityResources.FishingOneId, new Vector3(10.4f, 0, -6));
            AddFishing(root, TownActivityResources.FishingTwoId, new Vector3(10.4f, 0, -1));
            Arrival(root, "farm-gate", "农田入口", new Vector3(0, 0, -4.2f),
                Marker(root, "Farm Center Facing", Vector3.zero));
            Vector3[] orchard = { new Vector3(-17, 0, -5.5f), new Vector3(-17, 0, -1.6f),
                new Vector3(-13.8f, 0, -5.5f), new Vector3(-13.8f, 0, -1.6f) };
            for (int i = 0; i < orchard.Length; i++) AddFruitTree(root, TownActivityResources.OrchardIds[i], orchard[i]);

            Transform wellFacing = Marker(root, "Existing Well Center", new Vector3(0, 0, -6.2f));
            LocationArrivalPoint well = Arrival(root, TownActivityResources.WellId, "水井与堆肥补给",
                new Vector3(0, 0, -7.7f), wellFacing);
            TextMesh wellLabel = Label(root, "Well Resource Label", "WELL + COMPOST", new Vector3(1.2f, 1.5f, -7));
            well.gameObject.AddComponent<ActivityResourceView>().Configure(TownActivityResources.WellId, Array.Empty<Renderer>(), wellLabel);
            Box(root, "Compost Bin", new Vector3(1.7f, .34f, -6.3f), new Vector3(.9f, .7f, .8f), "Wood", true, true);
            Box(root, "Compost Soil", new Vector3(1.7f, .72f, -6.3f), new Vector3(.8f, .1f, .7f), "Earth", false);

            AddExistingTreeCollision(root);
            AddWellCollision(root);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void AddFishing(Transform root, string id, Vector3 position)
        {
            Transform facing = Marker(root, id + " Water Facing", position + Vector3.right * 3f);
            LocationArrivalPoint point = Arrival(root, id, id == TownActivityResources.FishingOneId ? "南岸钓位" : "北岸钓位", position, facing);
            Box(root, id + " Bank Deck", position + Vector3.down * .01f, new Vector3(1.4f, .07f, 1.6f), "Wood", false);
            TextMesh label = Label(root, id + " Label", "FISHING · FREE", position + new Vector3(-.6f, 1.7f, .8f));
            point.gameObject.AddComponent<ActivityResourceView>().Configure(id, Array.Empty<Renderer>(), label);
            Box(root, id + " Rod Rack", position + new Vector3(-.6f, .55f, -.7f), new Vector3(.07f, 1.1f, .08f), "Wood", false);
        }

        private static void AddFruitTree(Transform root, string id, Vector3 center)
        {
            Transform facing = Marker(root, id + " Tree Center", center);
            LocationArrivalPoint point = Arrival(root, id, "果树 " + id.Substring(id.Length - 1), center + Vector3.right * 1.15f, facing);
            Renderer[] existing = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(r => r.name.StartsWith("Orchard peaches", StringComparison.Ordinal) &&
                    Vector2.Distance(new Vector2(r.bounds.center.x, r.bounds.center.z), new Vector2(center.x, center.z)) < 1.1f)
                .OrderBy(r => r.name).Take(3).Cast<Renderer>().ToArray();
            if (existing.Length != 3)
            {
                existing = new Renderer[3];
                for (int i = 0; i < 3; i++)
                {
                    GameObject fruit = Primitive(root, id + " Ripe Fruit " + i, PrimitiveType.Sphere,
                        center + new Vector3(-.55f + i * .48f, 2.1f + i * .17f, -.4f), Vector3.one * .3f, "Fruit", false);
                    existing[i] = fruit.GetComponent<Renderer>();
                }
            }
            TextMesh label = Label(root, id + " Label", "FRUIT 3/3", center + new Vector3(0, 3.5f, 0));
            point.gameObject.AddComponent<ActivityResourceView>().Configure(id, existing, label);
        }

        private static LocationArrivalPoint Arrival(Transform parent, string id, string name, Vector3 position, Transform facing)
        {
            string path = LocationFolder + "/" + id + ".asset";
            TownLocationDefinitionAsset asset = AssetDatabase.LoadAssetAtPath<TownLocationDefinitionAsset>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<TownLocationDefinitionAsset>();
                AssetDatabase.CreateAsset(asset, path);
            }
            if (asset.Configure(id, name).Failed) throw new InvalidOperationException("Invalid activity location: " + id);
            EditorUtility.SetDirty(asset);
            LocationArrivalPoint point = Marker(parent, id, position).gameObject.AddComponent<LocationArrivalPoint>();
            if (point.Configure(asset, id, facing).Failed) throw new InvalidOperationException("Invalid arrival point: " + id);
            return point;
        }

        private static void AddExistingTreeCollision(Transform root)
        {
            foreach (MeshRenderer trunk in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(r => r.name.StartsWith("Tree trunk", StringComparison.Ordinal)))
            {
                Bounds bounds = trunk.bounds;
                Transform obstacle = Marker(root, "Existing Tree Trunk Collider", bounds.center);
                CapsuleCollider collider = obstacle.gameObject.AddComponent<CapsuleCollider>();
                collider.radius = Mathf.Max(.16f, Mathf.Max(bounds.size.x, bounds.size.z) * .5f);
                collider.height = Mathf.Max(.7f, bounds.size.y);
                BlockNavigation(obstacle.gameObject);
            }
        }

        private static void AddWellCollision(Transform root)
        {
            Transform obstacle = Marker(root, "Existing Well Collider", new Vector3(0, .8f, -6.2f));
            CapsuleCollider collider = obstacle.gameObject.AddComponent<CapsuleCollider>();
            collider.radius = .78f;
            collider.height = 1.6f;
            BlockNavigation(obstacle.gameObject);
        }

        private static void Tree(Transform root, Vector3 p, float scale)
        {
            Primitive(root, "Garden Tree Trunk", PrimitiveType.Cylinder, p + Vector3.up * 1.25f * scale,
                new Vector3(.34f, 1.25f, .34f) * scale, "Bark", true, true);
            Primitive(root, "Garden Tree Crown", PrimitiveType.Sphere, p + Vector3.up * 2.7f * scale,
                new Vector3(2.3f, 2.5f, 2.2f) * scale, "Leaf", false);
        }

        private static void Bench(Transform root, Vector3 p)
        {
            Box(root, "Garden Bench Seat", p + new Vector3(0, .5f, 0), new Vector3(2, .15f, .65f), "Wood", true, true);
            Box(root, "Garden Bench Back", p + new Vector3(0, 1, .3f), new Vector3(2, .6f, .12f), "Wood", false);
            foreach (float x in new[] { -.7f, .7f })
                Box(root, "Garden Bench Leg", p + new Vector3(x, .23f, 0), new Vector3(.14f, .46f, .5f), "Wood", false);
        }

        private static void Path(Transform root, string name, Vector3 from, Vector3 to, float width)
        {
            GameObject path = Box(root, name, (from + to) * .5f + Vector3.up * .006f,
                new Vector3(width, .022f, Vector3.Distance(from, to)), "Sand", false);
            path.transform.rotation = Quaternion.LookRotation(to - from);
        }

        private static Transform Marker(Transform root, string name, Vector3 position)
        {
            Transform item = new GameObject(name).transform;
            item.SetParent(root, false);
            item.position = position;
            return item;
        }

        private static TextMesh Label(Transform root, string name, string text, Vector3 position)
        {
            Transform marker = Marker(root, name, position);
            TextMesh label = marker.gameObject.AddComponent<TextMesh>();
            label.text = text;
            label.fontSize = 48;
            label.characterSize = .055f;
            label.anchor = TextAnchor.MiddleCenter;
            label.color = new Color(.16f, .23f, .14f);
            marker.gameObject.AddComponent<WorldSpaceBillboard>();
            return label;
        }

        private static GameObject Box(Transform root, string name, Vector3 p, Vector3 scale, string material,
            bool collider, bool obstacle = false) => Primitive(root, name, PrimitiveType.Cube, p, scale, material, collider, obstacle);

        private static GameObject Primitive(Transform root, string name, PrimitiveType primitive, Vector3 p,
            Vector3 scale, string material, bool collider, bool obstacle = false)
        {
            GameObject item = GameObject.CreatePrimitive(primitive);
            item.name = name;
            item.transform.SetParent(root, false);
            item.transform.position = p;
            item.transform.localScale = scale;
            item.isStatic = true;
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "Meadow_" + material + ".mat");
            if (existing == null) throw new InvalidOperationException("Missing meadow material " + material);
            item.GetComponent<Renderer>().sharedMaterial = existing;
            if (!collider) Object.DestroyImmediate(item.GetComponent<Collider>());
            if (obstacle) BlockNavigation(item);
            return item;
        }

        private static void BlockNavigation(GameObject item)
        {
            NavMeshModifier modifier = item.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = NavMesh.GetAreaFromName("Not Walkable");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
