using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Skybridge.Presentation
{
    [RequireComponent(typeof(Camera))]
    public sealed class OrbitCameraController : MonoBehaviour
    {
        private Camera controlledCamera;
        private Vector3 target;
        private Vector3 desiredTarget;
        private float yaw;
        private float pitch;
        private float distance = 24f;
        private float desiredDistance = 24f;
        private float maximumDistance = 48f;
        private Vector3 orbitDirection = new Vector3(15f, 11f, 17f).normalized;
        private Bounds focusBounds;
        private Vector3 focusStartAnchor;
        private Vector3 focusGoalAnchor;
        private bool hasFocusContext;
        private int lastScreenWidth;
        private int lastScreenHeight;
        private float lastAspect;
        private bool reducedMotion;
        private bool inputEnabled = true;
        private bool rightDragActive;
        private bool middleDragActive;
        private bool manualViewActive;

        public bool AutoFocus { get; private set; } = true;
        public event System.Action ViewAdjusted;

        public void Initialize(bool reduceMotion, bool autoFocus)
        {
            controlledCamera = GetComponent<Camera>();
            controlledCamera.nearClipPlane = 0.1f;
            controlledCamera.farClipPlane = 150f;
            reducedMotion = reduceMotion;
            AutoFocus = autoFocus;
            // Match the Three.js camera before the first automatic focus.  It
            // is observable when automatic focus is disabled on first launch.
            target = desiredTarget = Vector3.zero;
            var initialPosition = new Vector3(15f, 11f, 17f);
            SetOrbitFromPosition(initialPosition, target);
            ApplyTransform(true);
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            lastAspect = controlledCamera.aspect;
        }

        public void SetInputEnabled(bool enabled)
        {
            inputEnabled = enabled;
            if (!enabled) ClearPointerTracking();
        }

        public void SetReducedMotion(bool enabled)
        {
            reducedMotion = enabled;
            if (!enabled) return;
            target = desiredTarget;
            distance = desiredDistance;
            ApplyTransform(true);
        }

        public void SetAutoFocus(bool enabled, Bounds bounds)
        {
            AutoFocus = enabled;
            if (enabled) manualViewActive = false;
            if (enabled) Focus(bounds);
            else CancelFocusMotion();
        }

        public void SetAutoFocus(bool enabled, Bounds bounds, Vector3 startAnchor, Vector3 goalAnchor)
        {
            AutoFocus = enabled;
            if (enabled) manualViewActive = false;
            if (enabled) Focus(bounds, startAnchor, goalAnchor);
            else CancelFocusMotion();
        }

        public void Focus(Bounds bounds, Vector3 startAnchor, Vector3 goalAnchor, bool immediate = false)
        {
            if (manualViewActive && !immediate) return;
            if (controlledCamera == null) controlledCamera = GetComponent<Camera>();
            focusBounds = bounds;
            focusStartAnchor = startAnchor;
            focusGoalAnchor = goalAnchor;
            hasFocusContext = true;
            var portrait = controlledCamera != null && controlledCamera.aspect < 0.9f;
            desiredTarget = bounds.center;
            desiredTarget.y += portrait ? -bounds.size.y * 0.18f : Mathf.Max(0.1f, bounds.size.y * 0.08f);
            if (portrait) desiredTarget.x -= bounds.size.x * 0.04f;
            var verticalFov = controlledCamera != null ? controlledCamera.fieldOfView * Mathf.Deg2Rad : SkybridgeWorldView.SceneFieldOfView * Mathf.Deg2Rad;
            var aspect = controlledCamera != null ? controlledCamera.aspect : 16f / 9f;
            var horizontalFov = 2f * Mathf.Atan(Mathf.Tan(verticalFov * 0.5f) * aspect);
            var widthDistance = bounds.size.x * 0.5f / Mathf.Max(0.1f, Mathf.Tan(horizontalFov * 0.5f));
            var heightDistance = bounds.size.y * 0.5f / Mathf.Max(0.1f, Mathf.Tan(verticalFov * 0.5f));
            desiredDistance = Mathf.Max(widthDistance, heightDistance, bounds.size.z * 1.3f, 7.5f) * (portrait ? 1.45f : 1.38f);
            maximumDistance = Mathf.Max(48f, desiredDistance * 1.2f);
            // Three.js expands its fog envelope whenever focus distance grows.
            // Keep the authored 34/78 baseline for compact levels and extend
            // the far plane for the large Level 5 volume instead of letting
            // the distant goal island fade too early.
            RenderSettings.fogStartDistance = Mathf.Max(SkybridgeWorldView.SceneFogNear, desiredDistance * 0.62f);
            RenderSettings.fogEndDistance = Mathf.Max(SkybridgeWorldView.SceneFogFar, desiredDistance * 1.7f);
            if (controlledCamera != null)
            {
                controlledCamera.nearClipPlane = 0.1f;
                controlledCamera.farClipPlane = Mathf.Max(150f, desiredDistance * 4f);
                lastAspect = controlledCamera.aspect;
            }
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;

            var anchorDirection = goalAnchor - startAnchor;
            anchorDirection.y = 0f;
            var spatialDiagonal = Mathf.Abs(anchorDirection.x) > 0.01f && Mathf.Abs(anchorDirection.z) > 0.01f;
            Vector3 viewDirection;
            if (spatialDiagonal)
            {
                var along = anchorDirection.normalized;
                var perpendicular = new Vector3(-along.z, 0f, along.x);
                viewDirection = (
                    perpendicular * (portrait ? 0.5f : 0.76f) -
                    along * 0.2f +
                    Vector3.up * (portrait ? 0.82f : 0.62f)).normalized;
            }
            else
            {
                viewDirection = (portrait
                    ? new Vector3(0.72f, 0.72f, 0.25f)
                    : new Vector3(0.68f, 0.58f, 0.82f)).normalized;
            }
            SetOrbitDirection(viewDirection);
            if (immediate || reducedMotion)
            {
                target = desiredTarget;
                distance = desiredDistance;
                ApplyTransform(true);
            }
        }

        public void Focus(Bounds bounds, bool immediate = false)
        {
            Focus(bounds, Vector3.zero, Vector3.right, immediate);
        }

        public void ResetTo(Bounds bounds, Vector3 startAnchor, Vector3 goalAnchor)
        {
            manualViewActive = false;
            Focus(bounds, startAnchor, goalAnchor, true);
        }

        private void Update()
        {
            if (controlledCamera == null) controlledCamera = GetComponent<Camera>();
            HandleResizeFocus();
            HandleInput();
            var sharpness = reducedMotion ? 100f : 8f;
            var factor = 1f - Mathf.Exp(-sharpness * Time.unscaledDeltaTime);
            target = Vector3.Lerp(target, desiredTarget, factor);
            distance = Mathf.Lerp(distance, desiredDistance, factor);
            ApplyTransform(false);
        }

        private void HandleResizeFocus()
        {
            if (controlledCamera == null) return;
            var width = Screen.width;
            var height = Screen.height;
            var aspect = controlledCamera.aspect;
            var dimensionsChanged = width > 0 && height > 0 &&
                (width != lastScreenWidth || height != lastScreenHeight);
            var aspectChanged = Mathf.Abs(aspect - lastAspect) > 0.001f;
            if (!dimensionsChanged && !aspectChanged) return;

            lastScreenWidth = width;
            lastScreenHeight = height;
            lastAspect = aspect;
            if (AutoFocus && hasFocusContext && !manualViewActive)
            {
                // Three.js recomputes focusAll(0) on every resize while
                // autofocus is enabled, so the framing follows portrait /
                // landscape changes immediately rather than retaining the
                // previous distance and target.
                Focus(focusBounds, focusStartAnchor, focusGoalAnchor, true);
            }
        }

        private void HandleInput()
        {
            var mouse = Mouse.current;
            if (!inputEnabled || mouse == null)
            {
                ClearPointerTracking();
                return;
            }
            var overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (mouse.rightButton.wasPressedThisFrame) rightDragActive = !overUi;
            if (mouse.middleButton.wasPressedThisFrame) middleDragActive = !overUi;
            var delta = mouse.delta.ReadValue();
            if (rightDragActive && mouse.rightButton.isPressed)
            {
                yaw += delta.x * 0.18f;
                pitch = Mathf.Clamp(pitch - delta.y * 0.14f, -85f, 85f);
                if (delta.sqrMagnitude > 0.01f) { manualViewActive = true; ViewAdjusted?.Invoke(); }
            }
            if (middleDragActive && mouse.middleButton.isPressed)
            {
                var scale = distance * 0.0017f;
                desiredTarget -= transform.right * delta.x * scale;
                desiredTarget -= transform.up * delta.y * scale;
                if (delta.sqrMagnitude > 0.01f) { manualViewActive = true; ViewAdjusted?.Invoke(); }
            }
            if (mouse.rightButton.wasReleasedThisFrame) rightDragActive = false;
            if (mouse.middleButton.wasReleasedThisFrame) middleDragActive = false;
            var scroll = mouse.scroll.ReadValue().y;
            if (!overUi && Mathf.Abs(scroll) > 0.01f)
            {
                desiredDistance = Mathf.Clamp(desiredDistance * Mathf.Exp(-scroll * 0.0011f), 3f, maximumDistance);
                manualViewActive = true;
                ViewAdjusted?.Invoke();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) ClearPointerTracking();
        }

        private void ClearPointerTracking()
        {
            rightDragActive = false;
            middleDragActive = false;
        }

        private void CancelFocusMotion()
        {
            desiredTarget = target;
            desiredDistance = distance;
        }

        private void SetOrbitDirection(Vector3 viewDirection)
        {
            var normalized = viewDirection.sqrMagnitude > 0.0001f ? viewDirection.normalized : Vector3.forward;
            pitch = Mathf.Asin(Mathf.Clamp(normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            yaw = Mathf.Atan2(normalized.x, normalized.z) * Mathf.Rad2Deg;
            orbitDirection = normalized;
        }

        private void SetOrbitFromPosition(Vector3 position, Vector3 orbitTarget)
        {
            var offset = position - orbitTarget;
            distance = desiredDistance = Mathf.Max(0.001f, offset.magnitude);
            SetOrbitDirection(offset / distance);
        }

        private void ApplyTransform(bool force)
        {
            orbitDirection = SphericalDirection(yaw, pitch);
            var position = target + orbitDirection * distance;
            var rotation = Quaternion.LookRotation(-orbitDirection, Vector3.up);
            if (force)
            {
                transform.SetPositionAndRotation(position, rotation);
                return;
            }
            transform.position = position;
            transform.rotation = rotation;
        }

        private static Vector3 SphericalDirection(float azimuthDegrees, float elevationDegrees)
        {
            var azimuth = azimuthDegrees * Mathf.Deg2Rad;
            var elevation = elevationDegrees * Mathf.Deg2Rad;
            var cosElevation = Mathf.Cos(elevation);
            return new Vector3(
                Mathf.Sin(azimuth) * cosElevation,
                Mathf.Sin(elevation),
                Mathf.Cos(azimuth) * cosElevation).normalized;
        }
    }
}
