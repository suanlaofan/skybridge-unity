using System.Reflection;
using NUnit.Framework;
using Skybridge.Presentation;
using UnityEngine;

namespace Skybridge.Tests
{
    public sealed class OrbitCameraParityTests
    {
        [Test]
        public void FocusUsesThreeJsDiagonalDirectionAndClipEnvelope()
        {
            var cameraObject = new GameObject("CameraParityTest");
            var camera = cameraObject.AddComponent<Camera>();
            camera.aspect = 16f / 9f;
            camera.fieldOfView = SkybridgeWorldView.SceneFieldOfView;
            var orbit = cameraObject.AddComponent<OrbitCameraController>();
            orbit.Initialize(false, true);

            var bounds = new Bounds(Vector3.zero, new Vector3(24f, 5f, 24f));
            var start = new Vector3(-12f, 1f, -12f);
            var goal = new Vector3(12f, 11f, 12f);
            orbit.Focus(bounds, start, goal, true);

            var expected = ThreeDiagonalDirection(start, goal, false);
            var actual = (camera.transform.position - bounds.center).normalized;
            Assert.That(Vector3.Dot(actual, expected), Is.GreaterThan(0.999f));
            Assert.That(camera.nearClipPlane, Is.EqualTo(0.1f).Within(0.0001f));
            var desiredDistanceField = typeof(OrbitCameraController).GetField("desiredDistance", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(desiredDistanceField, Is.Not.Null);
            var desiredDistance = (float)desiredDistanceField.GetValue(orbit);
            Assert.That(camera.farClipPlane, Is.EqualTo(Mathf.Max(150f, desiredDistance * 4f)).Within(0.01f));

            Object.DestroyImmediate(cameraObject);
        }

        [Test]
        public void FocusUsesThreeJsPortraitOffsetsAndDirection()
        {
            var cameraObject = new GameObject("PortraitCameraParityTest");
            var camera = cameraObject.AddComponent<Camera>();
            camera.aspect = 0.75f;
            camera.fieldOfView = SkybridgeWorldView.SceneFieldOfView;
            var orbit = cameraObject.AddComponent<OrbitCameraController>();
            orbit.Initialize(false, true);

            var bounds = new Bounds(new Vector3(3f, 4f, 0f), new Vector3(10f, 6f, 8f));
            var start = new Vector3(0f, 0f, 0f);
            var goal = new Vector3(10f, 0f, 0f);
            orbit.Focus(bounds, start, goal, true);

            var expectedTarget = bounds.center + Vector3.up * (-bounds.size.y * 0.18f) - Vector3.right * (bounds.size.x * 0.04f);
            var targetField = typeof(OrbitCameraController).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(targetField, Is.Not.Null);
            var actualTarget = (Vector3)targetField.GetValue(orbit);
            Assert.That(Vector3.Distance(actualTarget, expectedTarget), Is.LessThan(0.0001f));

            Object.DestroyImmediate(cameraObject);
        }

        private static Vector3 ThreeDiagonalDirection(Vector3 start, Vector3 goal, bool portrait)
        {
            var anchor = goal - start;
            anchor.y = 0f;
            var along = anchor.normalized;
            var perpendicular = new Vector3(-along.z, 0f, along.x);
            return (perpendicular * (portrait ? 0.5f : 0.76f) - along * 0.2f + Vector3.up * (portrait ? 0.82f : 0.62f)).normalized;
        }
    }
}
