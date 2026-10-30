using System.Collections.Generic;
using UnityEngine;

namespace Rovernauts
{
    /// <summary>
    /// Builds everything out of Unity primitives so the prototype runs without imported art.
    /// The art team can later swap these for real models without touching gameplay code.
    /// </summary>
    public static class Visuals
    {
        public const string BaseMaterialName = "RovernautsBase";
        static Material template;
        static readonly Dictionary<Color, Material> cache = new Dictionary<Color, Material>();

        /// <summary>One shared material per colour, cloned from the render pipeline's default material.</summary>
        public static Material Mat(Color color)
        {
            if (cache.TryGetValue(color, out var mat) && mat != null) return mat;
            if (template == null)
            {
                // A material asset in Resources guarantees its shader ships in player builds;
                // the primitives' built-in default material alone gets stripped.
                template = Resources.Load<Material>(BaseMaterialName);
                if (template == null)
                {
                    var probe = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    template = probe.GetComponent<Renderer>().sharedMaterial;
                    Object.DestroyImmediate(probe);
                }
            }
            mat = new Material(template) { color = color };
            cache[color] = mat;
            return mat;
        }

        public static GameObject Part(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 scale, Color color,
            bool keepCollider = false, bool castShadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            // DestroyImmediate so the collider is gone before any physics query this frame.
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = Mat(color);
            if (!castShadows) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        public static LineRenderer Line(Transform parent, Color color, float width)
        {
            var go = new GameObject("Line");
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = Mat(color);
            line.startWidth = line.endWidth = width;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.positionCount = 0;
            return line;
        }
    }
}
