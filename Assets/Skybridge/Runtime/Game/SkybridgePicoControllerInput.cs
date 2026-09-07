using UnityEngine;
using UnityEngine.XR;

namespace Skybridge.Game
{
    /// <summary>Controller pose and semantic actions, with screen-pointer routing on Android emulator hardware.</summary>
    public sealed class SkybridgePicoControllerInput : MonoBehaviour
    {
        private SkybridgeGameController game;
        private InputDevice left, right, head;
        private bool rightPrimaryHeld, rightSecondaryHeld, leftPrimaryHeld, leftSecondaryHeld, previouslyTracked;
        private float nextStickAt;
        private int simulatorDiagnosticCount;
        private float nextSimulatorDiagnosticAt;
        private bool simulatorTriggerHeld;
        public void Initialize(SkybridgeGameController controller, Camera camera) => game = controller;
        private void Update()
        {
            if (game == null || !game.IsInitialized || game.XrPointer == null) return;
            if (!left.isValid) left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!right.isValid) right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!head.isValid) head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            var available = TryRightRay(out var ray);
            // Emulator hardware has no wear sensor and reports userPresence=false even
            // while its virtual controller is tracked and its game window is active.
            if (!game.XrUsesSimulatorPointer && head.isValid && head.TryGetFeatureValue(CommonUsages.userPresence, out var wearing) && !wearing) available = false;
            var triggerHeld = Read(right, CommonUsages.triggerButton) ||
                              right.TryGetFeatureValue(CommonUsages.trigger, out var triggerValue) && triggerValue >= 0.5f;
            // The PICO emulator can map its host mouse to a virtual controller.
            // Keep that native ray active; use Android screen input when no controller is tracked.
            if (game.XrUsesSimulatorPointer)
            {
                if (simulatorDiagnosticCount < 12 && Time.unscaledTime >= nextSimulatorDiagnosticAt)
                {
                    right.TryGetFeatureValue(CommonUsages.isTracked, out var tracked);
                    right.TryGetFeatureValue(CommonUsages.trackingState, out var state);
                    var mouse = UnityEngine.InputSystem.Mouse.current;
                    Debug.Log($"SKYBRIDGE_SIMULATOR_INPUT: controller={right.name}; valid={right.isValid}; tracked={tracked}; state={state}; available={available}; trigger={triggerHeld}; mouse={(mouse == null ? "none" : mouse.position.ReadValue().ToString())}; touch={UnityEngine.InputSystem.Touchscreen.current != null}; rayOrigin={ray.origin}; rayDirection={ray.direction}; camera={game.XrCamera.transform.position}");
                    simulatorDiagnosticCount++;
                    nextSimulatorDiagnosticAt = Time.unscaledTime + 3f;
                }
                if (!available)
                {
                    game.ProcessXrSimulatorPointer();
                    previouslyTracked = false;
                    return;
                }
            }
            game.ProcessXrInput(ray, available, triggerHeld);
            if (game.XrUsesSimulatorPointer && triggerHeld != simulatorTriggerHeld)
            {
                Debug.Log($"SKYBRIDGE_SIMULATOR_CLICK: held={triggerHeld}; hover={game.XrPointer.HoverTarget?.name}; committed={game.XrPointer.CommittedClicks}");
                simulatorTriggerHeld = triggerHeld;
            }
            var rp = Read(right, CommonUsages.primaryButton);
            var rs = Read(right, CommonUsages.secondaryButton);
            var lp = Read(left, CommonUsages.primaryButton);
            var ls = Read(left, CommonUsages.secondaryButton);
            if (available && previouslyTracked && game.XrPointer.IsArmed)
            {
                if (rp && !rightPrimaryHeld) game.PicoPrimaryAction();
                if (rs && !rightSecondaryHeld) game.PicoSecondaryAction();
                if (!game.XrInteractionBlocked)
                {
                    left.TryGetFeatureValue(CommonUsages.primary2DAxis, out var viewStick);
                    right.TryGetFeatureValue(CommonUsages.primary2DAxis, out var zoomStick);
                    var yaw = Mathf.Abs(viewStick.x) > 0.18f ? viewStick.x : 0;
                    var pitch = Mathf.Abs(viewStick.y) > 0.18f ? viewStick.y : 0;
                    var zoom = Mathf.Abs(zoomStick.y) > 0.18f ? zoomStick.y : 0;
                    if (Read(left, CommonUsages.gripButton))
                    {
                        game.XrPanView(new Vector2(yaw, pitch) * (0.4f * Time.unscaledDeltaTime));
                        game.XrAdjustView(0, 0, zoom * 0.7f * Time.unscaledDeltaTime);
                    }
                    else game.XrAdjustView(yaw * 60f * Time.unscaledDeltaTime, pitch * 45f * Time.unscaledDeltaTime, zoom * 0.7f * Time.unscaledDeltaTime);
                }
                if (game.XrWorkbench != null && game.XrWorkbench.ShortcutsEnabled && !game.XrInteractionBlocked)
                {
                    if (lp && !leftPrimaryHeld) game.XrDispatch("undo");
                    if (ls && !leftSecondaryHeld) game.XrDispatch("redo");
                    if (left.isValid && Time.unscaledTime >= nextStickAt && left.TryGetFeatureValue(CommonUsages.primary2DAxis, out var stick))
                    {
                        if (game.CurrentLevel.BuildVolume != null && Mathf.Abs(stick.x) >= 0.75f)
                        {
                            game.XrDispatch("rotate", stick.x > 0 ? 1 : -1);
                            nextStickAt = Time.unscaledTime + 0.35f;
                        }
                        else if (game.CurrentLevel.BuildVolume != null && Mathf.Abs(stick.y) >= 0.75f)
                        {
                            game.XrDispatch("layer", stick.y > 0 ? 1 : -1);
                            nextStickAt = Time.unscaledTime + 0.35f;
                        }
                    }
                }
            }
            rightPrimaryHeld = rp; rightSecondaryHeld = rs;
            leftPrimaryHeld = lp; leftSecondaryHeld = ls;
            previouslyTracked = available;
        }
        private bool TryRightRay(out Ray ray)
        {
            ray = default;
            if (!right.isValid || !right.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) || !tracked ||
                !right.TryGetFeatureValue(CommonUsages.devicePosition, out var position) ||
                !right.TryGetFeatureValue(CommonUsages.deviceRotation, out var rotation)) return false;
            if (right.TryGetFeatureValue(CommonUsages.trackingState, out var state) &&
                (state & (InputTrackingState.Position | InputTrackingState.Rotation)) != (InputTrackingState.Position | InputTrackingState.Rotation)) return false;
            var offset = transform.Find("Camera Offset");
            var space = offset != null ? offset : transform;
            ray = CreatePointerRay(space, position, rotation);
            return true;
        }
        private Ray CreatePointerRay(Transform space, Vector3 position, Quaternion rotation)
        {
            // The emulator aims its virtual controller from the mouse in the eye view,
            // but leaves devicePosition at the standing controller pose when the view moves.
            // Use the viewing origin for that screen-driven ray to avoid height/parallax drift.
            var origin = game.XrUsesSimulatorPointer ? game.XrCamera.transform.position : space.TransformPoint(position);
            return new Ray(origin, space.rotation * rotation * Vector3.forward);
        }
        private static bool Read(InputDevice device, InputFeatureUsage<bool> usage) => device.isValid && device.TryGetFeatureValue(usage, out var value) && value;
        private void OnDisable() { previouslyTracked = false; game?.XrPointer?.CancelInput(); }
    }
}
