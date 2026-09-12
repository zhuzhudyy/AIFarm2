using System;
using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Activities;
using UnityEngine;

namespace AIFarm.Presentation
{
    [CreateAssetMenu(fileName = "DemoSceneConfig", menuName = "AIFarm/Demo Scene Config")]
    public sealed class DemoSceneConfig : ScriptableObject
    {
        public const string DefaultAiGatewayBaseUrl = "http://127.0.0.1:8000";

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
        private float timeScale = 1f;

        [Min(60f)]
        [SerializeField]
        private float realSecondsPerDay = 720f;

        [Tooltip("Explicit fast-growth demonstration. Normal production crops take two game days.")]
        [SerializeField]
        private bool fastGrowthDemo;

        [Min(1)]
        [SerializeField]
        private int seedsReturnedPerCrop = 1;

        [Min(1)]
        [SerializeField]
        private int cropYield = 1;

        [Header("Town activities (all durations are simulation seconds)")]
        [SerializeField]
        private ActivityRules activityRules = new ActivityRules();

        [Min(0.1f)]
        [SerializeField]
        private float waterDecayGameSeconds = 28800f;

        [Min(0.1f)]
        [SerializeField]
        private float weedDelayGameSeconds = 14400f;

        [Min(0.1f)]
        [SerializeField]
        private float maturityGameSeconds = 172800f;

        [Min(0.01f)]
        [SerializeField]
        private float farmActionSeconds = 0.2f;

        [Min(0.01f)]
        [SerializeField]
        private float waitActionSeconds = 0.2f;

        [Min(0.1f)]
        [SerializeField]
        private float expressionCooldownSeconds = 12f;

        [Min(0.1f)]
        [SerializeField]
        private float expressionDisplaySeconds = 2.5f;

        [Min(0.1f)]
        [SerializeField]
        private float plotSize = 2f;

        [Min(0f)]
        [SerializeField]
        private float plotSpacing = 0.35f;

        [Header("AI Gateway")]
        [SerializeField]
        private AiGatewayMode aiGatewayMode = AiGatewayMode.Local;

        [Tooltip("HTTP(S) URL of the trusted AI gateway. Never enter an OpenAI API key here.")]
        [SerializeField]
        private string aiGatewayBaseUrl = DefaultAiGatewayBaseUrl;

        [Range(1, 60)]
        [SerializeField]
        private int aiRequestTimeoutSeconds = 30;

        [Range(1, 4)]
        [SerializeField]
        private int maximumConcurrentAiRequests = 2;

        public DemoInventoryConfig InventoryConfig => inventoryConfig;

        public int StartDay => startDay;

        public int StartHour => startHour;

        public float TimeScale => timeScale;

        public double GameSecondsPerRealSecond => 86400d / Math.Max(60f, realSecondsPerDay);

        public bool FastGrowthDemo => fastGrowthDemo;

        public int SeedsReturnedPerCrop => Math.Max(1, seedsReturnedPerCrop);

        public int CropYield => Math.Max(1, cropYield);

        public ActivityRules CreateActivityRules()
        {
            ActivityRules source = activityRules ?? new ActivityRules();
            return new ActivityRules
            {
                fishingGameSeconds = source.fishingGameSeconds,
                pickingGameSeconds = source.pickingGameSeconds,
                supplyGameSeconds = source.supplyGameSeconds,
                fruitRegrowthGameSeconds = source.fruitRegrowthGameSeconds,
                fruitsPerTree = source.fruitsPerTree,
                fishPerCatch = source.fishPerCatch,
                waterCarryCapacity = source.waterCarryCapacity,
                fertilizerCarryCapacity = source.fertilizerCarryCapacity
            };
        }

        public float PlotSize => plotSize;

        public float PlotSpacing => plotSpacing;

        public float WaterDecayGameSeconds => fastGrowthDemo ? 10f : waterDecayGameSeconds;

        public float WeedDelayGameSeconds => fastGrowthDemo ? 15f : weedDelayGameSeconds;

        public float MaturityGameSeconds => fastGrowthDemo ? 30f : maturityGameSeconds;

        public float ExpressionCooldownSeconds => expressionCooldownSeconds;

        public float ExpressionDisplaySeconds => expressionDisplaySeconds;

        public AiGatewayMode AiGatewayMode => aiGatewayMode;

        public string AiGatewayBaseUrl => string.IsNullOrWhiteSpace(aiGatewayBaseUrl)
            ? DefaultAiGatewayBaseUrl
            : aiGatewayBaseUrl;

        public int AiRequestTimeoutSeconds => aiRequestTimeoutSeconds > 0
            ? aiRequestTimeoutSeconds
            : 30;

