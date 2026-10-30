using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rovernauts
{
    /// <summary>
    /// Builds the moon arena from primitives: a floating plateau with craters, rocks and ridges
    /// (the obstacles enemies must path around), invisible boundary walls, and a starfield below.
    /// The layout is seeded so every run plays on the same map.
    /// </summary>
    public static class WorldBuilder
    {
        public const float HalfSize = 50f;
        public static Light Sun { get; private set; }
        public static Renderer Ground { get; private set; }

        public static void Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("World").transform;

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(root);
            sunGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            Sun = sunGo.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.intensity = 1.15f;
            Sun.color = new Color(0.9f, 0.93f, 1f);
            Sun.shadows = LightShadows.Soft;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.28f, 0.29f, 0.38f);

            var ground = Visuals.Part(PrimitiveType.Cube, root, new Vector3(0f, -2f, 0f), new Vector3(HalfSize * 2f, 4f, HalfSize * 2f),
                new Color(0.43f, 0.42f, 0.46f), keepCollider: true);
            ground.name = "Moon Surface";
            Ground = ground.GetComponent<Renderer>();

            // Craters: decoration only, enemies and the rover drive straight over them.
            var rim = new Color(0.52f, 0.5f, 0.54f);
            var floor = new Color(0.32f, 0.31f, 0.35f);
            for (int i = 0; i < 16; i++)
            {
                Vector3 p = RandomPoint(rng, 4f, HalfSize - 6f);
                float d = Range(rng, 4f, 11f);
                Visuals.Part(PrimitiveType.Cylinder, root, p.WithY(0.02f), new Vector3(d * 1.2f, 0.03f, d * 1.2f), rim, castShadows: false).name = "Crater";
                Visuals.Part(PrimitiveType.Cylinder, root, p.WithY(0.05f), new Vector3(d, 0.03f, d), floor, castShadows: false).name = "Crater Floor";
            }

            var placed = new List<(Vector3 pos, float radius)>();
            // Ridges: long walls that force real detours (the reason A* is needed).
            for (int i = 0, tries = 0; i < 6 && tries < 200; tries++)
            {
                Vector3 p = RandomPoint(rng, 16f, HalfSize - 12f);
                float length = Range(rng, 10f, 16f);
                if (Overlaps(placed, p, length * 0.5f + 3f)) continue;
                var ridge = Visuals.Part(PrimitiveType.Cube, root, p.WithY(1.1f), new Vector3(length, 2.2f, 2.4f), new Color(0.36f, 0.34f, 0.37f), keepCollider: true);
                ridge.name = "Ridge";
                ridge.transform.rotation = Quaternion.Euler(0f, Range(rng, 0f, 180f), 0f);
                ridge.AddComponent<Obstacle>();
                placed.Add((p, length * 0.5f));
                i++;
            }
            // Rocks: scattered boulders, kept clear of the spawn ring and the rover's start.
            for (int i = 0, tries = 0; i < 28 && tries < 400; tries++)
            {
                Vector3 p = RandomPoint(rng, 12f, HalfSize - 8f);
                float s = Range(rng, 2.2f, 5.5f);
                if (Overlaps(placed, p, s * 0.5f + 3f)) continue;
                MakeRock(root, p, s, rng);
                placed.Add((p, s * 0.5f));
                i++;
            }

            // Invisible boundary walls.
            for (int i = 0; i < 4; i++)
            {
                bool xAxis = i < 2;
                float sign = i % 2 == 0 ? 1f : -1f;
                var wall = Visuals.Part(PrimitiveType.Cube, root,
                    xAxis ? new Vector3(sign * (HalfSize + 1f), 3f, 0f) : new Vector3(0f, 3f, sign * (HalfSize + 1f)),
                    xAxis ? new Vector3(2f, 6f, HalfSize * 2f + 4f) : new Vector3(HalfSize * 2f + 4f, 6f, 2f), Color.black, keepCollider: true);
                wall.name = "Boundary";
                wall.GetComponent<Renderer>().enabled = false;
                wall.AddComponent<Obstacle>();
            }

            // Space below and around the plateau.
            var stars = new GameObject("Starfield").transform;
            stars.SetParent(root);
            for (int i = 0; i < 450; i++)
            {
                var p = new Vector3(Range(rng, -320f, 320f), Range(rng, -140f, -25f), Range(rng, -200f, 380f));
                float size = Range(rng, 0.3f, 1.3f);
                var tint = rng.NextDouble() < 0.2 ? new Color(0.7f, 0.8f, 1f) : Color.white;
                Visuals.Part(PrimitiveType.Cube, stars, p, Vector3.one * size, tint, castShadows: false);
            }
            Visuals.Part(PrimitiveType.Sphere, root, new Vector3(150f, -100f, 260f), Vector3.one * 120f, new Color(0.3f, 0.45f, 0.75f), castShadows: false).name = "Distant Planet";
            Visuals.Part(PrimitiveType.Sphere, root, new Vector3(-170f, -70f, 200f), Vector3.one * 30f, new Color(0.75f, 0.6f, 0.45f), castShadows: false).name = "Distant Moon";
        }

        static void MakeRock(Transform root, Vector3 p, float s, System.Random rng)
        {
            float shade = Range(rng, 0.3f, 0.45f);
            var color = new Color(shade + 0.04f, shade, shade - 0.02f);
            // Uniform XZ scale keeps the sphere collider matching the visible footprint.
            var rock = Visuals.Part(PrimitiveType.Sphere, root, p.WithY(s * 0.3f), new Vector3(s, s * 0.8f, s), color, keepCollider: true);
            rock.name = "Rock";
            rock.transform.rotation = Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f);
            rock.AddComponent<Obstacle>();
            for (int i = 0; i < 2; i++)
            {
                Vector3 offset = new Vector3(Range(rng, -0.6f, 0.6f), 0f, Range(rng, -0.6f, 0.6f)).normalized * s * 0.5f;
                float ps = s * Range(rng, 0.2f, 0.35f);
                Visuals.Part(PrimitiveType.Sphere, root, (p + offset).WithY(ps * 0.25f), new Vector3(ps, ps * 0.7f, ps), color * 0.9f).name = "Pebble";
            }
        }

        static bool Overlaps(List<(Vector3 pos, float radius)> placed, Vector3 p, float radius)
        {
            foreach (var (pos, r) in placed)
                if ((pos - p).Flat().magnitude < r + radius) return true;
            return false;
        }

        static Vector3 RandomPoint(System.Random rng, float minFromCenter, float maxExtent)
        {
            while (true)
            {
                var p = new Vector3(Range(rng, -maxExtent, maxExtent), 0f, Range(rng, -maxExtent, maxExtent));
                if (p.magnitude >= minFromCenter) return p;
            }
        }

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
