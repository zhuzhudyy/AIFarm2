using UnityEngine;

namespace AIFarm.Presentation
{
    [DisallowMultipleComponent]
    public sealed class TownDayNightLighting : MonoBehaviour
    {
        [SerializeField] private Light sun;
        [SerializeField] private float daylightIntensity = 1.25f;
        [SerializeField] private float moonlightIntensity = .32f;
        private GameBootstrap bootstrap;

        private void Start()
        {
            bootstrap = FindFirstObjectByType<GameBootstrap>();
            if (sun == null)
                foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (light.type == LightType.Directional)
                    {
                        sun = light;
                        break;
                    }
        }
        private void Update()
        {
            if (bootstrap?.Clock == null || sun == null)
                return;
            float hour = (float)(bootstrap.Clock.ElapsedGameSeconds % 86400 / 3600);
            float daylight = Mathf.SmoothStep(0, 1, Mathf.Clamp01((hour - 5) / 2)) *
                (1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((hour - 18) / 2)));
            sun.intensity = Mathf.Lerp(moonlightIntensity, daylightIntensity, daylight);
            sun.color = Color.Lerp(new Color(.47f, .61f, .95f), new Color(1, .92f, .77f), daylight);
            sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(24, 64, Mathf.Sin(Mathf.Clamp01((hour - 6) / 13) * Mathf.PI)), -35, 0);
            RenderSettings.ambientLight = Color.Lerp(new Color(.16f, .22f, .36f), new Color(.6f, .67f, .56f), daylight);
        }
    }
}
