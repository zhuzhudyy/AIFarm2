using System;
using AIFarm.Ai;
using AIFarm.Core;
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
        private float timeScale = 20f;

        [Min(0.1f)]
        [SerializeField]
        private float waterDecayGameSeconds = 10f;

        [Min(0.1f)]
        [SerializeField]
        private float weedDelayGameSeconds = 15f;

        [Min(0.1f)]
        [SerializeField]
        private float maturityGameSeconds = 30f;

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

        [Range(1, 3)]
        [SerializeField]
        private int aiRequestTimeoutSeconds = 3;

        public DemoInventoryConfig InventoryConfig => inventoryConfig;

        public int StartDay => startDay;

        public int StartHour => startHour;

        public float TimeScale => timeScale;

        public float PlotSize => plotSize;

        public float PlotSpacing => plotSpacing;

        public float WaterDecayGameSeconds => waterDecayGameSeconds;

        public float WeedDelayGameSeconds => weedDelayGameSeconds;

        public float MaturityGameSeconds => maturityGameSeconds;

        public float ExpressionCooldownSeconds => expressionCooldownSeconds;

        public float ExpressionDisplaySeconds => expressionDisplaySeconds;

        public AiGatewayMode AiGatewayMode => aiGatewayMode;

        public string AiGatewayBaseUrl => string.IsNullOrWhiteSpace(aiGatewayBaseUrl)
            ? DefaultAiGatewayBaseUrl
            : aiGatewayBaseUrl;

        public int AiRequestTimeoutSeconds => aiRequestTimeoutSeconds > 0
            ? aiRequestTimeoutSeconds
            : 3;

        public double InitialElapsedGameSeconds => ((startDay - 1) * 24d + startHour) * 60d * 60d;

        public DemoMode CreateDemoMode()
        {
            return new DemoMode(
                recommendedTimeScale: timeScale,
                waterDecayGameSeconds: waterDecayGameSeconds,
                weedDelayGameSeconds: weedDelayGameSeconds,
                maturityGameSeconds: maturityGameSeconds,
                sowActionSeconds: farmActionSeconds,
                fertilizeActionSeconds: farmActionSeconds * 1.1f,
                waterActionSeconds: farmActionSeconds,
                weedActionSeconds: farmActionSeconds,
                harvestActionSeconds: farmActionSeconds * 1.25f,
                waitActionSeconds: waitActionSeconds,
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
            float waterDecaySeconds = 10f,
            float weedDelaySeconds = 15f,
            float maturitySeconds = 30f,
            float actionSeconds = 0.2f,
            float waitSeconds = 0.2f,
            float expressionCooldown = 12f,
            float expressionDisplay = 2.5f,
            AiGatewayMode gatewayMode = AiGatewayMode.Local,
            string gatewayBaseUrl = DefaultAiGatewayBaseUrl,
            int requestTimeoutSeconds = 3)
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

            if (requestTimeoutSeconds < 1 || requestTimeoutSeconds > 3)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestTimeoutSeconds),
                    "AI gateway timeout must be between 1 and 3 seconds for the demo.");
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
