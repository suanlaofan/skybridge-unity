using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skybridge.Presentation
{
    internal static class ProceduralFactory
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Metallic = Shader.PropertyToID("_Metallic");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        public static Material Material(
            string name,
            Color color,
            float metallic,
            float smoothness,
            Color? emission = null,
            bool transparent = false)
        {
            var shaderAnchor = Resources.Load<Material>("SkybridgeShaderAnchor");
            var shader = shaderAnchor != null && shaderAnchor.shader != null
                ? shaderAnchor.shader
                : Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new System.InvalidOperationException("Skybridge requires a retained Lit shader.");
            var material = new Material(shader) { name = name };
            if (material.HasProperty(BaseColor)) material.SetColor(BaseColor, color);
            else material.color = color;
            if (material.HasProperty(Metallic)) material.SetFloat(Metallic, metallic);
            if (material.HasProperty(Smoothness)) material.SetFloat(Smoothness, smoothness);
            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                if (material.HasProperty(EmissionColor)) material.SetColor(EmissionColor, emission.Value);
            }
            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            return material;
        }

        public static GameObject Box(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Material material,
            bool collider = false)
        {
            var gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            gameObject.transform.localScale = size;
            SetMaterial(gameObject, material);
            ConfigureCollider(gameObject, collider);
            ConfigureShadows(gameObject);
            return gameObject;
        }

        public static GameObject Cylinder(
            string name,
            Transform parent,
            Vector3 localPosition,
            float radius,
            float height,
            Material material,
            int sides = 16,
            bool collider = false)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateCylinderMesh(radius, height, Mathf.Max(3, sides));
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            ConfigureShadows(gameObject);
            if (collider)
            {
                var cylinderCollider = gameObject.AddComponent<CapsuleCollider>();
                cylinderCollider.radius = radius;
                cylinderCollider.height = height;
            }
            return gameObject;
        }

        public static GameObject Sphere(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 scale,
            Material material,
            bool collider = false)
        {
            var gameObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            gameObject.transform.localScale = scale;
            SetMaterial(gameObject, material);
            ConfigureCollider(gameObject, collider);
            ConfigureShadows(gameObject);
            return gameObject;
        }

        public static GameObject FlatRock(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 scale,
            Material material,
            int variant)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            gameObject.transform.localScale = scale;
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateFlatRockMesh(variant);
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            ConfigureShadows(gameObject);
            return gameObject;
        }

        public static GameObject Beam(
            string name,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float radius,
            Material material)
        {
            var delta = end - start;
            var beam = Cylinder(name, parent, (start + end) * 0.5f, radius, delta.magnitude, material, 8);
            beam.transform.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
            return beam;
        }

        public static GameObject FrameBox(
            string name,
            Transform parent,
            Vector3 size,
            Material material,
            float thickness = 0.035f)
        {
            var frame = new GameObject(name);
            frame.transform.SetParent(parent, false);
            var halfX = size.x * 0.5f;
            var halfZ = size.z * 0.5f;
            Box("Front", frame.transform, new Vector3(0f, 0f, halfZ), new Vector3(size.x, thickness, thickness), material);
            Box("Rear", frame.transform, new Vector3(0f, 0f, -halfZ), new Vector3(size.x, thickness, thickness), material);
            Box("Left", frame.transform, new Vector3(-halfX, 0f, 0f), new Vector3(thickness, thickness, size.z), material);
            Box("Right", frame.transform, new Vector3(halfX, 0f, 0f), new Vector3(thickness, thickness, size.z), material);
            return frame;
        }

        public static void DestroyChildren(Transform parent)
        {
            for (var index = parent.childCount - 1; index >= 0; index--)
            {
                var child = parent.GetChild(index).gameObject;
                if (Application.isPlaying) Object.Destroy(child);
                else Object.DestroyImmediate(child);
            }
        }

        private static void ConfigureShadows(GameObject gameObject)
        {
            var renderer = gameObject.GetComponent<Renderer>();
            if (renderer == null) return;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        private static void SetMaterial(GameObject gameObject, Material material)
        {
            var renderer = gameObject.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }

        private static void ConfigureCollider(GameObject gameObject, bool enabled)
        {
            var collider = gameObject.GetComponent<Collider>();
            if (collider != null && !enabled)
            {
                if (Application.isPlaying) Object.Destroy(collider);
                else Object.DestroyImmediate(collider);
            }
        }

        private static Mesh CreateCylinderMesh(float radius, float height, int sides)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var half = height * 0.5f;
            for (var side = 0; side < sides; side++)
            {
                var next = (side + 1) % sides;
                var angle = side * Mathf.PI * 2f / sides;
                var nextAngle = next * Mathf.PI * 2f / sides;
                var a = new Vector3(Mathf.Cos(angle) * radius, -half, Mathf.Sin(angle) * radius);
                var b = new Vector3(Mathf.Cos(angle) * radius, half, Mathf.Sin(angle) * radius);
                var c = new Vector3(Mathf.Cos(nextAngle) * radius, half, Mathf.Sin(nextAngle) * radius);
                var d = new Vector3(Mathf.Cos(nextAngle) * radius, -half, Mathf.Sin(nextAngle) * radius);
                AddQuad(vertices, triangles, a, b, c, d);
                AddTriangle(vertices, triangles, Vector3.down * half, d, a);
                AddTriangle(vertices, triangles, Vector3.up * half, b, c);
            }
            var mesh = new Mesh { name = $"Cylinder_{sides}" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateFlatRockMesh(int variant)
        {
            const int sides = 10;
            var random = new System.Random(481 + variant * 97);
            var top = new Vector3[sides];
            var waist = new Vector3[sides];
            var bottom = new Vector3[sides];
            for (var index = 0; index < sides; index++)
            {
                var angle = index * Mathf.PI * 2f / sides;
                var jitter = 0.88f + (float)random.NextDouble() * 0.22f;
                top[index] = new Vector3(Mathf.Cos(angle) * 0.82f * jitter, 0.55f, Mathf.Sin(angle) * 0.82f * jitter);
                waist[index] = new Vector3(Mathf.Cos(angle) * jitter, 0.05f, Mathf.Sin(angle) * jitter);
                bottom[index] = new Vector3(Mathf.Cos(angle) * 0.48f * jitter, -0.8f, Mathf.Sin(angle) * 0.48f * jitter);
            }
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var index = 0; index < sides; index++)
            {
                var next = (index + 1) % sides;
                AddQuad(vertices, triangles, top[index], waist[index], waist[next], top[next]);
                AddQuad(vertices, triangles, waist[index], bottom[index], bottom[next], waist[next]);
                AddTriangle(vertices, triangles, Vector3.up * 0.62f, top[next], top[index]);
                AddTriangle(vertices, triangles, Vector3.down * 0.92f, bottom[index], bottom[next]);
            }
            var mesh = new Mesh { name = $"FlatRock_{variant}" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        private static void AddTriangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
        {
            var start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }
    }
}