        public int MaximumConcurrentAiRequests => maximumConcurrentAiRequests > 0
            ? maximumConcurrentAiRequests
            : AiRequestCoordinator.DefaultMaximumConcurrentRequests;

        public double InitialElapsedGameSeconds => ((startDay - 1) * 24d + startHour) * 60d * 60d;

        public DemoMode CreateDemoMode()
        {
            return new DemoMode(
                recommendedTimeScale: timeScale,
                waterDecayGameSeconds: WaterDecayGameSeconds,
                weedDelayGameSeconds: WeedDelayGameSeconds,
                maturityGameSeconds: MaturityGameSeconds,
                sowActionSeconds: farmActionSeconds * (float)GameSecondsPerRealSecond,
                fertilizeActionSeconds: farmActionSeconds * 1.1f * (float)GameSecondsPerRealSecond,
                waterActionSeconds: farmActionSeconds * (float)GameSecondsPerRealSecond,
                weedActionSeconds: farmActionSeconds * (float)GameSecondsPerRealSecond,
                harvestActionSeconds: farmActionSeconds * 1.25f * (float)GameSecondsPerRealSecond,
                waitActionSeconds: waitActionSeconds * (float)GameSecondsPerRealSecond,
                expressionCooldownSeconds: expressionCooldownSeconds,
                expressionDisplaySeconds: expressionDisplaySeconds);
        }

        public void Configure(
            DemoInventoryConfig inventory,
            int day,
            int hour,
            float scale,
            float size,
            float spacing,
            float waterDecaySeconds = 28800f,
            float weedDelaySeconds = 14400f,
            float maturitySeconds = 172800f,
            float actionSeconds = 0.2f,
            float waitSeconds = 0.2f,
            float expressionCooldown = 12f,
            float expressionDisplay = 2.5f,
            AiGatewayMode gatewayMode = AiGatewayMode.Local,
            string gatewayBaseUrl = DefaultAiGatewayBaseUrl,
            int requestTimeoutSeconds = 30,
            int maxConcurrentAiRequests = AiRequestCoordinator.DefaultMaximumConcurrentRequests)
        {
            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            if (day < 1 || hour < 0 || hour > 23 || scale <= 0f || size <= 0f || spacing < 0f ||
                waterDecaySeconds <= 0f || weedDelaySeconds <= 0f || maturitySeconds <= 0f ||
                actionSeconds <= 0f || waitSeconds <= 0f || expressionCooldown <= 0f ||
                expressionDisplay <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(day), "Demo scene settings are outside their supported range.");
            }

            if (!Enum.IsDefined(typeof(AiGatewayMode), gatewayMode))
            {
                throw new ArgumentOutOfRangeException(nameof(gatewayMode));
            }

            if (!IsSafeGatewayBaseUrl(gatewayBaseUrl))
            {
                throw new ArgumentException(
                    "AI gateway URL must be an HTTP(S) base URL without credentials, query, or fragment.",
                    nameof(gatewayBaseUrl));
            }

            if (requestTimeoutSeconds < 1 || requestTimeoutSeconds > 60)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestTimeoutSeconds),
                    "AI gateway timeout must be between 1 and 60 seconds.");
            }

            if (maxConcurrentAiRequests < 1 || maxConcurrentAiRequests > 4)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxConcurrentAiRequests),
                    "AI concurrency must be between 1 and 4 for the demo.");
            }

            inventoryConfig = inventory;
            startDay = day;
            startHour = hour;
            timeScale = scale;
            plotSize = size;
            plotSpacing = spacing;
            waterDecayGameSeconds = waterDecaySeconds;
            weedDelayGameSeconds = weedDelaySeconds;
            maturityGameSeconds = maturitySeconds;
            farmActionSeconds = actionSeconds;
            waitActionSeconds = waitSeconds;
            expressionCooldownSeconds = expressionCooldown;
            expressionDisplaySeconds = expressionDisplay;
            aiGatewayMode = gatewayMode;
            aiGatewayBaseUrl = gatewayBaseUrl.Trim().TrimEnd('/');
            aiRequestTimeoutSeconds = requestTimeoutSeconds;
            maximumConcurrentAiRequests = maxConcurrentAiRequests;
        }

        private static bool IsSafeGatewayBaseUrl(string value)
        {
            string candidate = (value ?? string.Empty).Trim();
            return Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                !string.IsNullOrWhiteSpace(uri.Host) &&
                string.IsNullOrEmpty(uri.UserInfo) &&
                string.IsNullOrEmpty(uri.Query) &&
                string.IsNullOrEmpty(uri.Fragment);
        }
    }
}
