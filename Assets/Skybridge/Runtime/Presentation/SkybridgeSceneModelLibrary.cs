using UnityEngine;

namespace Skybridge.Presentation
{
    /// <summary>
    /// Scene-authored model templates used by both the Unity Scene view and
    /// the runtime Game view.  The templates stay editable in the scene; the
    /// runtime clones them for each level endpoint and hides the authoring
    /// preview so it cannot render a second copy.
    /// </summary>
    public sealed class SkybridgeSceneModelLibrary : MonoBehaviour
    {
        [SerializeField] private GameObject islandTemplate;
        [SerializeField] private GameObject goalIslandTemplate;
        [SerializeField] private GameObject rescueVehicleTemplate;
        [SerializeField] private GameObject goalGateTemplate;
        [SerializeField] private Transform previewRoot;

        public GameObject IslandTemplate => islandTemplate;
        public GameObject GoalIslandTemplate => goalIslandTemplate;
        public GameObject RescueVehicleTemplate => rescueVehicleTemplate;
        public GameObject GoalGateTemplate => goalGateTemplate;
        public Transform PreviewRoot => previewRoot;

        public bool IsConfigured => islandTemplate != null &&
                                     goalIslandTemplate != null &&
                                     rescueVehicleTemplate != null &&
                                     goalGateTemplate != null;

        public bool TryGetTemplate(string resourcePath, out GameObject template)
        {
            return TryGetTemplate(resourcePath, false, out template);
        }

        public bool TryGetTemplate(string resourcePath, bool goalEndpoint, out GameObject template)
        {
            template = null;
            if (string.IsNullOrEmpty(resourcePath)) return false;

            if (resourcePath.Contains("/Island/"))
            {
                template = goalEndpoint && goalIslandTemplate != null
                    ? goalIslandTemplate
                    : islandTemplate;
            }
            else if (resourcePath.Contains("/RescueVehicle/")) template = rescueVehicleTemplate;
            else if (resourcePath.Contains("/GoalGate/")) template = goalGateTemplate;

            return template != null;
        }

        public void SetPreviewVisible(bool visible)
        {
            if (previewRoot != null) previewRoot.gameObject.SetActive(visible);
        }

        public void AssignTemplates(
            GameObject island,
            GameObject goalIsland,
            GameObject rescueVehicle,
            GameObject goalGate,
            Transform preview)
        {
            islandTemplate = island;
            goalIslandTemplate = goalIsland;
            rescueVehicleTemplate = rescueVehicle;
            goalGateTemplate = goalGate;
            previewRoot = preview;
        }
    }
}
