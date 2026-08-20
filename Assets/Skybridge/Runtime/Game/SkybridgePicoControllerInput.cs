using UnityEngine;
using UnityEngine.XR;

namespace Skybridge.Game
{
    /// <summary>
    /// Controller-only PICO input bridge. It uses Unity XR feature usages so
    /// no PICO physical key is a gameplay requirement. Hand tracking is not
    /// queried or enabled anywhere in this component.
    /// </summary>
    public sealed class SkybridgePicoControllerInput : MonoBehaviour
    {
        private const float StickThreshold = 0.72f;
        private const float StickRepeatSeconds = 0.26f;

        private SkybridgeGameController game;
        private Camera xrCamera;
        private InputDevice left;
        private InputDevice right;
        private bool rightTriggerWasHeld;
        private bool rightPrimaryWasHeld;
        private bool rightSecondaryWasHeld;
        private bool leftPrimaryWasHeld;
        private bool leftSecondaryWasHeld;
        private float nextStickActionAt;

        public void Initialize(SkybridgeGameController controller, Camera camera)
        {
            game = controller;
            xrCamera = camera;
        }

        private void Update()
        {
            if (game == null || xrCamera == null) return;
            EnsureDevices();
            var ray = GetRightAimRay();
            game.PreviewPicoRay(ray);
            HandleRightTrigger(ray);
            HandleButtons();
            HandleLeftStick();
        }

        private void EnsureDevices()
        {
            if (!left.isValid) left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!right.isValid) right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        }

        private Ray GetRightAimRay()
        {
            if (right.isValid &&
                right.TryGetFeatureValue(CommonUsages.devicePosition, out var position) &&
                right.TryGetFeatureValue(CommonUsages.deviceRotation, out var rotation))
            {
                var worldPosition = transform.TransformPoint(position);
                var worldRotation = transform.rotation * rotation;
                return new Ray(worldPosition, worldRotation * Vector3.forward);
            }
            return new Ray(xrCamera.transform.position, xrCamera.transform.forward);
        }

        private void HandleRightTrigger(Ray ray)
        {
            var held = ReadButton(right, CommonUsages.triggerButton);
            if (held && !rightTriggerWasHeld) game.HandlePicoRay(ray);
            rightTriggerWasHeld = held;
        }

        private void HandleButtons()
        {
            HandleEdge(right, CommonUsages.primaryButton, ref rightPrimaryWasHeld, game.PicoToggleTest);
            HandleEdge(right, CommonUsages.secondaryButton, ref rightSecondaryWasHeld, game.PicoCancel);
            HandleEdge(left, CommonUsages.primaryButton, ref leftPrimaryWasHeld, game.PicoUndo);
            HandleEdge(left, CommonUsages.secondaryButton, ref leftSecondaryWasHeld, game.PicoRedo);
        }

        private void HandleLeftStick()
        {
            if (!left.isValid || Time.unscaledTime < nextStickActionAt) return;
            if (!left.TryGetFeatureValue(CommonUsages.primary2DAxis, out var stick)) return;
            if (Mathf.Abs(stick.x) >= StickThreshold)
            {
                game.PicoRotate(stick.x > 0f ? 1 : -1);
                nextStickActionAt = Time.unscaledTime + StickRepeatSeconds;
            }
            else if (Mathf.Abs(stick.y) >= StickThreshold)
            {
                game.PicoChangeLayer(stick.y > 0f ? 1 : -1);
                nextStickActionAt = Time.unscaledTime + StickRepeatSeconds;
            }
        }

        private static bool ReadButton(InputDevice device, InputFeatureUsage<bool> usage)
        {
            return device.isValid && device.TryGetFeatureValue(usage, out var held) && held;
        }

        private static void HandleEdge(InputDevice device, InputFeatureUsage<bool> usage, ref bool wasHeld, System.Action action)
        {
            var held = ReadButton(device, usage);
            if (held && !wasHeld) action?.Invoke();
            wasHeld = held;
        }
    }
}
