using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    // Faceted low-poly tubes for the player's suit: rings stacked along Y, flat shaded.
    public static class SuitMesh
    {
        public readonly struct Ring
        {
            public readonly float y, rx, rz, cx, cz;
            public Ring(float y, float rx, float rz, float cx = 0, float cz = 0)
            { this.y = y; this.rx = rx; this.rz = rz; this.cx = cx; this.cz = cz; }
        }

        public static Mesh Lathe(string name, int sides, params Ring[] rings)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            Vector3 Point(Ring ring, int side)
            {
                float angle = (side % sides) * Mathf.PI * 2 / sides + Mathf.PI / sides;
                return new Vector3(ring.cx + Mathf.Sin(angle) * ring.rx, ring.y, ring.cz + Mathf.Cos(angle) * ring.rz);
            }
            void Face(params Vector3[] corners)
            {
                int start = vertices.Count;
                vertices.AddRange(corners);
                for (int i = 1; i < corners.Length - 1; i++) triangles.AddRange(new[] { start, start + i, start + i + 1 });
            }
            for (int r = 0; r < rings.Length - 1; r++)
            {
                var a = rings[r];
                var b = rings[r + 1];
                bool down = b.y < a.y;
                for (int s = 0; s < sides; s++)
                {
                    var a0 = Point(a, s); var a1 = Point(a, s + 1);
                    var b0 = Point(b, s); var b1 = Point(b, s + 1);
                    if (down) Face(a0, b0, b1, a1); else Face(a0, a1, b1, b0);
                }
            }
            Cap(rings[0], rings.Length > 1 && rings[1].y < rings[0].y);
            Cap(rings[rings.Length - 1], !(rings.Length > 1 && rings[1].y < rings[0].y));
            void Cap(Ring ring, bool top)
            {
                if (ring.rx < 1e-4f) return;
                var corners = new Vector3[sides];
                for (int s = 0; s < sides; s++) corners[s] = Point(ring, top ? s : sides - s);
                Face(corners);
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
