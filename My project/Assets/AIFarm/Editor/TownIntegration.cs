using AIFarm.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace AIFarm.Editor
{
    public static class TownIntegration
    {
        public static void ApplyAndSave()
        {
            Apply();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Stop Play first.");
            GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            var points = Object.FindObjectsByType<PlotInteractionPoint>(FindObjectsSortMode.None);
            foreach (var resident in Object.FindObjectsByType<TownResidentScheduleController>(FindObjectsSortMode.None))
            {
                var navigator = resident.GetComponent<NpcNavigator>() ?? resident.gameObject.AddComponent<NpcNavigator>();
                navigator.Configure(resident.GetComponent<NavMeshAgent>(), points, true, 4);
                var feedback = resident.GetComponent<BlockoutActionFeedback>() ?? resident.gameObject.AddComponent<BlockoutActionFeedback>();
                Transform visual = resident.transform.Find("NPC_Visual_Capsule") ?? resident.transform;
                feedback.Configure(visual, null, null);
                var executor = resident.GetComponent<NpcPlanExecutor>() ?? resident.gameObject.AddComponent<NpcPlanExecutor>();
                executor.Configure(resident.ResidentId, bootstrap, navigator, feedback);
                if (resident.GetComponent<TownLifeController>() == null) resident.gameObject.AddComponent<TownLifeController>();
            }
            if (bootstrap.GetComponent<TownDayNightLighting>() == null) bootstrap.gameObject.AddComponent<TownDayNightLighting>();
            if (bootstrap.GetComponent<TownDialogueOverlay>() == null) bootstrap.gameObject.AddComponent<TownDialogueOverlay>();
            // The three configuration inputs are created at runtime to avoid persisting secrets in scene YAML.
        }
    }
}
