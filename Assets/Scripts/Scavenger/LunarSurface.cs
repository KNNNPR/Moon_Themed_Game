using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoonThemedGame.Scavenger
{
    // Procedural lunar surface around the station; the station floor is ground level.
    // Everything scales with `unit` (the robot's length). Slopes stay gentle and obstacles
    // are sparse with wide gaps, so every point of the patch can be reached.
    public sealed class LunarSurface : MonoBehaviour
    {
        struct Crater { public Vector2 center; public float radius, depth; }

        public int seed;
        public float unit = 1;
        [Tooltip("Side of the square patch, in robot lengths.")]
        public float sizeInUnits = 140;
        [Range(32, 200)] public int cells = 128;
        [Tooltip("Flat ground around the station, in world units.")]
        public float flatRadius = 20;

        readonly List<Crater> craters = new List<Crater>();
        readonly List<Vector2> occupied = new List<Vector2>();
        System.Random random;
        float noiseX, noiseZ;
        Material ground, rock, metal;

        public float Size => sizeInUnits * unit;
        public float HalfSize => Size * 0.5f;
        public static LunarSurface Create(Vector3 origin, float unit, float flatRadius, int seed)
        {
            var surface = new GameObject("Lunar Surface").AddComponent<LunarSurface>();
            surface.transform.position = origin;
            surface.unit = unit;
            surface.flatRadius = flatRadius;
            surface.sizeInUnits = Mathf.Max(surface.sizeInUnits, flatRadius / unit * 5);
            surface.seed = seed != 0 ? seed : Random.Range(1, int.MaxValue);
            surface.Generate();
            return surface;
        }

        float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        public void Generate()
        {
            random = new System.Random(seed);
            noiseX = Range(0, 1000);
            noiseZ = Range(0, 1000);
            ground = MakeMaterial(new Color(0.2f, 0.2f, 0.21f));
            rock = MakeMaterial(new Color(0.13f, 0.13f, 0.13f));
            metal = MakeMaterial(new Color(0.3f, 0.27f, 0.23f));
            
            craters.Clear();
            int count = Mathf.RoundToInt(sizeInUnits * 0.18f);
            for (int i = 0; i < count; i++)
            {
                float radius = Range(3, 11) * unit;
                var center = new Vector2(Range(-HalfSize, HalfSize), Range(-HalfSize, HalfSize));
                if (center.magnitude < flatRadius + radius * 1.5f) continue;
                craters.Add(new Crater { center = center, radius = radius, depth = radius * Range(0.12f, 0.2f) });
            }

            BuildGround();
            occupied.Clear();
            ScatterRocks();
            BuildShelters();
        }

        // Local-space height. Gentle noise, shallow bowl craters with soft rims, flat ground
        // at the centre and a raised rim at the edge of the patch.
        public float Height(float x, float z)
        {
            float h = 0;
            h += (Mathf.PerlinNoise(noiseX + x / (30 * unit), noiseZ + z / (30 * unit)) - 0.5f) * 2.2f * unit;
            h += (Mathf.PerlinNoise(noiseX + x / (9 * unit), noiseZ + z / (9 * unit)) - 0.5f) * 0.5f * unit;
            foreach (var c in craters)
            {
                float d = Vector2.Distance(new Vector2(x, z), c.center) / c.radius;
                if (d < 1) h -= c.depth * (1 - d * d);
                float rim = d - 1;
                h += c.depth * 0.35f * Mathf.Exp(-rim * rim * 10);
            }
            float flat = Mathf.SmoothStep(1, 0, Mathf.InverseLerp(flatRadius, flatRadius + 12 * unit, new Vector2(x, z).magnitude));
            h = Mathf.Lerp(h, 0, flat);
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            h += Mathf.SmoothStep(0, 1, Mathf.InverseLerp(HalfSize * 0.85f, HalfSize, edge)) * 6 * unit;
            return h;
        }

        public float WorldHeight(Vector3 world)
        {
            var local = transform.InverseTransformPoint(world);
            return transform.TransformPoint(new Vector3(local.x, Height(local.x, local.z), local.z)).y;
        }

        public Vector3 WorldNormal(Vector3 world)
        {
            var local = transform.InverseTransformPoint(world);
            float e = unit * 0.5f;
            float hx = Height(local.x + e, local.z) - Height(local.x - e, local.z);
            float hz = Height(local.x, local.z + e) - Height(local.x, local.z - e);
            return transform.TransformDirection(new Vector3(-hx, 2 * e, -hz).normalized);
        }

        public Vector3 ClampInside(Vector3 world, float margin)
        {
            var local = transform.InverseTransformPoint(world);
            float limit = HalfSize * 0.85f - margin;
            local.x = Mathf.Clamp(local.x, -limit, limit);
            local.z = Mathf.Clamp(local.z, -limit, limit);
            return transform.TransformPoint(local);
        }

        void BuildGround()
        {
            float step = Size / cells;
            var vertices = new List<Vector3>(cells * cells * 6);
            var colors = new List<Color>(cells * cells * 6);
            for (int zi = 0; zi < cells; zi++)
            for (int xi = 0; xi < cells; xi++)
            {
                float x0 = -HalfSize + xi * step, z0 = -HalfSize + zi * step;
                var a = Point(x0, z0);
                var b = Point(x0, z0 + step);
                var c = Point(x0 + step, z0 + step);
                var d = Point(x0 + step, z0);
                float shade = 0.92f + 0.08f * Mathf.PerlinNoise(noiseX + xi * 0.31f, noiseZ + zi * 0.31f);
                var tint = new Color(shade, shade, shade);
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                vertices.Add(a); vertices.Add(c); vertices.Add(d);
                for (int k = 0; k < 6; k++) colors.Add(tint);
            }
            var triangles = new int[vertices.Count];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = i;
            var mesh = new Mesh { name = "Lunar Surface", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var groundObject = new GameObject("Ground");
            groundObject.transform.SetParent(transform, false);
            groundObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            groundObject.AddComponent<MeshRenderer>().sharedMaterial = ground;
            groundObject.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        Vector3 Point(float x, float z) => new Vector3(x, Height(x, z), z);

        bool TryPlace(float clearance, out Vector2 spot)
        {
            float pad = flatRadius + 4 * unit;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                spot = new Vector2(Range(-HalfSize * 0.82f, HalfSize * 0.82f), Range(-HalfSize * 0.82f, HalfSize * 0.82f));
                if (spot.magnitude < pad + clearance) continue;
                bool free = true;
                foreach (var other in occupied)
                    if (Vector2.Distance(other, spot) < clearance + 3 * unit) { free = false; break; }
                if (!free) continue;
                occupied.Add(spot);
                return true;
            }
            spot = default;
            return false;
        }

        // Rocks never touch each other: each keeps at least three robot lengths of free ground around it.
        void ScatterRocks()
        {
            int count = Mathf.RoundToInt(sizeInUnits * 0.5f);
            for (int i = 0; i < count; i++)
            {
                float size = Range(0.3f, 1.4f) * unit;
                if (!TryPlace(size, out var spot)) continue;
                var go = new GameObject("Rock");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(spot.x, Height(spot.x, spot.y) - size * 0.25f, spot.y);
                go.transform.localRotation = Quaternion.Euler(Range(0, 360), Range(0, 360), Range(0, 360));
                go.transform.localScale = new Vector3(size, size * Range(0.5f, 0.9f), size * Range(0.8f, 1.2f));
                var mesh = RockMesh();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = rock;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
        }

        // Dumped junk with a roof: the robot fits underneath, and loses sky view there.
        void BuildShelters()
        {
            int count = Mathf.Max(3, Mathf.RoundToInt(sizeInUnits / 25));
            for (int i = 0; i < count; i++)
            {
                float width = Range(3f, 4f) * unit, depth = Range(2.5f, 4f) * unit, height = Range(1.8f, 2.4f) * unit;
                if (!TryPlace(Mathf.Max(width, depth), out var spot)) continue;
                var root = new GameObject("Shelter").transform;
                root.SetParent(transform, false);
                root.localPosition = new Vector3(spot.x, Height(spot.x, spot.y), spot.y);
                root.localRotation = Quaternion.Euler(0, Range(0, 360), 0);
                float leg = 0.25f * unit;
                foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                {
                    var local = new Vector3(corner.x * (width * 0.5f - leg), 0, corner.y * (depth * 0.5f - leg));
                    var world = root.TransformPoint(local);
                    float ground = WorldHeight(world) - root.position.y;
                    float tall = height - ground + 0.3f * unit;
                    Piece(PrimitiveType.Cube, new Vector3(local.x, ground + tall * 0.5f - 0.3f * unit, local.z), new Vector3(leg * 2, tall, leg * 2), Quaternion.identity, metal, "Leg").transform.SetParent(root, false);
                }
                var roof = Piece(PrimitiveType.Cube, new Vector3(0, height + 0.12f * unit, 0), new Vector3(width, 0.25f * unit, depth), Quaternion.Euler(0, 0, Range(-4, 4)), metal, "Roof");
                roof.transform.SetParent(root, false);
            }
        }

        GameObject Piece(PrimitiveType shape, Vector3 localPosition, Vector3 scale, Quaternion rotation, Material material, string name)
        {
            var piece = GameObject.CreatePrimitive(shape);
            piece.name = name;
            piece.transform.SetParent(transform, false);
            piece.transform.localPosition = localPosition;
            piece.transform.localRotation = rotation;
            piece.transform.localScale = scale;
            piece.GetComponent<Renderer>().sharedMaterial = material;
            if (shape == PrimitiveType.Cylinder) Destroy(piece.GetComponent<Collider>());
            return piece;
        }

        Mesh RockMesh()
        {
            float t = (1 + Mathf.Sqrt(5)) / 2;
            var points = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1)
            };
            for (int i = 0; i < points.Count; i++) points[i] = points[i].normalized * 0.5f * Range(0.8f, 1.15f);
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            var vertices = new Vector3[faces.Length];
            var triangles = new int[faces.Length];
            for (int i = 0; i < faces.Length; i += 3)
            {
                Vector3 a = points[faces[i]], b = points[faces[i + 1]], c = points[faces[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0) (b, c) = (c, b);
                vertices[i] = a; vertices[i + 1] = b; vertices[i + 2] = c;
                triangles[i] = i; triangles[i + 1] = i + 1; triangles[i + 2] = i + 2;
            }
            var mesh = new Mesh { name = "Rock", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Material MakeMaterial(Color color)
        {
            var source = Resources.Load<Material>("LunarSurface");
            Material material;
            if (source) material = new Material(source);
            else
            {
                var shader = Shader.Find("Old Tech/Retro Cel Lit");
                material = new Material(shader ? shader : Shader.Find("Universal Render Pipeline/Lit"));
            }
            material.SetColor("_BaseColor", color);
            return material;
        }
    }
}
