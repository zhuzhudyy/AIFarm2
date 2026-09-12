using AIFarm.Activities;
using UnityEngine;

namespace AIFarm.Presentation
{
    public sealed class ActivityResourceView : MonoBehaviour
    {
        [SerializeField] private string targetId;
        [SerializeField] private Renderer[] fruitRenderers;
        [SerializeField] private TextMesh status;
        private GameBootstrap bootstrap;

        public string TargetId => targetId;

        public void Configure(string id, Renderer[] fruits, TextMesh label)
        {
            targetId = id;
            fruitRenderers = fruits;
            status = label;
        }

        private void Start() => bootstrap = FindFirstObjectByType<GameBootstrap>();

        private void Update()
        {
            TownActivityResources resources = bootstrap?.Activities;
            if (resources == null) return;
            if (fruitRenderers != null && fruitRenderers.Length > 0)
            {
                int remaining = resources.GetFruitRemaining(targetId);
                for (int i = 0; i < fruitRenderers.Length; i++)
                    if (fruitRenderers[i] != null) fruitRenderers[i].enabled = i < remaining;
                if (status != null) status.text = remaining > 0 ? $"FRUIT {remaining}/{resources.Rules.fruitsPerTree}" :
                    $"REGROW {resources.GetRegrowthRemaining(targetId) / 3600d:0.0}h";
            }
            else if (status != null)
                status.text = targetId == TownActivityResources.WellId ? "WELL + COMPOST" :
                    resources.IsAvailable(targetId) ? "FISHING · FREE" : "FISHING · IN USE";
        }
    }
}
