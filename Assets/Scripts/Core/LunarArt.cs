using UnityEngine;
using UnityEngine.Rendering;

namespace Rovernauts
{
    public static class LunarArt
    {
        public static readonly Color Alloy = new Color(0.76f, 0.84f, 0.87f);
        public static readonly Color Frame = new Color(0.075f, 0.115f, 0.16f);
        public static readonly Color Orange = new Color(1f, 0.47f, 0.13f);
        public static readonly Color Cyan = new Color(0.28f, 0.9f, 1f);
        static Mesh rockMesh, armorMesh;

        public static GameObject Armor(Transform parent, Vector3 position, Vector3 dimensions, Color color)
        {
            if (armorMesh == null) armorMesh = CreateArmorMesh();
            return PlaceMesh("Chamfered armor", armorMesh, parent, position, dimensions, color);
        }

        public static GameObject Rock(Transform parent, Vector3 position, Vector3 dimensions, Color color)
        {
            if (rockMesh == null) rockMesh = CreateRockMesh();
            return PlaceMesh("Basalt", rockMesh, parent, position, dimensions, color);
        }

        public static GameObject LightStrip(Transform parent, Vector3 position, Vector3 scale, Color color)
        {
            var part = Visuals.Part(PrimitiveType.Cube, parent, position, scale, color, castShadows: false);
            part.GetComponent<Renderer>().sharedMaterial = Visuals.Glow(color);
            return part;
        }

        public static void BuildLandingPad(Transform world)
        {
            var pad = new GameObject("Survey station").transform;
            pad.SetParent(world, false);
            Visuals.Part(PrimitiveType.Cylinder, pad, Vector3.up * 0.02f,
                new Vector3(13f, 0.02f, 13f), new Color(0.19f, 0.23f, 0.29f), castShadows: false);
            var rim = Visuals.Line(pad, Orange, 0.12f);
            rim.sharedMaterial = Visuals.Glow(Orange);
            rim.loop = true;
            rim.positionCount = 12;
            for (int i = 0; i < 12; i++)
            {
                float a = (i + 0.5f) * Mathf.PI / 6f;
                rim.SetPosition(i, new Vector3(Mathf.Cos(a) * 6.1f, 0.055f, Mathf.Sin(a) * 6.1f));
            }
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = -1; i <= 1; i++)
                    LightStrip(pad, new Vector3(side * 5f, 0.06f, 1.15f * i), new Vector3(0.25f, 0.04f, 0.6f), Cyan);

                var station = new GameObject("Survey equipment").transform;
                station.SetParent(pad, false);
                station.localPosition = new Vector3(side * 7.5f, 0f, -6f);
                var obstacle = station.gameObject.AddComponent<BoxCollider>();
                obstacle.center = Vector3.up * 0.6f;
                obstacle.size = new Vector3(2.8f, 1.2f, 1.8f);
                station.gameObject.AddComponent<Obstacle>();
                Armor(station, Vector3.up * 0.55f, new Vector3(2.8f, 1.1f, 1.8f), Frame);
                Armor(station, Vector3.up * 1.1f, new Vector3(2.65f, 0.25f, 1.7f), Alloy);
                LightStrip(station, new Vector3(0f, 1.26f, 0.5f), new Vector3(1.6f, 0.04f, 0.16f), Cyan);
                Armor(station, new Vector3(0.85f, 2f, 0f), new Vector3(0.12f, 1.65f, 0.12f), Alloy);
                LightStrip(station, new Vector3(0.85f, 2.9f, 0f), Vector3.one * 0.17f, Orange);
            }
        }

        static GameObject PlaceMesh(string name, Mesh mesh, Transform parent, Vector3 position, Vector3 scale, Color color)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = Visuals.Mat(color);
            return part;
        }

        static Mesh CreateArmorMesh()
        {
            var outline = new[]
            {
                new Vector2(-0.5f, -0.32f), new Vector2(-0.32f, -0.5f),
                new Vector2(0.32f, -0.5f), new Vector2(0.5f, -0.32f),
                new Vector2(0.5f, 0.32f), new Vector2(0.32f, 0.5f),
                new Vector2(-0.32f, 0.5f), new Vector2(-0.5f, 0.32f),
            };
            return BuildLathe("Armor", outline, new[] { -0.5f, -0.28f, 0.29f, 0.5f },
                new[] { 0.8f, 1f, 1f, 0.82f });
        }

        static Mesh CreateRockMesh()
        {
            var outline = new[]
            {
                new Vector2(-0.5f, -0.16f), new Vector2(-0.3f, -0.47f),
                new Vector2(0.12f, -0.5f), new Vector2(0.48f, -0.21f),
                new Vector2(0.5f, 0.19f), new Vector2(0.3f, 0.48f),
                new Vector2(-0.19f, 0.49f), new Vector2(-0.48f, 0.24f),
            };
            return BuildLathe("Basalt", outline, new[] { -0.44f, -0.23f, 0.18f, 0.5f },
                new[] { 0.72f, 1f, 0.86f, 0.43f }, rough: true);
        }

        static Mesh BuildLathe(string name, Vector2[] outline, float[] heights, float[] widths, bool rough = false)
        {
            var points = new Vector3[heights.Length, outline.Length];
            for (int layer = 0; layer < heights.Length; layer++)
            for (int i = 0; i < outline.Length; i++)
            {
                float shift = rough ? Mathf.Sin(i * 2.1f + layer) * 0.085f : 0f;
                points[layer, i] = new Vector3(outline[i].x * widths[layer] + shift,
                    heights[layer] + shift, outline[i].y * widths[layer]);
            }

            var vertices = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < outline.Length; i++)
            {
                int next = (i + 1) % outline.Length;
                for (int row = 0; row < heights.Length - 1; row++)
                {
                    vertices.Add(points[row, i]);
                    vertices.Add(points[row + 1, i]);
                    vertices.Add(points[row + 1, next]);
                    vertices.Add(points[row, i]);
                    vertices.Add(points[row + 1, next]);
                    vertices.Add(points[row, next]);
                }
                int top = heights.Length - 1;
                vertices.Add(Vector3.up * heights[top]);
                vertices.Add(points[top, next]);
                vertices.Add(points[top, i]);
            }
            var triangles = new int[vertices.Count];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = i;
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
