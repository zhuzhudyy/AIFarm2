using System;
using UnityEngine;

namespace AIFarm.Presentation
{
    [CreateAssetMenu(fileName = "DemoInventoryConfig", menuName = "AIFarm/Demo Inventory Config")]
    public sealed class DemoInventoryConfig : ScriptableObject
    {
        [Min(0)]
        [SerializeField]
        private int carrotSeeds = 9;

        [Min(0)]
        [SerializeField]
        private int water = 9;

        [Min(0)]
        [SerializeField]
        private int fertilizer = 9;

        [Min(0)]
        [SerializeField]
        private int carrots;

        public int CarrotSeeds => carrotSeeds;

        public int Water => water;

        public int Fertilizer => fertilizer;

        public int Carrots => carrots;

        public void Configure(int seedCount, int waterCount, int fertilizerCount, int carrotCount)
        {
            if (seedCount < 0 || waterCount < 0 || fertilizerCount < 0 || carrotCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seedCount), "Inventory counts cannot be negative.");
            }

            carrotSeeds = seedCount;
            water = waterCount;
            fertilizer = fertilizerCount;
            carrots = carrotCount;
        }
    }
}
