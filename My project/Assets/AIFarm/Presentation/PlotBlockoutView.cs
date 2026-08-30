using AIFarm.Core;
using AIFarm.Farming;
using UnityEngine;

namespace AIFarm.Presentation
{
    [DisallowMultipleComponent]
    public sealed class PlotBlockoutView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField]
        private GameBootstrap bootstrap;

        [SerializeField]
        private int plotNumber = 1;

        [SerializeField]
        private Renderer soilRenderer;

        [SerializeField]
        private GameObject cropVisual;

        [SerializeField]
        private Renderer cropRenderer;

        [SerializeField]
        private GameObject weedVisual;

        private MaterialPropertyBlock soilProperties;
        private MaterialPropertyBlock cropProperties;
        private MaterialPropertyBlock weedProperties;
        private Renderer[] weedRenderers;

        public ActionResult Configure(
            GameBootstrap gameBootstrap,
            int number,
            Renderer soil,
            GameObject crop,
            Renderer cropBody,
            GameObject weeds)
        {
            if (gameBootstrap == null || number < 1 || number > FarmField.PlotCount ||
                soil == null || crop == null || cropBody == null || weeds == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "PlotBlockoutView requires a valid plot and all blockout renderers.");
            }

            bootstrap = gameBootstrap;
            plotNumber = number;
            soilRenderer = soil;
            cropVisual = crop;
            cropRenderer = cropBody;
            weedVisual = weeds;
            weedRenderers = weedVisual.GetComponentsInChildren<Renderer>(includeInactive: true);
            cropVisual.SetActive(false);
            weedVisual.SetActive(false);
            return ActionResult.Success($"Plot {number:00} blockout view configured.");
        }

        public void Refresh()
        {
            if (bootstrap == null || !bootstrap.IsInitialized || soilRenderer == null ||
                cropVisual == null || cropRenderer == null || weedVisual == null)
            {
                return;
            }

            soilProperties ??= new MaterialPropertyBlock();
            cropProperties ??= new MaterialPropertyBlock();
            weedProperties ??= new MaterialPropertyBlock();
            weedRenderers ??= weedVisual.GetComponentsInChildren<Renderer>(includeInactive: true);
            FarmPlot plot = bootstrap.Field.GetPlot(plotNumber);

            Color soilColor = SoilColorFor(plot);
            SetColor(soilRenderer, soilProperties, soilColor);

            bool hasCrop = plot.State != PlotState.Empty;
            cropVisual.SetActive(hasCrop);
            weedVisual.SetActive(plot.HasWeeds);
            if (plot.HasWeeds)
            {
                foreach (Renderer weedRenderer in weedRenderers)
                {
                    SetColor(weedRenderer, weedProperties, new Color(0.22f, 0.65f, 0.12f));
                }
            }
            if (!hasCrop)
            {
                return;
            }

            float normalizedGrowth = plot.State == PlotState.Mature
                ? 1f
                : Mathf.Clamp01(plot.GrowthProgress / (float)FarmPlot.RequiredGrowth);
            float scale = Mathf.Lerp(0.35f, 1f, normalizedGrowth);
            cropVisual.transform.localScale = new Vector3(0.55f * scale, 0.85f * scale, 0.55f * scale);
            Color cropColor = plot.State == PlotState.Mature
                ? new Color(1f, 0.36f, 0.04f)
                : Color.Lerp(new Color(0.34f, 0.72f, 0.18f), new Color(0.95f, 0.55f, 0.08f), normalizedGrowth);
            SetColor(cropRenderer, cropProperties, cropColor);
        }

        private void Update()
        {
            Refresh();
        }

        private static Color SoilColorFor(FarmPlot plot)
        {
            if (plot.State == PlotState.Empty)
            {
                return new Color(0.38f, 0.20f, 0.08f);
            }

            if (plot.HasWeeds)
            {
                return new Color(0.25f, 0.38f, 0.10f);
            }

            if (plot.WaterLevel >= FarmPlot.MaximumWaterLevel)
            {
                return new Color(0.16f, 0.28f, 0.32f);
            }

            if (plot.WaterLevel >= FarmPlot.RequiredWaterLevel)
            {
                return new Color(0.25f, 0.22f, 0.16f);
            }

            return new Color(0.48f, 0.30f, 0.13f);
        }

        private static void SetColor(
            Renderer target,
            MaterialPropertyBlock properties,
            Color color)
        {
            target.GetPropertyBlock(properties);
            properties.SetColor(BaseColorId, color);
            properties.SetColor(ColorId, color);
            target.SetPropertyBlock(properties);
        }
    }
}
