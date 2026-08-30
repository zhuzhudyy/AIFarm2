using System;
using UnityEngine;

namespace AIFarm.Presentation
{
    [CreateAssetMenu(fileName = "DemoSceneConfig", menuName = "AIFarm/Demo Scene Config")]
    public sealed class DemoSceneConfig : ScriptableObject
    {
        [SerializeField]
        private DemoInventoryConfig inventoryConfig;

        [Min(1)]
        [SerializeField]
        private int startDay = 1;

        [Range(0, 23)]
        [SerializeField]
        private int startHour = 8;

        [Min(0.01f)]
        [SerializeField]
        private float timeScale = 60f;

        [Min(0.1f)]
        [SerializeField]
        private float plotSize = 2f;

        [Min(0f)]
        [SerializeField]
        private float plotSpacing = 0.35f;

        public DemoInventoryConfig InventoryConfig => inventoryConfig;

        public int StartDay => startDay;

        public int StartHour => startHour;

        public float TimeScale => timeScale;

        public float PlotSize => plotSize;

        public float PlotSpacing => plotSpacing;

        public double InitialElapsedGameSeconds => ((startDay - 1) * 24d + startHour) * 60d * 60d;

        public void Configure(
            DemoInventoryConfig inventory,
            int day,
            int hour,
            float scale,
            float size,
            float spacing)
        {
            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            if (day < 1 || hour < 0 || hour > 23 || scale <= 0f || size <= 0f || spacing < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(day), "Demo scene settings are outside their supported range.");
            }

            inventoryConfig = inventory;
            startDay = day;
            startHour = hour;
            timeScale = scale;
            plotSize = size;
            plotSpacing = spacing;
        }
    }
}
