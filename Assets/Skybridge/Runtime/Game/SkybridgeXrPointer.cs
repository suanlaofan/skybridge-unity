using System.Collections.Generic;
using System.Linq;
using Skybridge.Presentation;
using Skybridge.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Skybridge.Game
{
    /// <summary>One authoritative pointer owns UI hover/press/release and world placement.</summary>
    public sealed class SkybridgeXrPointer : MonoBehaviour
    {
        private SkybridgeGameController game;
        private SkybridgeXrWorkbenchView view;
        private Camera camera;
        private PointerEventData data;
        private GameObject hover, pressedUi;
        private SkybridgePickable pressedWorld;
        private bool pressedOnUi, wasHeld, tracked, pressedModal;
        private bool releaseRequired = true;
        private LineRenderer line;
        private Material lineMaterial;
        private int pressedLayer;
        public bool IsArmed => tracked && !releaseRequired;
        public GameObject HoverTarget => hover;
        public int CommittedClicks { get; private set; }

        public void Initialize(SkybridgeGameController controller, SkybridgeXrWorkbenchView workbench, Camera xrCamera)
        {
            game = controller; view = workbench; camera = xrCamera;
            data = new PointerEventData(EventSystem.current) { pointerId = 601, button = PointerEventData.InputButton.Left };
            var rayObject = new GameObject("Right Controller Ray");
            rayObject.transform.SetParent(transform, false);
            line = rayObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true; line.positionCount = 2;
            line.startWidth = 0.002f; line.endWidth = 0.003f;
            lineMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            lineMaterial.color = new Color(0.15f, 0.7f, 0.82f);
            line.sharedMaterial = lineMaterial;
            line.enabled = false;
        }

        public void Process(Ray ray, bool trackingValid, bool held)
        {
            if (game == null || view == null || data == null) return;
            tracked = trackingValid;
            game.XrSetTracking(trackingValid);
            if (!trackingValid) { CancelInput(); wasHeld = held; return; }
            var uiHit = TryUiHit(view.Canvases, camera, ray, out var target, out var uiPoint, out var uiDistance);
            if (uiHit && IsOccluded(ray, uiDistance, null)) { uiHit = false; target = null; }
            var worldTarget = default(SkybridgePickable);
            var worldPoint = ray.GetPoint(2f);
            if (!uiHit && !game.XrInteractionBlocked) TryWorldHit(ray, out worldTarget, out worldPoint);
            SetHover(uiHit ? target : null);
            var endPoint = uiHit ? uiPoint : worldTarget != null ? worldPoint : ray.GetPoint(2f);
            data.position = camera.WorldToScreenPoint(endPoint);
            data.pointerCurrentRaycast = new RaycastResult { gameObject = target, worldPosition = endPoint, distance = Vector3.Distance(ray.origin, endPoint) };
            // Screen-driven rays begin at the eye. Drawing that beam across the near
            // plane produces a wide stripe; hover and placement previews already show its target.
            line.enabled = !game.XrUsesScreenPointer;
            line.SetPosition(0, ray.origin + ray.direction * 0.035f);
            line.SetPosition(1, endPoint);
            if (uiHit || game.XrInteractionBlocked) game.XrHidePreview();
            else game.XrPreviewWorldPick(worldTarget);
            if (releaseRequired)
            {
                if (!held) releaseRequired = false;
                wasHeld = held;
                return;
            }
            if (held && !wasHeld)
            {
                pressedOnUi = uiHit;
                pressedModal = view.HasModal;
                pressedLayer = game.Builder.ActiveLayer;
                pressedWorld = !uiHit && !game.XrInteractionBlocked ? worldTarget : null;
                data.pressPosition = data.position;
                data.pointerPressRaycast = data.pointerCurrentRaycast;
                data.eligibleForClick = true;
                pressedUi = uiHit ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) : null;
                data.pointerPress = pressedUi; data.rawPointerPress = target;
                if (target != null) ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerDownHandler);
            }
            if (!held && wasHeld)
            {
                var capture = pressedUi;
                if (capture != null) ExecuteEvents.Execute(capture, data, ExecuteEvents.pointerUpHandler);
                var clickUi = capture != null && uiHit && capture == ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) && pressedModal == view.HasModal;
                var clickWorld = !pressedOnUi && !uiHit && !game.XrInteractionBlocked && pressedWorld != null && worldTarget == pressedWorld && pressedLayer == game.Builder.ActiveLayer;
                ClearCapture();
                if (clickUi) { ExecuteEvents.Execute(capture, data, ExecuteEvents.pointerClickHandler); CommittedClicks++; }
                else if (clickWorld) { game.XrCommitWorldPick(worldTarget, worldPoint); CommittedClicks++; }
            }
            wasHeld = held;
        }

        public void ClickForAutomation(Ray ray)
        {
            Process(ray, true, false); Process(ray, true, true); Process(ray, true, false);
        }
        public void CancelInput()
        {
            if (pressedUi != null && data != null) ExecuteEvents.Execute(pressedUi, data, ExecuteEvents.pointerUpHandler);
            ClearCapture(); SetHover(null);
            releaseRequired = true; wasHeld = false;
            if (line != null) line.enabled = false;
            game?.XrHidePreview();
        }
        private void ClearCapture()
        {
            pressedUi = null; pressedWorld = null; pressedOnUi = false;
            if (data == null) return;
            data.eligibleForClick = false; data.pointerPress = null; data.rawPointerPress = null;
        }
        private void SetHover(GameObject target)
        {
            if (hover == target || data == null) return;
            if (hover != null) ExecuteEvents.ExecuteHierarchy(hover, data, ExecuteEvents.pointerExitHandler);
            hover = target; data.pointerEnter = target;
            if (hover != null) ExecuteEvents.ExecuteHierarchy(hover, data, ExecuteEvents.pointerEnterHandler);
        }
        private bool TryWorldHit(Ray ray, out SkybridgePickable target, out Vector3 point)
        {
            var preferTargets = game.Builder.ActiveTool.HasValue || game.XrIsMovingSelection;
            if (game.World.TryPick(ray, preferTargets, out target, out point) && !IsOccluded(ray, Vector3.Distance(ray.origin, point), target)) return true;
            target = null; point = ray.GetPoint(2f);
            if (!preferTargets) return false;
            foreach (var hit in Physics.SphereCastAll(ray, 0.012f, 4f).OrderBy(item => item.distance))
            {
                var candidate = hit.collider.GetComponentInParent<SkybridgePickable>();
                if (candidate == null || candidate.Kind == PickKind.Piece) continue;
                var aimPoint = hit.collider.bounds.center;
                if (Vector3.Angle(ray.direction, aimPoint - ray.origin) > 0.9f) continue;
                var aimRay = new Ray(ray.origin, aimPoint - ray.origin);
                if (IsOccluded(aimRay, Vector3.Distance(ray.origin, aimPoint), candidate)) continue;
                if (TryUiHit(view.Canvases, camera, aimRay, out _, out _, out var panelDistance) && panelDistance < Vector3.Distance(ray.origin, aimPoint)) continue;
                target = candidate; point = aimPoint;
                return true;
            }
            return false;
        }
        private static bool IsOccluded(Ray ray, float distance, SkybridgePickable intended)
        {
            foreach (var hit in Physics.RaycastAll(ray, Mathf.Max(0f, distance - 0.005f)).OrderBy(item => item.distance))
            {
                var pick = hit.collider.GetComponentInParent<SkybridgePickable>();
                if (pick == intended && intended != null) continue;
                if (pick != null && pick.Kind != PickKind.Piece) continue;
                if (hit.collider.isTrigger) continue;
                return true;
            }
            return false;
        }
        public static bool TryUiHit(IReadOnlyList<Canvas> canvases, Camera camera, Ray ray, out GameObject target, out Vector3 point, out float distance)
        {
            target = null; point = default; distance = float.PositiveInfinity;
            var depth = -1;
            foreach (var canvas in canvases)
            {
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                var graphics = GraphicRegistry.GetGraphicsForCanvas(canvas);
                for (var graphicIndex = 0; graphicIndex < graphics.Count; graphicIndex++)
                {
                    var graphic = graphics[graphicIndex];
                    if (graphic == null || !graphic.isActiveAndEnabled || !graphic.raycastTarget || graphic.canvasRenderer.cull || graphic.color.a < 0.01f) continue;
                    var rect = graphic.rectTransform;
                    var plane = new Plane(rect.forward, rect.position);
                    if (!plane.Raycast(ray, out var d) || d < 0.02f || d > 6f) continue;
                    var p = ray.GetPoint(d);
                    if (!rect.rect.Contains(rect.InverseTransformPoint(p))) continue;
                    if (!graphic.Raycast(camera.WorldToScreenPoint(p), canvas.worldCamera ?? camera)) continue;
                    if (d > distance + 0.003f || Mathf.Abs(d - distance) <= 0.003f && graphic.depth < depth) continue;
                    target = graphic.gameObject; point = p; distance = d; depth = graphic.depth;
                }
            }
            return target != null;
        }
        private void OnDisable() => CancelInput();
        private void OnDestroy() { if (lineMaterial != null) Destroy(lineMaterial); }
    }
}
