using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Skybridge.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skybridge.Presentation
{
    public enum BuildTargetViewKind
    {
        Road,
        Foundation,
        Truss,
    }

    public sealed class BuildTargetViewData
    {
        public string Id = string.Empty;
        public BuildTargetViewKind Kind;
        public int Index = -1;
        public string RoadPieceId = string.Empty;
        public GridPoint From;
        public GridPoint To;
        public GridPoint Point;
    }

    public sealed class SkybridgeWorldView : MonoBehaviour
    {
        public const float GridUnit = 2.4f;
        public const float HeightUnit = 1.1f;
        public const float RoadBaseY = 1.55f;
        public const float RoadWidth = 1.5f;
        public const float TableTopY = -3.05f;
        public const float VehicleClearance = 0.04f;
        public const float SceneFieldOfView = 38f;
        public const float SceneFogNear = 34f;
        public const float SceneFogFar = 78f;

        private const string IslandModelResource =
            "SkybridgeModels/Island/tripo_convert_a3c039d2-83f0-441c-a722-098cbe09a69e";
        private const string IslandTextureResource =
            "SkybridgeModels/Island/圆形能量平台3d模型_basecolor";
        private const string RescueVehicleModelResource =
            "SkybridgeModels/RescueVehicle/tripo_convert_72313d7f-4ed4-492b-a48d-6aa3ddb5ad59";
        private const string RescueVehicleTextureResource =
            "SkybridgeModels/RescueVehicle/三维模型_探测车_basecolor";
        private const string GoalGateModelResource =
            "SkybridgeModels/GoalGate/tripo_convert_02717a75-bd4c-451a-a5d2-ba9a7d107b7e";
        private const string GoalGateTextureResource =
            "SkybridgeModels/GoalGate/科幻拱门3d模型_basecolor";

        private readonly Dictionary<string, GameObject> pieceObjects = new();
        private readonly Dictionary<string, string> pieceSignatures = new();
        private readonly Dictionary<int, GameObject> routeJoints = new();
        private readonly List<Material> materials = new();
        private readonly List<Vector3> vehiclePath = new();
        private readonly List<float> vehiclePathDistances = new();
        // Keep the logical grid route separate from the sampled world-space
        // waypoints.  The Three.js scene keeps the route that was validated
        // when a test is stopped or restored; rebuilding the visual hierarchy
        // must not silently fall back to the authored Level 5 diagonal route.
        private readonly List<GridPoint> vehicleRouteNodes = new();
        private readonly List<Transform> vehicleWheels = new();
        private readonly List<IslandOcclusionState> islandOcclusionStates = new();

        private Transform environmentRoot;
        private Transform levelRoot;
        private Transform piecesRoot;
        private Transform targetsRoot;
        private Transform effectsRoot;
        private Transform ghostRoot;
        private GameObject vehicle;
        private GameObject selectionFrame;
        private Coroutine vehicleRoutine;
        private LevelDefinition level;
        private bool reducedMotion;
        private float vehiclePathLength;
        private Vector3 goalDirection = Vector3.right;
        private Bounds vehicleFailureZone;
        private Bounds goalZone;

        private Material environmentMaterial;
        private Material environmentShadeMaterial;
        private Material roadMaterial;
        private Material roadEdgeMaterial;
        private Material trussMaterial;
        private Material accentMaterial;
        private Material mechanicalMaterial;
        private Material warningMaterial;
        private Material rockMaterial;
        private Material voidMaterial;
        private Material glassMaterial;
        private Material targetMaterial;
        private Material ghostValidMaterial;
        private Material ghostInvalidMaterial;
        private Material islandModelMaterial;
        private Material rescueVehicleModelMaterial;
        private Material goalGateModelMaterial;
        private bool islandModelReady;
        private bool rescueVehicleModelReady;
        private bool goalGateModelReady;
        private SkybridgeSceneModelLibrary sceneModelLibrary;

        public Camera WorldCamera { get; private set; }
        public float VehicleProgress { get; private set; }
        public bool IsRunning => vehicleRoutine != null;
        public float MinimumBreakVehicleY { get; private set; } = float.PositiveInfinity;

        /// <summary>
        /// Returns a defensive copy of the currently configured logical route.
        /// This is used by the game controller when it has to rebuild visual
        /// objects after a failure, so arbitrary Level 5 turns survive restore.
        /// </summary>
        public IReadOnlyList<GridPoint> GetVehicleGridPath()
        {
            return vehicleRouteNodes.ToList();
        }

        public bool VehicleRouteShowsJoints { get; private set; }

        public bool ImportedModelAssetsReady => islandModelReady && rescueVehicleModelReady && goalGateModelReady;

        public bool HasImportedModel(string endpointName)
        {
            var endpoint = levelRoot == null ? null : levelRoot.Find(endpointName);
            return endpoint != null && endpoint.Find("ImportedModel") != null &&
                   endpoint.Find("ImportedModel").GetComponentsInChildren<Renderer>(true).Length > 0;
        }

        public Transform GetImportedModel(string endpointName)
        {
            var endpoint = levelRoot == null ? null : levelRoot.Find(endpointName);
            return endpoint == null ? null : endpoint.Find("ImportedModel");
        }

        public Bounds GetImportedModelBounds(string endpointName)
        {
            var imported = GetImportedModel(endpointName);
            return imported == null
                ? new Bounds(Vector3.zero, Vector3.zero)
                : CalculateBounds(imported.gameObject);
        }

        public void Initialize(Camera worldCamera, SkybridgeSceneModelLibrary modelLibrary = null)
        {
            WorldCamera = worldCamera;
            sceneModelLibrary = modelLibrary;
            BuildRoots();
            BuildMaterials();
            ConfigureRenderEnvironment();
            ConfigureLights();
        }

        public void SetReducedMotion(bool enabled)
        {
            reducedMotion = enabled;
        }

        public void SetXrPresentation(bool enabled)
        {
            // The oversized desktop tabletop crosses the fixed XR workbench.
            // Islands retain their own platforms; the decorative table has no game-rule role.
            if (environmentRoot != null) environmentRoot.gameObject.SetActive(!enabled);
        }

        public void LoadLevel(LevelDefinition definition)
        {
            level = definition ?? throw new ArgumentNullException(nameof(definition));
            StopSimulation();
            ClearIslandOcclusionStates();
            HideGhost();
            ClearSelection();
            pieceObjects.Clear();
            pieceSignatures.Clear();
            routeJoints.Clear();
            vehicleWheels.Clear();
            ProceduralFactory.DestroyChildren(environmentRoot);
            ProceduralFactory.DestroyChildren(levelRoot);
            ProceduralFactory.DestroyChildren(piecesRoot);
            ProceduralFactory.DestroyChildren(targetsRoot);
            ProceduralFactory.DestroyChildren(effectsRoot);
            CreateEnvironment();
            CreateLevelGeometry();
            RegisterIslandOcclusionStates();
            // The open Level 5 volume starts without route joints in Three.js;
            // joints are shown again once a test path is configured.
            SetVehicleGridPath(CreateDefaultGridPath(), level.BuildVolume == null);
            SetVehicleProgress(0f);
        }

        public void SetLayout(BridgeLayout layout)
        {
            var desiredIds = new HashSet<string>();
            foreach (var road in layout.Roads)
            {
                desiredIds.Add(road.Id);
                UpsertPiece(
                    road.Id,
                    $"{road.Type}:{road.From.Key}:{road.To.Key}",
                    road.Type,
                    () => CreateRoadModel(road.From, road.To, road.Type, false));
            }
            foreach (var pier in layout.Piers)
            {
                desiredIds.Add(pier.Id);
                UpsertPiece(
                    pier.Id,
                    $"{pier.Type}:{pier.Top.Key}:{pier.Foundation.Key}",
                    PieceType.Pier,
                    () => CreatePierModel(pier.Top, false));
            }
            foreach (var truss in layout.Trusses)
            {
                var road = layout.Roads.FirstOrDefault(candidate => candidate.Id == truss.RoadPieceId);
                if (road == null) continue;
                desiredIds.Add(truss.Id);
                UpsertPiece(
                    truss.Id,
                    $"{truss.Type}:{truss.RoadPieceId}:{road.From.Key}:{road.To.Key}",
                    PieceType.Truss,
                    () => CreateTrussModel(road.From, road.To, false));
            }

            foreach (var id in pieceObjects.Keys.Where(id => !desiredIds.Contains(id)).ToList())
            {
                var piece = pieceObjects[id];
                pieceObjects.Remove(id);
                pieceSignatures.Remove(id);
                if (piece != null)
                {
                    piece.SetActive(false);
                    Destroy(piece);
                }
            }
        }

        private void UpsertPiece(string id, string signature, PieceType type, Func<GameObject> factory)
        {
            if (pieceObjects.TryGetValue(id, out var existing) && existing != null &&
                pieceSignatures.TryGetValue(id, out var currentSignature) && currentSignature == signature)
            {
                return;
            }
            if (existing != null)
            {
                existing.SetActive(false);
                Destroy(existing);
            }
            var piece = factory();
            piece.name = $"Piece:{id}";
            RegisterPiece(piece, id, type);
            pieceSignatures[id] = signature;
        }

        public void SetBuildTargets(IEnumerable<BuildTargetViewData> targetData)
        {
            ProceduralFactory.DestroyChildren(targetsRoot);
            foreach (var data in targetData)
            {
                var target = new GameObject($"BuildTarget:{data.Id}");
                target.transform.SetParent(targetsRoot, false);
                var pickable = target.AddComponent<SkybridgePickable>();
                pickable.Id = data.Id;
                pickable.Index = data.Index;
                switch (data.Kind)
                {
                    case BuildTargetViewKind.Foundation:
                        CreateFoundationTarget(target, data.Point);
                        pickable.Kind = PickKind.FoundationTarget;
                        break;
                    case BuildTargetViewKind.Truss:
                        CreateRoadTarget(target, data.From, data.To, true);
                        pickable.Kind = PickKind.TrussTarget;
                        pickable.Id = data.RoadPieceId;
                        break;
                    default:
                        CreateRoadTarget(target, data.From, data.To, false);
                        pickable.Kind = PickKind.RoadTarget;
                        break;
                }
            }
        }

        public void ShowRoadGhost(PieceType type, GridPoint from, GridPoint to, bool valid)
        {
            HideGhost();
            var ghost = type == PieceType.Truss
                ? CreateTrussModel(from, to, true)
                : CreateRoadModel(from, to, type, true);
            ghost.name = "PlacementGhost";
            ghost.transform.SetParent(ghostRoot, false);
            ApplyGhostMaterial(ghost, valid);
        }

        public void ShowPierGhost(GridPoint top, bool valid)
        {
            HideGhost();
            var ghost = CreatePierModel(top, true);
            ghost.name = "PlacementGhost";
            ghost.transform.SetParent(ghostRoot, false);
            ApplyGhostMaterial(ghost, valid);
        }

        public void HideGhost()
        {
            if (ghostRoot != null) ProceduralFactory.DestroyChildren(ghostRoot);
        }

        public bool TryPick(Vector2 screenPosition, out SkybridgePickable pickable, out Vector3 point)
        {
            return TryPick(screenPosition, false, out pickable, out point);
        }

        public bool TryPick(Vector2 screenPosition, bool preferBuildTargets, out SkybridgePickable pickable, out Vector3 point)
        {
            pickable = null;
            point = Vector3.zero;
            if (WorldCamera == null) return false;
            var ray = WorldCamera.ScreenPointToRay(screenPosition);
            var hits = Physics.RaycastAll(ray, 500f).OrderBy(hit => hit.distance).ToList();

            if (preferBuildTargets && TryFindSnappedTarget(screenPosition, hits, out pickable, out point)) return true;

            // When no placement tool is active, the reference scene treats a
            // click on an existing component as a selection gesture.  Unity's
            // transparent build-target frames also have colliders, so simply
            // returning the first pickable hit can let an empty grid cell
            // swallow the component underneath it.  Resolve piece hits first
            // in the idle/selection state; target frames remain available as
            // the placement snap surface whenever a tool is active.
            if (!preferBuildTargets)
            {
                foreach (var hit in hits)
                {
                    var candidate = hit.collider.GetComponentInParent<SkybridgePickable>();
                    if (candidate == null || candidate.Kind != PickKind.Piece) continue;
                    pickable = candidate;
                    point = hit.point;
                    return true;
                }

                return false;
            }

            foreach (var hit in hits)
            {
                var candidate = hit.collider.GetComponentInParent<SkybridgePickable>();
                if (candidate == null) continue;
                pickable = candidate;
                point = hit.point;
                return true;
            }
            return false;
        }

        /// <summary>
        /// XR equivalent of screen picking. It deliberately avoids desktop
        /// pixel snapping: controller rays already supply a spatial hit point.
        /// </summary>
        public bool TryPick(Ray ray, bool preferBuildTargets, out SkybridgePickable pickable, out Vector3 point)
        {
            pickable = null;
            point = Vector3.zero;
            var hits = Physics.RaycastAll(ray, 500f).OrderBy(hit => hit.distance).ToList();
            if (!preferBuildTargets)
            {
                foreach (var hit in hits)
                {
                    var candidate = hit.collider.GetComponentInParent<SkybridgePickable>();
                    if (candidate == null || candidate.Kind != PickKind.Piece) continue;
                    pickable = candidate;
                    point = hit.point;
                    return true;
                }
                return false;
            }

            foreach (var hit in hits)
            {
                var candidate = hit.collider.GetComponentInParent<SkybridgePickable>();
                if (candidate == null) continue;
                pickable = candidate;
                point = hit.point;
                return true;
            }
            return false;
        }

        private bool TryFindSnappedTarget(
            Vector2 screenPosition,
            IReadOnlyList<RaycastHit> hits,
            out SkybridgePickable pickable,
            out Vector3 point)
        {
            pickable = null;
            point = Vector3.zero;
            var bestDistance = float.PositiveInfinity;
            var snapRadius = CalculateTargetSnapRadius(WorldCamera.pixelWidth);
            foreach (var candidate in targetsRoot.GetComponentsInChildren<SkybridgePickable>(false))
            {
                if (candidate == null || candidate.Kind == PickKind.Piece) continue;
                var bounds = CalculateBounds(candidate.gameObject);
                var worldPoint = bounds.center;
                var projected = WorldCamera.WorldToScreenPoint(worldPoint);
                if (projected.z <= 0f) continue;
                var distance = Vector2.Distance(screenPosition, new Vector2(projected.x, projected.y));
                if (distance > snapRadius || distance >= bestDistance) continue;
                bestDistance = distance;
                pickable = candidate;
                point = worldPoint;
            }
            if (pickable != null) return true;

            foreach (var hit in hits)
            {
                var candidate = hit.collider.GetComponentInParent<SkybridgePickable>();
                if (candidate == null || candidate.Kind == PickKind.Piece) continue;
                pickable = candidate;
                point = hit.point;
                return true;
            }
            return false;
        }

        public static float CalculateTargetSnapRadius(float viewportWidth)
        {
            return Mathf.Clamp(viewportWidth * 0.055f, 34f, 62f);
        }

        public void SelectPiece(string pieceId)
        {
            ClearSelection();
            if (string.IsNullOrEmpty(pieceId) || !pieceObjects.TryGetValue(pieceId, out var piece)) return;
            var bounds = CalculateBoundsInSpace(piece, effectsRoot);
            selectionFrame = ProceduralFactory.FrameBox(
                "SelectionHighlight",
                effectsRoot,
                bounds.size + Vector3.one * 0.12f,
                targetMaterial,
                0.055f);
            selectionFrame.transform.localPosition = bounds.center;
        }

        public void SelectTarget(int targetIndex)
        {
            ClearSelection();
            var target = targetsRoot.GetComponentsInChildren<SkybridgePickable>(false)
                .FirstOrDefault(candidate => candidate.Kind == PickKind.RoadTarget && candidate.Index == targetIndex);
            if (target == null) return;
            var bounds = CalculateBoundsInSpace(target.gameObject, effectsRoot);
            selectionFrame = ProceduralFactory.FrameBox(
                "TargetIssueHighlight",
                effectsRoot,
                bounds.size + Vector3.one * 0.12f,
                ghostInvalidMaterial,
                0.065f);
            selectionFrame.transform.localPosition = bounds.center;
        }

        public void ClearSelection()
        {
            if (selectionFrame == null) return;
            if (Application.isPlaying) Destroy(selectionFrame);
            else DestroyImmediate(selectionFrame);
            selectionFrame = null;
        }

        public void EmitSparks(Vector3 position, int count = 9)
        {
            StartCoroutine(SparkRoutine(position, Mathf.Clamp(count, 2, 16)));
        }

        public void EmitDust(Vector3 position, int count = 10)
        {
            StartCoroutine(DustRoutine(position, Mathf.Clamp(count, 2, 18)));
        }

        public void PulseWarningJoint(int pathNodeIndex)
        {
            PulseJoint(pathNodeIndex);
        }

        public void AnimatePiecePlacement(string pieceId)
        {
            if (reducedMotion || string.IsNullOrEmpty(pieceId) || !pieceObjects.TryGetValue(pieceId, out var piece) || piece == null) return;
            StartCoroutine(ScalePieceRoutine(piece.transform, true, 0.12f));
        }

        public void AnimatePieceRemoval(string pieceId)
        {
            if (reducedMotion || string.IsNullOrEmpty(pieceId) || !pieceObjects.TryGetValue(pieceId, out var piece) || piece == null) return;
            var echo = Instantiate(piece, effectsRoot, true);
            echo.name = $"Removing:{pieceId}";
            foreach (var pick in echo.GetComponentsInChildren<SkybridgePickable>(true)) Destroy(pick);
            foreach (var collider in echo.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (var body in echo.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
            StartCoroutine(ScalePieceRoutine(echo.transform, false, 0.14f));
        }

        public void SetVehicleGridPath(IReadOnlyList<GridPoint> nodes, bool showJoints = true)
        {
            if (nodes == null || nodes.Count < 2) return;
            StopVehicle();
            vehicleRouteNodes.Clear();
            vehicleRouteNodes.AddRange(nodes);
            VehicleRouteShowsJoints = showJoints;
            foreach (var joint in routeJoints.Values)
            {
                if (joint != null) Destroy(joint);
            }
            routeJoints.Clear();

            var startDirection = GridDirection(nodes[0], nodes[1]);
            var goalDirection = GridDirection(nodes[^2], nodes[^1]);
            var start = GridToWorld(nodes[0]);
            var goal = GridToWorld(nodes[^1]);
            var path = new List<Vector3> { start - startDirection * 1.55f + Vector3.up * VehicleClearance };
            for (var index = 0; index < nodes.Count; index++)
            {
                path.Add(GridToWorld(nodes[index]) + Vector3.up * VehicleClearance);
                if (showJoints) CreateMagneticJoint(index, nodes[index], GetRouteJointDirection(nodes, index));
            }
            path.Add(goal + goalDirection * 2f + Vector3.up * VehicleClearance);
            ConfigureRouteEndpoints(nodes);
            SetVehiclePath(path);
        }

        public void ConfigureRouteEndpoints(IReadOnlyList<GridPoint> nodes)
        {
            if (nodes == null || nodes.Count < 2 || levelRoot == null) return;
            var startDirection = GridDirection(nodes[0], nodes[1]);
            goalDirection = GridDirection(nodes[^2], nodes[^1]);
            var start = GridToWorld(level.StartAnchor);
            var goal = GridToWorld(level.GoalAnchor);
            AlignEndpoint("StartIsland", start - startDirection * 2.35f, startDirection);
            AlignEndpoint("StartAnchor", start, startDirection);
            AlignEndpoint("GoalIsland", goal + goalDirection * 2.35f, goalDirection);
            AlignEndpoint("GoalAnchor", goal, goalDirection);
            AlignEndpoint("GoalGate", goal + goalDirection * 1.35f, goalDirection);
            UpdateGoalZone(goal);
        }

        public IEnumerator RunVehicle(
            float speed,
            Action<float> onProgress,
            Action<int> onWaypoint,
            Action onComplete)
        {
            StopVehicle();
            var duration = vehiclePathLength / Mathf.Max(0.1f, speed);
            var elapsed = 0f;
            var previousWaypoint = 0;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var timeProgress = Mathf.Clamp01(elapsed / duration);
                var progress = SmootherStep(timeProgress);
                SetVehicleProgress(progress);
                onProgress?.Invoke(progress);
                var waypoint = FindWaypointIndex(progress);
                if (waypoint > previousWaypoint)
                {
                    for (var index = previousWaypoint + 1; index <= waypoint; index++) onWaypoint?.Invoke(index);
                    previousWaypoint = waypoint;
                }
                yield return null;
            }
            SetVehicleProgress(1f);
            vehicleRoutine = null;
            onComplete?.Invoke();
        }

        // Three.js uses MathUtils.smootherstep for vehicle travel. Unity's
        // Mathf.SmoothStep is cubic, so keep the quintic curve explicitly.
        private static float SmootherStep(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * value * (value * (value * 6f - 15f) + 10f);
        }

        public void StartVehicle(
            float speed,
            Action<float> onProgress,
            Action<int> onWaypoint,
            Action onComplete)
        {
            vehicleRoutine = StartCoroutine(RunVehicle(speed, onProgress, onWaypoint, onComplete));
        }

        public void StopVehicle()
        {
            if (vehicleRoutine != null)
            {
                StopCoroutine(vehicleRoutine);
                vehicleRoutine = null;
            }
            Time.timeScale = 1f;
        }

        public void StopSimulation()
        {
            StopAllCoroutines();
            vehicleRoutine = null;
            Time.timeScale = 1f;
        }

        public IEnumerator BreakAndFall(
            WeakPoint weakPoint,
            IReadOnlyCollection<string> fallingPieceIds,
            Action onFailureZoneEntered)
        {
            StopVehicle();
            if (routeJoints.TryGetValue(weakPoint.PathNodeIndex, out var breakingJoint) && breakingJoint != null)
            {
                breakingJoint.SetActive(false);
            }
            var breakPosition = GridToWorld(weakPoint.Joint);
            EmitSparks(breakPosition, reducedMotion ? 5 : 14);
            EmitDust(breakPosition + Vector3.down * 0.12f, reducedMotion ? 4 : 12);
            Time.timeScale = 0.35f;
            MinimumBreakVehicleY = vehicle != null ? GetContentPosition(vehicle.transform).y : float.PositiveInfinity;
            // The broken pieces are visual effects with collisions disabled. Integrate their
            // trajectories in content space so orbiting/scaling the workspace never alters the outcome.
            var fallingVisuals = new List<FallingVisual>();
            foreach (var id in fallingPieceIds)
            {
                if (!pieceObjects.TryGetValue(id, out var piece) || piece == null) continue;
                AddFallingVisual(fallingVisuals, piece, (Vector3.down + Vector3.right * 0.18f) * (2.6f / 1.4f), new Vector3(25, 12, -45));
            }
            if (vehicle != null)
                AddFallingVisual(fallingVisuals, vehicle, (Vector3.down + Vector3.forward * 0.08f) * (2.1f / 2.2f), new Vector3(15, 8, -30));
            var failureSent = false;
            var previousVehiclePosition = vehicle != null ? GetContentPosition(vehicle.transform) : Vector3.zero;
            var slowMotionElapsed = 0f;
            while (slowMotionElapsed < TestTimings.BreakSlowMotionMilliseconds / 1000f)
            {
                slowMotionElapsed = Mathf.Min(slowMotionElapsed + Time.unscaledDeltaTime, TestTimings.BreakSlowMotionMilliseconds / 1000f);
                UpdateFallingVisuals(fallingVisuals, slowMotionElapsed * 0.35f);
                if (vehicle != null)
                {
                    var vehiclePosition = GetContentPosition(vehicle.transform);
                    MinimumBreakVehicleY = Mathf.Min(MinimumBreakVehicleY, vehiclePosition.y);
                    if (!failureSent && CrossesFailureVolume(previousVehiclePosition, vehiclePosition))
                    {
                        failureSent = true;
                        onFailureZoneEntered?.Invoke();
                    }
                    previousVehiclePosition = vehiclePosition;
                }
                yield return null;
            }
            Time.timeScale = 1f;
            var elapsed = 0f;
            var displaySeconds = TestTimings.FallDisplayMilliseconds / 1000f;
            var recoverySeconds = TestTimings.MaxFailureRecoveryMilliseconds / 1000f;
            while (elapsed < displaySeconds || !failureSent && elapsed < recoverySeconds)
            {
                elapsed = Mathf.Min(elapsed + Time.unscaledDeltaTime, recoverySeconds);
                UpdateFallingVisuals(fallingVisuals, slowMotionElapsed * 0.35f + elapsed);
                if (vehicle != null)
                {
                    var vehiclePosition = GetContentPosition(vehicle.transform);
                    MinimumBreakVehicleY = Mathf.Min(MinimumBreakVehicleY, vehiclePosition.y);
                    if (!failureSent && CrossesFailureVolume(previousVehiclePosition, vehiclePosition))
                    {
                        failureSent = true;
                        onFailureZoneEntered?.Invoke();
                    }
                    previousVehiclePosition = vehiclePosition;
                }
                yield return null;
            }
        }

        private sealed class FallingVisual
        {
            public Transform Transform;
            public Vector3 Position, Velocity, AngularVelocity;
            public Quaternion Rotation;
        }

        private void AddFallingVisual(List<FallingVisual> visuals, GameObject item, Vector3 velocity, Vector3 angularVelocity)
        {
            var body = item.GetComponent<Rigidbody>();
            if (body != null) { body.isKinematic = true; body.detectCollisions = false; }
            visuals.Add(new FallingVisual
            {
                Transform = item.transform, Position = GetContentPosition(item.transform),
                Rotation = Quaternion.Inverse(transform.rotation) * item.transform.rotation,
                Velocity = velocity, AngularVelocity = angularVelocity,
            });
        }

        private void UpdateFallingVisuals(List<FallingVisual> visuals, float elapsed)
        {
            foreach (var item in visuals)
            {
                if (item.Transform == null) continue;
                var local = item.Position + item.Velocity * elapsed + Vector3.down * (0.5f * Physics.gravity.magnitude * elapsed * elapsed);
                item.Transform.SetPositionAndRotation(transform.TransformPoint(local),
                    transform.rotation * Quaternion.Euler(item.AngularVelocity * elapsed) * item.Rotation);
            }
        }

        // A fast fall can cross the shallow failure volume between two rendered frames.
        // Test the swept segment in content coordinates, including scaled XR workspaces.
        private bool CrossesFailureVolume(Vector3 previous, Vector3 current)
        {
            if (vehicleFailureZone.Contains(previous) || vehicleFailureZone.Contains(current)) return true;
            var delta = current - previous;
            return delta.sqrMagnitude > 0.000001f &&
                   vehicleFailureZone.IntersectRay(new Ray(previous, delta), out var enter) &&
                   enter >= 0f && enter <= delta.magnitude;
        }

        public bool IsVehicleFullyInGoal()
        {
            if (vehicle == null || VehicleProgress < 0.999f) return false;
            var collider = vehicle.GetComponent<BoxCollider>();
            var bounds = collider != null
                ? CalculateBoxColliderBoundsInSpace(collider, transform)
                : CalculateBoundsInSpace(vehicle, transform);
            return goalZone.Contains(bounds.min) && goalZone.Contains(bounds.max);
        }

        public void SetVehicleProgress(float progress)
        {
            VehicleProgress = Mathf.Clamp01(progress);
            if (vehicle == null || vehiclePath.Count < 2 || vehiclePathLength <= 0f) return;
            var distance = VehicleProgress * vehiclePathLength;
            var position = SampleVehiclePath(distance);
            var window = Mathf.Min(GridUnit * 0.22f, vehiclePathLength * 0.05f);
            var before = SampleVehiclePath(Mathf.Max(0f, distance - window));
            var after = SampleVehiclePath(Mathf.Min(vehiclePathLength, distance + window));
            var tangent = (after - before).normalized;
            var suspension = reducedMotion ? 0f : Mathf.Sin(VehicleProgress * Mathf.PI * 12f) * 0.012f;
            vehicle.transform.localPosition = position + Vector3.up * suspension;
            if (tangent.sqrMagnitude > 0.001f)
            {
                vehicle.transform.localRotation = DirectionQuaternion(tangent);
            }
            if (reducedMotion) return;
            foreach (var wheel in vehicleWheels)
            {
                if (wheel != null) wheel.Rotate(Vector3.forward, -Time.unscaledDeltaTime * 650f, Space.Self);
            }
        }

        public Bounds GetSceneBounds()
        {
            var renderers = levelRoot.GetComponentsInChildren<Renderer>().Concat(piecesRoot.GetComponentsInChildren<Renderer>()).ToArray();
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one * 10f);
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        public Vector3 GridToWorld(GridPoint point)
        {
            var volume = GetBuildVolume();
            var centerX = (volume.Min.X + volume.Max.X) * 0.5f;
            var centerZ = (volume.Min.Z + volume.Max.Z) * 0.5f;
            return new Vector3(
                (point.X - centerX) * GridUnit,
                RoadBaseY + point.Y * HeightUnit,
                (point.Z - centerZ) * GridUnit);
        }

        public Vector3 WorldToContentPoint(Vector3 worldPoint)
        {
            return transform.InverseTransformPoint(worldPoint);
        }

        private void BuildRoots()
        {
            environmentRoot = CreateRoot("EnvironmentRoot");
            levelRoot = CreateRoot("LevelRoot");
            piecesRoot = CreateRoot("PiecesRoot");
            targetsRoot = CreateRoot("TargetsRoot");
            effectsRoot = CreateRoot("EffectsRoot");
            ghostRoot = CreateRoot("GhostRoot");
        }

        private Transform CreateRoot(string rootName)
        {
            var root = new GameObject(rootName).transform;
            root.SetParent(transform, false);
            return root;
        }

        private void BuildMaterials()
        {
            environmentMaterial = AddMaterial(ProceduralFactory.Material("Environment", SkybridgePalette.Environment, 0.08f, 0.28f));
            environmentShadeMaterial = AddMaterial(ProceduralFactory.Material("EnvironmentShade", SkybridgePalette.EnvironmentShade, 0.12f, 0.22f));
            roadMaterial = AddMaterial(ProceduralFactory.Material("Road", SkybridgePalette.Road, 0.12f, 0.38f));
            roadEdgeMaterial = AddMaterial(ProceduralFactory.Material("RoadEdge", SkybridgePalette.RoadEdge, 0.28f, 0.48f));
            trussMaterial = AddMaterial(ProceduralFactory.Material("Truss", SkybridgePalette.Truss, 0.72f, 0.70f));
            accentMaterial = AddMaterial(ProceduralFactory.Material("Accent", SkybridgePalette.Accent, 0.35f, 0.54f));
            mechanicalMaterial = AddMaterial(ProceduralFactory.Material("Mechanical", SkybridgePalette.Mechanical, 0.76f, 0.70f));
            warningMaterial = AddMaterial(ProceduralFactory.Material("Warning", SkybridgePalette.Warning, 0.5f, 0.62f, SkybridgePalette.Warning * 0.45f));
            rockMaterial = AddMaterial(ProceduralFactory.Material("Rock", SkybridgePalette.Rock, 0.02f, 0.06f));
            voidMaterial = AddMaterial(ProceduralFactory.Material("Void", SkybridgePalette.Void, 0.18f, 0.18f));
            glassMaterial = AddMaterial(ProceduralFactory.Material("Glass", new Color(0.56f, 0.97f, 1f, 0.72f), 0.05f, 0.9f, null, true));
            targetMaterial = AddMaterial(ProceduralFactory.Material("Target", new Color(0.176f, 0.886f, 0.902f, 0.78f), 0.1f, 0.5f, SkybridgePalette.Valid * 0.22f, true));
            ghostValidMaterial = AddMaterial(ProceduralFactory.Material("GhostValid", new Color(0.176f, 0.886f, 0.902f, 0.44f), 0.15f, 0.55f, SkybridgePalette.Valid * 0.35f, true));
            ghostInvalidMaterial = AddMaterial(ProceduralFactory.Material("GhostInvalid", new Color(1f, 0.294f, 0.227f, 0.44f), 0.15f, 0.55f, SkybridgePalette.Invalid * 0.35f, true));
            LoadImportedModelMaterials();
        }

        private void LoadImportedModelMaterials()
        {
            islandModelMaterial = LoadPersistentModelMaterial(
                "SkybridgeMaterials/IslandModel",
                "IslandModelMaterial",
                IslandTextureResource,
                SkybridgePalette.Environment);
            rescueVehicleModelMaterial = LoadPersistentModelMaterial(
                "SkybridgeMaterials/RescueVehicleModel",
                "RescueVehicleModelMaterial",
                RescueVehicleTextureResource,
                SkybridgePalette.Mechanical);
            goalGateModelMaterial = LoadPersistentModelMaterial(
                "SkybridgeMaterials/GoalGateModel",
                "GoalGateModelMaterial",
                GoalGateTextureResource,
                SkybridgePalette.Accent);
            islandModelReady = Resources.Load<GameObject>(IslandModelResource) != null;
            rescueVehicleModelReady = Resources.Load<GameObject>(RescueVehicleModelResource) != null;
            goalGateModelReady = Resources.Load<GameObject>(GoalGateModelResource) != null;
        }

        private Material LoadPersistentModelMaterial(
            string materialResource,
            string name,
            string textureResource,
            Color fallback)
        {
            var persistent = Resources.Load<Material>(materialResource);
            return persistent != null
                ? persistent
                : AddModelMaterial(name, textureResource, fallback);
        }

        private Material AddModelMaterial(string name, string textureResource, Color fallback)
        {
            var material = AddMaterial(ProceduralFactory.Material(name, fallback, 0.28f, 0.48f));
            var texture = Resources.Load<Texture2D>(textureResource);
            if (texture != null)
            {
                material.mainTexture = texture;
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            }
            return material;
        }

        private Material AddMaterial(Material material)
        {
            materials.Add(material);
            return material;
        }

        private void ConfigureRenderEnvironment()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = SkybridgePalette.SceneBackground;
            RenderSettings.fogStartDistance = SceneFogNear;
            RenderSettings.fogEndDistance = SceneFogFar;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = SkybridgePalette.Hex("F7FBFF");
            RenderSettings.ambientEquatorColor = SkybridgePalette.Hex("BAC8CA");
            RenderSettings.ambientGroundColor = SkybridgePalette.Hex("4A5552");
            RenderSettings.ambientIntensity = 1.1f;
            if (WorldCamera != null)
            {
                WorldCamera.clearFlags = CameraClearFlags.SolidColor;
                WorldCamera.backgroundColor = SkybridgePalette.SceneBackground;
                if (Application.platform != RuntimePlatform.Android)
                {
                    // Desktop uses its original perspective and focus envelope.
                    // PICO owns its XR projection and clipping contract.
                    WorldCamera.farClipPlane = 150f;
                    WorldCamera.fieldOfView = SceneFieldOfView;
                }
            }
        }

        private void LateUpdate()
        {
            UpdateIslandOcclusion();
        }

        private void RegisterIslandOcclusionStates()
        {
            foreach (var islandName in new[] { "StartIsland", "GoalIsland" })
            {
                var island = levelRoot.Find(islandName);
                if (island == null) continue;
                var renderers = island.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;
                var originals = renderers.Select(renderer => renderer.sharedMaterials).ToArray();
                var faded = originals.Select(materialSet => materialSet.Select(CreateOcclusionMaterial).ToArray()).ToArray();
                islandOcclusionStates.Add(new IslandOcclusionState(island, renderers, originals, faded));
            }
        }

        private Material CreateOcclusionMaterial(Material original)
        {
            if (original == null) return null;
            var faded = new Material(original) { name = $"{original.name}:Occluded" };
            var color = faded.HasProperty("_BaseColor") ? faded.GetColor("_BaseColor") : faded.color;
            color.a = Mathf.Min(0.28f, color.a);
            if (faded.HasProperty("_BaseColor")) faded.SetColor("_BaseColor", color);
            else faded.color = color;
            faded.SetFloat("_Surface", 1f);
            faded.SetFloat("_Blend", 0f);
            faded.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            faded.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            faded.SetFloat("_ZWrite", 0f);
            faded.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            faded.renderQueue = (int)RenderQueue.Transparent;
            return faded;
        }

        private void UpdateIslandOcclusion()
        {
            if (WorldCamera == null || islandOcclusionStates.Count == 0 || pieceObjects.Count == 0) return;
            var cameraPosition = WorldCamera.transform.position;
            foreach (var state in islandOcclusionStates)
            {
                if (state.Island == null) continue;
                var islandBounds = CalculateBounds(state.Island.gameObject);
                var occluded = false;
                foreach (var piece in pieceObjects.Values)
                {
                    if (piece == null) continue;
                    var pieceCenter = CalculateBounds(piece).center;
                    var delta = pieceCenter - cameraPosition;
                    var distance = delta.magnitude;
                    if (distance <= 0.1f) continue;
                    var ray = new Ray(cameraPosition, delta / distance);
                    if (islandBounds.IntersectRay(ray, out var hitDistance) && hitDistance < distance - 0.35f)
                    {
                        occluded = true;
                        break;
                    }
                }
                state.SetOccluded(occluded);
            }
        }

        private void ClearIslandOcclusionStates()
        {
            foreach (var state in islandOcclusionStates) state.Dispose();
            islandOcclusionStates.Clear();
        }

        private void ConfigureLights()
        {
            var sun = new GameObject("KeySun").AddComponent<Light>();
            sun.transform.SetParent(transform, false);
            sun.type = LightType.Directional;
            sun.color = Color.white;
            sun.intensity = 3.2f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.82f;
            sun.transform.rotation = Quaternion.Euler(48f, -132f, 0f);

            var fill = new GameObject("FillLight").AddComponent<Light>();
            fill.transform.SetParent(transform, false);
            fill.type = LightType.Directional;
            fill.color = SkybridgePalette.Hex("B9E8ED");
            fill.intensity = 0.75f;
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(38f, 42f, 0f);
        }

        private void CreateEnvironment()
        {
            var volume = GetBuildVolume();
            var minimum = GridToWorld(volume.Min);
            var maximum = GridToWorld(volume.Max);
            var width = Mathf.Max(38f, Mathf.Abs(maximum.x - minimum.x) + 14f);
            var depth = Mathf.Max(20f, Mathf.Abs(maximum.z - minimum.z) + 14f);
            var voidWidth = Mathf.Max(21f, Mathf.Abs(maximum.x - minimum.x) + 6f);
            var voidDepth = Mathf.Max(8.4f, Mathf.Abs(maximum.z - minimum.z) + 6f);
            ProceduralFactory.Box("EnvironmentTable", environmentRoot, new Vector3(0f, TableTopY - 0.45f, 0f), new Vector3(width, 0.9f, depth), environmentMaterial);
            ProceduralFactory.Box("EnvironmentTableEdge", environmentRoot, new Vector3(0f, TableTopY - 0.93f, 0f), new Vector3(width + 0.2f, 0.12f, depth + 0.2f), mechanicalMaterial);
            ProceduralFactory.Box("EnvironmentVoid", environmentRoot, new Vector3(0f, TableTopY + 0.045f, 0f), new Vector3(voidWidth, 0.08f, voidDepth), voidMaterial);
            ProceduralFactory.Box("EnvironmentSafetyFront", environmentRoot, new Vector3(0f, TableTopY + 0.095f, voidDepth * 0.5f + 0.07f), new Vector3(voidWidth + 0.4f, 0.035f, 0.12f), accentMaterial);
            ProceduralFactory.Box("EnvironmentSafetyRear", environmentRoot, new Vector3(0f, TableTopY + 0.095f, -voidDepth * 0.5f - 0.07f), new Vector3(voidWidth + 0.4f, 0.035f, 0.12f), accentMaterial);
        }

        private void CreateLevelGeometry()
        {
            var volume = GetBuildVolume();
            var volumeMin = GridToWorld(volume.Min);
            var volumeMax = GridToWorld(volume.Max);
            var horizontalMargin = GridUnit * 2.5f;
            vehicleFailureZone.SetMinMax(
                new Vector3(
                    Mathf.Min(volumeMin.x, volumeMax.x) - horizontalMargin,
                    TableTopY - 0.4f,
                    Mathf.Min(volumeMin.z, volumeMax.z) - horizontalMargin),
                new Vector3(
                    Mathf.Max(volumeMin.x, volumeMax.x) + horizontalMargin,
                    TableTopY + 0.32f,
                    Mathf.Max(volumeMin.z, volumeMax.z) + horizontalMargin));
            GetDockDirections(out var startDirection, out goalDirection);
            var start = GridToWorld(level.StartAnchor);
            var goal = GridToWorld(level.GoalAnchor);
            // Keep the island silhouettes in lockstep with the Three.js scene
            // configuration.  The first four levels happen to follow the
            // historical id-based sequence, but level five intentionally
            // reuses the variant-3 goal silhouette (rather than variant 1).
            // An explicit mapping prevents a future level-id change from
            // silently changing the authored visual language.
            CreateIsland("StartIsland", start - startDirection * 2.35f, startDirection, GetStartIslandVariant(level.Id), false);
            CreateIsland("GoalIsland", goal + goalDirection * 2.35f, goalDirection, GetGoalIslandVariant(level.Id), true);
            CreateBridgeAnchor(start, startDirection, false);
            CreateBridgeAnchor(goal, goalDirection, true);
            foreach (var pad in level.FoundationPads) CreateFoundation(pad);
            if (level.Id == LevelId.Level4) CreateTrussGuide();
            CreateGoalGate(goal + goalDirection * 1.35f, goalDirection);
            CreateVehicle(start - startDirection * 1.55f + Vector3.up * VehicleClearance, startDirection);
            UpdateGoalZone(goal);
        }

        // Public read-only selectors keep the authored Three.js scene mapping
        // testable without exposing mutable presentation state.
        public static int GetStartIslandVariant(LevelId levelId)
        {
            return levelId switch
            {
                LevelId.Level1 => 0,
                LevelId.Level2 => 1,
                LevelId.Level3 => 2,
                LevelId.Level4 => 3,
                LevelId.Level5 => 0,
                _ => 0,
            };
        }

        public static int GetGoalIslandVariant(LevelId levelId)
        {
            return levelId switch
            {
                LevelId.Level1 => 1,
                LevelId.Level2 => 2,
                LevelId.Level3 => 3,
                LevelId.Level4 => 0,
                LevelId.Level5 => 3,
                _ => 1,
            };
        }

        private void AlignEndpoint(string objectName, Vector3 position, Vector3 direction)
        {
            var endpoint = levelRoot.Find(objectName);
            if (endpoint == null) return;
            endpoint.localPosition = position;
            endpoint.localRotation = DirectionQuaternion(direction);
        }

        /// <summary>
        /// Matches Three.js rebuildRouteJoints(): an interior corner is
        /// oriented along the bisector from the previous node to the next
        /// node, rather than inheriting only the incoming edge direction.
        /// Vertical slope is intentionally ignored for the magnetic lock's
        /// horizontal face, just as in the reference scene.
        /// </summary>
        public static Vector3 GetRouteJointDirection(IReadOnlyList<GridPoint> nodes, int index)
        {
            if (nodes == null || nodes.Count < 2)
            {
                return Vector3.right;
            }

            var clampedIndex = Mathf.Clamp(index, 0, nodes.Count - 1);
            var from = nodes[Mathf.Max(0, clampedIndex - 1)];
            var to = nodes[Mathf.Min(nodes.Count - 1, clampedIndex + 1)];
            var horizontal = new Vector3(to.X - from.X, 0f, to.Z - from.Z);
            if (horizontal.sqrMagnitude > 0.001f)
            {
                return horizontal.normalized;
            }

            // This only occurs for a degenerate duplicated node.  Keep the
            // same deterministic fallback as gridEdgeDirection in Three.js.
            return Vector3.right;
        }

        /// <summary>
        /// Builds the same basis as SkybridgeScene.directionQuaternion().
        /// Local +X follows the road/vehicle direction while local +Y remains
        /// the projected world-up axis.  Quaternion.FromToRotation only fixes
        /// the +X axis and can therefore introduce visible roll on a sloped
        /// edge that runs along Z.
        /// </summary>
        public static Quaternion DirectionQuaternion(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.0001f)
            {
                return Quaternion.identity;
            }

            var xAxis = direction.normalized;
            var zAxis = Vector3.Cross(xAxis, Vector3.up);
            if (zAxis.sqrMagnitude < 0.0001f)
            {
                zAxis = Vector3.forward;
            }
            else
            {
                zAxis.Normalize();
            }

            var yAxis = Vector3.Cross(zAxis, xAxis).normalized;
            return Quaternion.LookRotation(zAxis, yAxis);
        }

        private void UpdateGoalZone(Vector3 goal)
        {
            const float length = 3.4f;
            var width = RoadWidth * 1.64f;
            var size = new Vector3(
                Mathf.Abs(goalDirection.x) * length + Mathf.Abs(goalDirection.z) * width,
                2.43f,
                Mathf.Abs(goalDirection.z) * length + Mathf.Abs(goalDirection.x) * width);
            goalZone = new Bounds(goal + goalDirection * 1.95f + Vector3.up * 1.135f, size);
        }

        private void CreateIsland(string name, Vector3 position, Vector3 direction, int variant, bool goal)
        {
            var group = new GameObject(name);
            group.transform.SetParent(levelRoot, false);
            group.transform.localPosition = position;
            group.transform.localRotation = DirectionQuaternion(direction);
            if (!TryCreateImportedModel(
                    group.transform,
                    IslandModelResource,
                    islandModelMaterial,
                    new Vector3(5.35f, 2.9f, 4.45f),
                    new Vector3(-90f, 0f, 0f),
                    goal,
                    out _))
            {
                throw new InvalidOperationException($"Missing required island model at Resources/{IslandModelResource}.");
            }
        }

        private void CreateBridgeAnchor(Vector3 position, Vector3 direction, bool goal)
        {
            var group = new GameObject(goal ? "GoalAnchor" : "StartAnchor");
            group.transform.SetParent(levelRoot, false);
            group.transform.localPosition = position;
            group.transform.localRotation = DirectionQuaternion(direction);
            ProceduralFactory.Box("Plate", group.transform, new Vector3(0f, -0.12f, 0f), new Vector3(0.58f, 0.22f, 1.82f), mechanicalMaterial);
            ProceduralFactory.Box("Cap", group.transform, new Vector3(goal ? 0.19f : -0.19f, -0.05f, 0f), new Vector3(0.16f, 0.26f, 1.96f), accentMaterial);
        }

        private void CreateFoundation(GridPoint point)
        {
            var group = new GameObject($"Foundation:{point.Key}");
            group.transform.SetParent(levelRoot, false);
            var position = GridToWorld(point);
            group.transform.localPosition = new Vector3(position.x, TableTopY + 0.12f, position.z);
            ProceduralFactory.Cylinder("Pad", group.transform, Vector3.zero, 0.94f, 0.28f, environmentMaterial, 8);
            ProceduralFactory.Cylinder("Socket", group.transform, Vector3.up * 0.2f, 0.45f, 0.16f, mechanicalMaterial, 16);
            var ring = ProceduralFactory.Cylinder("CyanRing", group.transform, Vector3.up * 0.31f, 0.54f, 0.055f, trussMaterial, 24);
            ring.transform.localScale = new Vector3(1f, 1f, 1f);
            for (var index = 0; index < 4; index++)
            {
                var angle = index * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                ProceduralFactory.Cylinder("Bolt", group.transform, new Vector3(Mathf.Cos(angle) * 0.66f, 0.19f, Mathf.Sin(angle) * 0.66f), 0.055f, 0.09f, mechanicalMaterial, 8);
            }
        }

        private void CreateGoalGate(Vector3 position, Vector3 direction)
        {
            var group = new GameObject("GoalGate");
            group.transform.SetParent(levelRoot, false);
            group.transform.localPosition = position;
            group.transform.localRotation = DirectionQuaternion(direction);
            if (!TryCreateImportedModel(
                    group.transform,
                    GoalGateModelResource,
                    goalGateModelMaterial,
                    new Vector3(0.9f, 2.45f, 1.75f),
                    Vector3.zero,
                    false,
                    out _))
            {
                throw new InvalidOperationException($"Missing required goal gate model at Resources/{GoalGateModelResource}.");
            }
        }

        private void CreateVehicle(Vector3 position, Vector3 direction)
        {
            vehicle = new GameObject("UnmannedRescueVehicle");
            vehicle.transform.SetParent(levelRoot, false);
            vehicle.transform.localPosition = position;
            vehicle.transform.localRotation = DirectionQuaternion(direction);
            if (!TryCreateImportedModel(
                    vehicle.transform,
                    RescueVehicleModelResource,
                    rescueVehicleModelMaterial,
                    new Vector3(1.62f, 1.05f, 1.08f),
                    Vector3.zero,
                    false,
                    out _))
            {
                throw new InvalidOperationException($"Missing required rescue vehicle model at Resources/{RescueVehicleModelResource}.");
            }

            // The imported mesh supplies the visual vehicle.  The compact box
            // preserves the old deterministic failure physics and goal-bounds
            // contract without making the detailed FBX mesh a dynamic collider.
            var vehicleCollider = vehicle.AddComponent<BoxCollider>();
            vehicleCollider.center = new Vector3(0f, 0.43f, 0f);
            vehicleCollider.size = new Vector3(1.62f, 0.86f, 1.08f);
        }

        private bool TryCreateImportedModel(
            Transform parent,
            string resourcePath,
            Material material,
            Vector3 targetSize,
            Vector3 sourceEulerCorrection,
            bool goalEndpoint,
            out GameObject model)
        {
            model = null;

            // Scene-authored templates are the source of truth when the
            // bootstrap scene contains the model adjustment layer.  Cloning
            // the complete template preserves Inspector edits to renderer
            // materials, texture slots, child rotations and local offsets in
            // the Game view.  The fallback below remains available for test
            // scenes that intentionally do not load the authoring scene.
            if (sceneModelLibrary != null &&
                sceneModelLibrary.TryGetTemplate(resourcePath, goalEndpoint, out var sceneTemplate) &&
                sceneTemplate != null)
            {
                model = Instantiate(sceneTemplate, parent, false);
                model.name = "ImportedModel";
                // Keep the gameplay wrapper canonical while preserving every authored mesh pose.
                // A rotated wrapper plus a counter-rotated mesh is valid authoring, but testing
                // only wrapper.up otherwise misreports an upright island as inverted.
                var authoredRotation = model.transform.localRotation;
                foreach (Transform child in model.transform)
                {
                    child.localPosition = authoredRotation * child.localPosition;
                    child.localRotation = authoredRotation * child.localRotation;
                }
                model.transform.localRotation = Quaternion.identity;
                model.SetActive(true);
                foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (var body in model.GetComponentsInChildren<Rigidbody>(true))
                {
                    body.isKinematic = true;
                    body.detectCollisions = false;
                }
                foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }

                var sceneBounds = CalculateBounds(model);
                if (sceneBounds.size.sqrMagnitude < 0.0001f)
                {
                    Debug.LogError($"SKYBRIDGE_MODEL_EMPTY_SCENE_TEMPLATE {resourcePath}");
                    Destroy(model);
                    model = null;
                    return false;
                }

                var sceneUpDot = Vector3.Dot(model.transform.up, transform.up);
                Debug.Log($"SKYBRIDGE_SCENE_MODEL {resourcePath} size={sceneBounds.size} bottom={sceneBounds.min.y:0.000} upDot={sceneUpDot:0.000}");
                if (sceneUpDot < 0.95f)
                {
                    Debug.LogError($"SKYBRIDGE_SCENE_MODEL_NOT_UPRIGHT {resourcePath} upDot={sceneUpDot:0.000}");
                }
                return true;
            }

            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                Debug.LogError($"SKYBRIDGE_MODEL_MISSING Resources/{resourcePath}");
                return false;
            }

            // Keep the gameplay-facing ImportedModel root upright and put any
            // FBX axis correction on a nested source object.  Testing only the
            // prefab root's up vector is insufficient because an FBX importer
            // can place its -90 degree conversion on an internal mesh node.
            model = new GameObject("ImportedModel");
            model.transform.SetParent(parent, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            var source = Instantiate(prefab, model.transform, false);
            source.name = "SourceModel";
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.Euler(sourceEulerCorrection);
            source.transform.localScale = Vector3.one;

            var renderers = source.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogError($"SKYBRIDGE_MODEL_EMPTY Resources/{resourcePath}");
                Destroy(model);
                model = null;
                return false;
            }

            foreach (var collider in source.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in source.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
            foreach (var animator in source.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            foreach (var renderer in renderers)
            {
                var slots = Mathf.Max(1, renderer.sharedMaterials.Length);
                var shared = new Material[slots];
                for (var index = 0; index < shared.Length; index++) shared[index] = material;
                renderer.sharedMaterials = shared;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            var bounds = CalculateLocalBounds(model.transform);
            if (bounds.size.sqrMagnitude < 0.0001f)
            {
                Debug.LogError($"SKYBRIDGE_MODEL_INVALID_BOUNDS Resources/{resourcePath}");
                Destroy(model);
                model = null;
                return false;
            }

            // Imported FBX files are not guaranteed to use the same forward
            // axis. The island's thin source axis is Z, so CreateIsland passes
            // an explicit X-axis correction; vehicle and gate still receive
            // the conservative footprint check below.
            var allowAspectYaw = resourcePath != IslandModelResource;
            var targetAspect = targetSize.x / Mathf.Max(0.001f, targetSize.z);
            var aspect0 = bounds.size.x / Mathf.Max(0.001f, bounds.size.z);
            var aspect90 = bounds.size.z / Mathf.Max(0.001f, bounds.size.x);
            if (allowAspectYaw &&
                Mathf.Abs(Mathf.Log(Mathf.Max(0.001f, aspect90 / targetAspect))) <
                Mathf.Abs(Mathf.Log(Mathf.Max(0.001f, aspect0 / targetAspect))))
            {
                // Apply footprint yaw around the gameplay world's up axis.
                // Post-multiplying here would rotate around the FBX's already
                // corrected local Y axis; for the island's X-axis correction
                // that would stand the platform on its side again.
                source.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * source.transform.localRotation;
                bounds = CalculateLocalBounds(model.transform);
            }

            var scale = Mathf.Min(
                targetSize.x / Mathf.Max(0.001f, bounds.size.x),
                targetSize.y / Mathf.Max(0.001f, bounds.size.y),
                targetSize.z / Mathf.Max(0.001f, bounds.size.z));
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f) scale = 1f;
            source.transform.localScale = Vector3.one * scale;
            // Measuring from the identity wrapper includes both the explicit
            // FBX pose correction and the newly applied scale.  The resulting
            // offset is therefore already in gameplay-parent coordinates.
            bounds = CalculateLocalBounds(model.transform);

            // Keep the imported visual centered over the gameplay anchor. The
            // island is aligned by its top surface; the vehicle and gate sit
            // on the anchor plane with their lowest point at local Y = 0.
            var alignTop = resourcePath == IslandModelResource;
            source.transform.localPosition = new Vector3(
                -bounds.center.x,
                alignTop ? -bounds.max.y : -bounds.min.y,
                -bounds.center.z);

            var worldBounds = CalculateBounds(model);
            // Upright is relative to the bridge workspace, which the player can freely rotate.
            var upDot = Vector3.Dot(model.transform.up, transform.up);
            Debug.Log($"SKYBRIDGE_IMPORTED_MODEL {resourcePath} size={worldBounds.size} scale={scale:0.000} bottom={worldBounds.min.y:0.000} upDot={upDot:0.000}");
            if (upDot < 0.95f)
            {
                Debug.LogError($"SKYBRIDGE_MODEL_NOT_UPRIGHT {resourcePath} upDot={upDot:0.000}");
            }
            return true;
        }

        /// <summary>
        /// Creates the normalized, editable model wrapper used by the scene
        /// authoring layer.  Keeping this fitting code next to the runtime
        /// importer guarantees that the Scene preview and the Resources
        /// fallback start with identical scale, axis and ground alignment.
        /// </summary>
        public static GameObject BuildSceneModelTemplate(
            GameObject prefab,
            Material material,
            Vector3 targetSize,
            Vector3 sourceEulerCorrection,
            bool allowAspectYaw,
            bool alignTop,
            Transform parent = null)
        {
            if (prefab == null) return null;

            var model = new GameObject("ImportedModel");
            if (parent != null) model.transform.SetParent(parent, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            var source = UnityEngine.Object.Instantiate(prefab, model.transform, false);
            source.name = "SourceModel";
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.Euler(sourceEulerCorrection);
            source.transform.localScale = Vector3.one;

            var renderers = source.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                DestroyTemplateObject(model);
                return null;
            }

            foreach (var collider in source.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in source.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
            foreach (var animator in source.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            foreach (var renderer in renderers)
            {
                if (material != null)
                {
                    var slots = Mathf.Max(1, renderer.sharedMaterials.Length);
                    var shared = new Material[slots];
                    for (var index = 0; index < shared.Length; index++) shared[index] = material;
                    renderer.sharedMaterials = shared;
                }
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            var bounds = CalculateLocalBounds(model.transform);
            if (bounds.size.sqrMagnitude < 0.0001f)
            {
                DestroyTemplateObject(model);
                return null;
            }

            var targetAspect = targetSize.x / Mathf.Max(0.001f, targetSize.z);
            var aspect0 = bounds.size.x / Mathf.Max(0.001f, bounds.size.z);
            var aspect90 = bounds.size.z / Mathf.Max(0.001f, bounds.size.x);
            if (allowAspectYaw &&
                Mathf.Abs(Mathf.Log(Mathf.Max(0.001f, aspect90 / targetAspect))) <
                Mathf.Abs(Mathf.Log(Mathf.Max(0.001f, aspect0 / targetAspect))))
            {
                source.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * source.transform.localRotation;
                bounds = CalculateLocalBounds(model.transform);
            }

            var scale = Mathf.Min(
                targetSize.x / Mathf.Max(0.001f, bounds.size.x),
                targetSize.y / Mathf.Max(0.001f, bounds.size.y),
                targetSize.z / Mathf.Max(0.001f, bounds.size.z));
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f) scale = 1f;
            source.transform.localScale = Vector3.one * scale;
            bounds = CalculateLocalBounds(model.transform);
            source.transform.localPosition = new Vector3(
                -bounds.center.x,
                alignTop ? -bounds.max.y : -bounds.min.y,
                -bounds.center.z);
            return model;
        }

        private static void DestroyTemplateObject(GameObject model)
        {
            if (model == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(model);
            else UnityEngine.Object.DestroyImmediate(model);
        }

        private GameObject CreateRoadModel(GridPoint from, GridPoint to, PieceType type, bool ghost)
        {
            var group = new GameObject(type == PieceType.Ramp ? "Ramp" : "Deck");
            group.transform.SetParent(ghost ? ghostRoot : piecesRoot, false);
            var start = GridToWorld(from);
            var end = GridToWorld(to);
            var delta = end - start;
            group.transform.localPosition = (start + end) * 0.5f;
            group.transform.localRotation = DirectionQuaternion(delta);
            var length = delta.magnitude * 0.94f;
            ProceduralFactory.Box("DeckSurface", group.transform, new Vector3(0f, -0.11f, 0f), new Vector3(length, 0.22f, RoadWidth), roadMaterial, true);
            ProceduralFactory.Box("Underlay", group.transform, new Vector3(0f, -0.245f, 0f), new Vector3(length * 0.96f, 0.09f, RoadWidth * 0.86f), roadEdgeMaterial);
            foreach (var side in new[] { -1f, 1f })
            {
                ProceduralFactory.Box("Rail", group.transform, new Vector3(0f, 0.095f, side * (RoadWidth * 0.5f - 0.045f)), new Vector3(length, 0.11f, 0.09f), accentMaterial);
                foreach (var endSide in new[] { -1f, 1f })
                {
                    ProceduralFactory.Box("Post", group.transform, new Vector3(endSide * length * 0.42f, 0.18f, side * (RoadWidth * 0.5f - 0.045f)), new Vector3(0.08f, 0.3f, 0.1f), accentMaterial);
                }
            }
            return group;
        }

        private GameObject CreatePierModel(GridPoint top, bool ghost)
        {
            var group = new GameObject("Pier");
            group.transform.SetParent(ghost ? ghostRoot : piecesRoot, false);
            var position = GridToWorld(top);
            group.transform.localPosition = new Vector3(position.x, 0f, position.z);
            var topY = RoadBaseY + top.Y * HeightUnit - 0.22f;
            var bottomY = TableTopY + 0.45f;
            var height = Mathf.Max(0.4f, topY - bottomY);
            ProceduralFactory.Box("Column", group.transform, new Vector3(0f, bottomY + height * 0.5f, 0f), new Vector3(0.5f, height, 0.5f), mechanicalMaterial, true);
            ProceduralFactory.Box("Shell", group.transform, new Vector3(0f, bottomY + height * 0.47f, 0f), new Vector3(0.78f, height * 0.72f, 0.72f), environmentMaterial);
            ProceduralFactory.Box("Cap", group.transform, new Vector3(0f, topY - 0.11f, 0f), new Vector3(1.25f, 0.22f, 1.25f), trussMaterial);
            ProceduralFactory.Cylinder("Foot", group.transform, new Vector3(0f, bottomY, 0f), 0.6f, 0.24f, mechanicalMaterial, 8);
            return group;
        }

        private GameObject CreateTrussModel(GridPoint from, GridPoint to, bool ghost)
        {
            var group = new GameObject("Truss");
            group.transform.SetParent(ghost ? ghostRoot : piecesRoot, false);
            var start = GridToWorld(from);
            var end = GridToWorld(to);
            var delta = end - start;
            group.transform.localPosition = (start + end) * 0.5f;
            group.transform.localRotation = DirectionQuaternion(delta);
            var half = delta.magnitude * 0.46f;
            foreach (var side in new[] { -1f, 1f })
            {
                var z = side * (RoadWidth * 0.5f - 0.07f);
                ProceduralFactory.Beam("UpperBeam", group.transform, new Vector3(-half, -0.32f, z), new Vector3(half, -0.32f, z), 0.055f, trussMaterial);
                ProceduralFactory.Beam("LowerBeam", group.transform, new Vector3(-half, -0.88f, z), new Vector3(half, -0.88f, z), 0.065f, trussMaterial);
                ProceduralFactory.Beam("DiagonalA", group.transform, new Vector3(-half, -0.32f, z), new Vector3(half, -0.88f, z), 0.06f, trussMaterial);
                ProceduralFactory.Beam("DiagonalB", group.transform, new Vector3(-half, -0.88f, z), new Vector3(half, -0.32f, z), 0.06f, trussMaterial);
            }
            foreach (var x in new[] { -half, half })
            {
                ProceduralFactory.Beam("CrossBeam", group.transform, new Vector3(x, -0.88f, -RoadWidth * 0.43f), new Vector3(x, -0.88f, RoadWidth * 0.43f), 0.055f, trussMaterial);
            }
            return group;
        }

        private void RegisterPiece(GameObject piece, string id, PieceType type)
        {
            pieceObjects[id] = piece;
            var pickable = piece.AddComponent<SkybridgePickable>();
            pickable.Kind = PickKind.Piece;
            pickable.Id = id;
            pickable.PieceType = type.ToString();
            if (piece.GetComponentInChildren<Collider>() == null)
            {
                var bounds = CalculateLocalBounds(piece.transform);
                var collider = piece.AddComponent<BoxCollider>();
                collider.center = bounds.center;
                collider.size = bounds.size;
            }
        }

        private void CreateRoadTarget(GameObject target, GridPoint from, GridPoint to, bool truss)
        {
            var start = GridToWorld(from);
            var end = GridToWorld(to);
            var delta = end - start;
            target.transform.localPosition = (start + end) * 0.5f + Vector3.up * (truss ? -0.44f : 0.07f);
            target.transform.localRotation = DirectionQuaternion(delta);
            var length = delta.magnitude * 0.92f;
            ProceduralFactory.FrameBox("TargetFrame", target.transform, new Vector3(length, 0.06f, RoadWidth * 1.08f), targetMaterial, 0.04f);
            var collider = target.AddComponent<BoxCollider>();
            collider.size = new Vector3(delta.magnitude * 0.9f, 0.64f, RoadWidth * 0.8f);
            collider.center = Vector3.up * 0.16f;
        }

        private void CreateFoundationTarget(GameObject target, GridPoint point)
        {
            var position = GridToWorld(point);
            target.transform.localPosition = new Vector3(position.x, TableTopY + 0.34f, position.z);
            ProceduralFactory.FrameBox("FoundationFrame", target.transform, new Vector3(1.35f, 0.06f, 1.35f), targetMaterial, 0.045f);
            var collider = target.AddComponent<BoxCollider>();
            collider.size = new Vector3(1.55f, 0.55f, 1.55f);
        }

        private void ApplyGhostMaterial(GameObject root, bool valid)
        {
            var material = valid ? ghostValidMaterial : ghostInvalidMaterial;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = material;
            if (!valid)
            {
                ProceduralFactory.Beam("InvalidA", root.transform, new Vector3(-0.55f, 0.72f, -0.55f), new Vector3(0.55f, 0.72f, 0.55f), 0.035f, ghostInvalidMaterial);
                ProceduralFactory.Beam("InvalidB", root.transform, new Vector3(-0.55f, 0.72f, 0.55f), new Vector3(0.55f, 0.72f, -0.55f), 0.035f, ghostInvalidMaterial);
            }
        }

        private void CreateMagneticJoint(int index, GridPoint point, Vector3 direction)
        {
            var group = new GameObject($"Joint:{index}");
            group.transform.SetParent(levelRoot, false);
            group.transform.localPosition = GridToWorld(point) + Vector3.down * 0.18f;
            group.transform.localRotation = DirectionQuaternion(direction);
            foreach (var side in new[] { -1f, 1f })
            {
                var joint = ProceduralFactory.Cylinder("Lock", group.transform, new Vector3(0f, 0f, side * (RoadWidth * 0.5f + 0.03f)), 0.18f, 0.12f, mechanicalMaterial, 20);
                joint.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var inset = ProceduralFactory.Cylinder("Inset", group.transform, new Vector3(0f, 0f, side * (RoadWidth * 0.5f + 0.035f)), 0.085f, 0.135f, trussMaterial, 16);
                inset.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            routeJoints[index] = group;
        }

        private void PulseJoint(int pathNodeIndex)
        {
            if (!routeJoints.TryGetValue(pathNodeIndex, out var joint) || joint == null) return;
            foreach (var renderer in joint.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = warningMaterial;
            if (!reducedMotion) StartCoroutine(PulseRoutine(joint.transform));
        }

        private IEnumerator PulseRoutine(Transform joint)
        {
            var elapsed = 0f;
            while (elapsed < 0.76f && joint != null)
            {
                elapsed += Time.unscaledDeltaTime;
                var pulse = 1f + Mathf.Sin(elapsed / 0.76f * Mathf.PI * 4f) * 0.16f;
                joint.localScale = Vector3.one * pulse;
                yield return null;
            }
            if (joint != null) joint.localScale = Vector3.one;
        }

        private IEnumerator SparkRoutine(Vector3 position, int count)
        {
            var particles = new List<(GameObject Object, Vector3 Velocity)>();
            var random = new System.Random(count * 97 + Mathf.RoundToInt(position.sqrMagnitude * 10f));
            for (var index = 0; index < count; index++)
            {
                var spark = ProceduralFactory.Sphere("Spark", effectsRoot, position, Vector3.one * 0.055f, index % 2 == 0 ? roadMaterial : accentMaterial);
                var velocity = new Vector3(
                    ((float)random.NextDouble() - 0.5f) * 2.2f,
                    0.6f + (float)random.NextDouble() * 1.4f,
                    ((float)random.NextDouble() - 0.5f) * 2.2f);
                particles.Add((spark, velocity));
            }
            var elapsed = 0f;
            while (elapsed < 0.32f)
            {
                var delta = Time.unscaledDeltaTime;
                elapsed += delta;
                for (var index = 0; index < particles.Count; index++)
                {
                    var pair = particles[index];
                    pair.Velocity += Vector3.down * (4.2f * delta);
                    pair.Object.transform.localPosition += pair.Velocity * delta;
                    pair.Object.transform.localScale = Vector3.one * 0.055f * (1f - elapsed / 0.32f);
                    particles[index] = pair;
                }
                yield return null;
            }
            foreach (var particle in particles) if (particle.Object != null) Destroy(particle.Object);
        }

        private IEnumerator DustRoutine(Vector3 position, int count)
        {
            var particles = new List<(GameObject Object, Vector3 Velocity, float BaseScale)>();
            var random = new System.Random(count * 131 + Mathf.RoundToInt(position.sqrMagnitude * 17f));
            for (var index = 0; index < count; index++)
            {
                var scale = 0.1f + (float)random.NextDouble() * 0.14f;
                var dust = ProceduralFactory.Sphere(
                    "Dust",
                    effectsRoot,
                    position + new Vector3(
                        ((float)random.NextDouble() - 0.5f) * 0.45f,
                        (float)random.NextDouble() * 0.18f,
                        ((float)random.NextDouble() - 0.5f) * 0.45f),
                    Vector3.one * scale,
                    index % 2 == 0 ? environmentShadeMaterial : rockMaterial);
                var velocity = new Vector3(
                    ((float)random.NextDouble() - 0.5f) * 1.15f,
                    0.32f + (float)random.NextDouble() * 0.7f,
                    ((float)random.NextDouble() - 0.5f) * 1.15f);
                particles.Add((dust, velocity, scale));
            }
            var duration = reducedMotion ? 0.18f : 0.52f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                var delta = Time.unscaledDeltaTime;
                elapsed += delta;
                var remaining = 1f - Mathf.Clamp01(elapsed / duration);
                for (var index = 0; index < particles.Count; index++)
                {
                    var particle = particles[index];
                    particle.Velocity += Vector3.down * (0.65f * delta);
                    particle.Object.transform.localPosition += particle.Velocity * delta;
                    particle.Object.transform.localScale = Vector3.one * particle.BaseScale * Mathf.Lerp(0.28f, 1f, remaining);
                    particles[index] = particle;
                }
                yield return null;
            }
            foreach (var particle in particles) if (particle.Object != null) Destroy(particle.Object);
        }

        private IEnumerator ScalePieceRoutine(Transform target, bool placing, float duration)
        {
            if (target == null) yield break;
            var baseScale = target.localScale;
            var elapsed = 0f;
            if (placing) target.localScale = baseScale * 0.84f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                if (placing)
                {
                    var factor = t < 0.72f
                        ? Mathf.Lerp(0.84f, 1.045f, EaseOutCubic(t / 0.72f))
                        : Mathf.Lerp(1.045f, 1f, (t - 0.72f) / 0.28f);
                    target.localScale = baseScale * factor;
                }
                else
                {
                    target.localScale = baseScale * (1f - EaseInCubic(t));
                }
                yield return null;
            }
            if (target == null) yield break;
            if (placing) target.localScale = baseScale;
            else Destroy(target.gameObject);
        }

        private static float EaseOutCubic(float value)
        {
            var inverse = 1f - Mathf.Clamp01(value);
            return 1f - inverse * inverse * inverse;
        }

        private static float EaseInCubic(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * value;
        }

        private void CreateTrussGuide()
        {
            for (var cell = 0; cell < 5; cell++)
            {
                var from = new GridPoint(cell, 0);
                var to = new GridPoint(cell + 1, 0);
                var group = CreateTrussModel(from, to, true);
                group.name = "TrussGuide";
                group.transform.SetParent(levelRoot, false);
                ApplyGhostMaterial(group, true);
            }
        }

        private IReadOnlyList<GridPoint> CreateDefaultGridPath()
        {
            var path = new List<GridPoint> { level.StartAnchor };
            var cursor = level.StartAnchor;
            while (cursor.X != level.GoalAnchor.X)
            {
                cursor = new GridPoint(cursor.X + Math.Sign(level.GoalAnchor.X - cursor.X), cursor.Y + Math.Sign(level.GoalAnchor.Y - cursor.Y), cursor.Z);
                path.Add(cursor);
            }
            while (cursor.Z != level.GoalAnchor.Z)
            {
                cursor = new GridPoint(cursor.X, cursor.Y + Math.Sign(level.GoalAnchor.Y - cursor.Y), cursor.Z + Math.Sign(level.GoalAnchor.Z - cursor.Z));
                path.Add(cursor);
            }
            return path;
        }

        private void SetVehiclePath(IReadOnlyList<Vector3> points)
        {
            vehiclePath.Clear();
            vehiclePath.AddRange(points);
            vehiclePathDistances.Clear();
            vehiclePathDistances.Add(0f);
            vehiclePathLength = 0f;
            for (var index = 1; index < vehiclePath.Count; index++)
            {
                vehiclePathLength += Vector3.Distance(vehiclePath[index - 1], vehiclePath[index]);
                vehiclePathDistances.Add(vehiclePathLength);
            }
        }

        private Vector3 SampleVehiclePath(float distance)
        {
            var clamped = Mathf.Clamp(distance, 0f, vehiclePathLength);
            var segment = Mathf.Max(0, vehiclePathDistances.Count - 2);
            for (var index = 0; index < vehiclePathDistances.Count - 1; index++)
            {
                if (clamped <= vehiclePathDistances[index + 1])
                {
                    segment = index;
                    break;
                }
            }
            var startDistance = vehiclePathDistances[segment];
            var endDistance = vehiclePathDistances[segment + 1];
            var t = Mathf.Approximately(startDistance, endDistance) ? 0f : (clamped - startDistance) / (endDistance - startDistance);
            return Vector3.Lerp(vehiclePath[segment], vehiclePath[segment + 1], t);
        }

        private int FindWaypointIndex(float progress)
        {
            var distance = Mathf.Clamp01(progress) * vehiclePathLength;
            var index = 0;
            for (var candidate = 1; candidate < vehiclePathDistances.Count; candidate++)
            {
                if (distance >= vehiclePathDistances[candidate]) index = candidate;
                else break;
            }
            return index;
        }

        private Vector3 GridDirection(GridPoint from, GridPoint to)
        {
            var direction = new Vector3(to.X - from.X, 0f, to.Z - from.Z);
            return direction.sqrMagnitude > 0.001f ? direction.normalized : DockDirection();
        }

        private Vector3 DockDirection()
        {
            var deltaX = level.GoalAnchor.X - level.StartAnchor.X;
            var deltaZ = level.GoalAnchor.Z - level.StartAnchor.Z;
            return Mathf.Abs(deltaX) >= Mathf.Abs(deltaZ) && deltaX != 0
                ? Vector3.right * Math.Sign(deltaX)
                : Vector3.forward * (deltaZ == 0 ? 1 : Math.Sign(deltaZ));
        }

        private void GetDockDirections(out Vector3 startDirection, out Vector3 endDirection)
        {
            var path = BridgeRules.FindRoadPath(
                level.ReferenceSolution.Layout.Roads,
                level.StartAnchor,
                level.GoalAnchor);
            if (path != null && path.Nodes.Count >= 2)
            {
                startDirection = GridDirection(path.Nodes[0], path.Nodes[1]);
                endDirection = GridDirection(path.Nodes[^2], path.Nodes[^1]);
                return;
            }

            startDirection = DockDirection();
            endDirection = startDirection;
        }

        private BuildVolume GetBuildVolume()
        {
            if (level.BuildVolume != null) return level.BuildVolume;
            return new BuildVolume(
                new GridPoint(Math.Min(level.StartAnchor.X, level.GoalAnchor.X), Math.Min(level.StartAnchor.Y, level.GoalAnchor.Y), Math.Min(level.StartAnchor.Z, level.GoalAnchor.Z)),
                new GridPoint(Math.Max(level.StartAnchor.X, level.GoalAnchor.X), Math.Max(level.StartAnchor.Y, level.GoalAnchor.Y), Math.Max(level.StartAnchor.Z, level.GoalAnchor.Z)),
                -2);
        }

        private Bounds CalculateBounds(GameObject gameObject)
        {
            var renderers = gameObject.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(gameObject.transform.position, Vector3.one);
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private Vector3 GetContentPosition(Transform target)
        {
            return target == null ? Vector3.zero : WorldToContentPoint(target.position);
        }

        private static Bounds CalculateBoundsInSpace(GameObject gameObject, Transform space)
        {
            var renderers = gameObject.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(space.InverseTransformPoint(gameObject.transform.position), Vector3.one);
            }

            var worldBounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++) worldBounds.Encapsulate(renderers[index].bounds);
            return ConvertWorldBoundsToSpace(worldBounds, space);
        }

        private static Bounds CalculateBoxColliderBoundsInSpace(BoxCollider collider, Transform space)
        {
            var halfSize = collider.size * 0.5f;
            var center = collider.center;
            var localBounds = new Bounds();
            var initialized = false;
            foreach (var x in new[] { -1f, 1f })
            {
                foreach (var y in new[] { -1f, 1f })
                {
                    foreach (var z in new[] { -1f, 1f })
                    {
                        var corner = center + Vector3.Scale(halfSize, new Vector3(x, y, z));
                        var contentCorner = space.InverseTransformPoint(collider.transform.TransformPoint(corner));
                        if (!initialized)
                        {
                            localBounds = new Bounds(contentCorner, Vector3.zero);
                            initialized = true;
                        }
                        else
                        {
                            localBounds.Encapsulate(contentCorner);
                        }
                    }
                }
            }
            return localBounds;
        }

        private static Bounds CalculateLocalBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
            var worldBounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++) worldBounds.Encapsulate(renderers[index].bounds);
            return ConvertWorldBoundsToSpace(worldBounds, root);
        }

        private static Bounds ConvertWorldBoundsToSpace(Bounds worldBounds, Transform space)
        {
            var corners = new Vector3[8];
            var min = worldBounds.min;
            var max = worldBounds.max;
            corners[0] = new Vector3(min.x, min.y, min.z);
            corners[1] = new Vector3(min.x, min.y, max.z);
            corners[2] = new Vector3(min.x, max.y, min.z);
            corners[3] = new Vector3(min.x, max.y, max.z);
            corners[4] = new Vector3(max.x, min.y, min.z);
            corners[5] = new Vector3(max.x, min.y, max.z);
            corners[6] = new Vector3(max.x, max.y, min.z);
            corners[7] = new Vector3(max.x, max.y, max.z);
            var local = new Bounds(space.InverseTransformPoint(corners[0]), Vector3.zero);
            for (var index = 1; index < corners.Length; index++) local.Encapsulate(space.InverseTransformPoint(corners[index]));
            return local;
        }

        private void OnDestroy()
        {
            StopSimulation();
            ClearIslandOcclusionStates();
            foreach (var material in materials) if (material != null) Destroy(material);
        }

        private sealed class IslandOcclusionState
        {
            private readonly Renderer[] renderers;
            private readonly Material[][] originals;
            private readonly Material[][] faded;
            private bool? occluded;

            public IslandOcclusionState(Transform island, Renderer[] renderers, Material[][] originals, Material[][] faded)
            {
                Island = island;
                this.renderers = renderers;
                this.originals = originals;
                this.faded = faded;
            }

            public Transform Island { get; }

            public void SetOccluded(bool value)
            {
                if (occluded == value) return;
                occluded = value;
                for (var index = 0; index < renderers.Length; index++)
                {
                    if (renderers[index] != null) renderers[index].sharedMaterials = value ? faded[index] : originals[index];
                }
            }

            public void Dispose()
            {
                for (var rendererIndex = 0; rendererIndex < faded.Length; rendererIndex++)
                {
                    foreach (var material in faded[rendererIndex]) if (material != null) UnityEngine.Object.Destroy(material);
                }
            }
        }
    }
}
