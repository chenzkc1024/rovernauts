using UnityEngine;

namespace Rovernauts
{
    /// <summary>Cheap primitive-based effects: debris bursts, shockwave rings and scorch marks.</summary>
    public static class Fx
    {
        public static void Burst(Vector3 position, Color color, int count, float speed, float size, float life = 0.5f)
        {
            for (int i = 0; i < count; i++)
            {
                var go = Visuals.Part(PrimitiveType.Cube, null, position, Vector3.one * size, color, castShadows: false);
                go.name = "Debris";
                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) + 0.3f;
                go.AddComponent<FxParticle>().Launch(dir.normalized * speed * Random.Range(0.5f, 1f), life * Random.Range(0.7f, 1.3f));
            }
        }

        public static LineRenderer GroundRing(Transform parent, Color color, float radius, float width)
        {
            var line = Visuals.Line(parent, color, width);
            line.useWorldSpace = false;
            line.sharedMaterial = Visuals.Glow(color);
            line.loop = true;
            line.positionCount = 48;
            for (int i = 0; i < line.positionCount; i++)
            {
                float angle = i * 2f * Mathf.PI / line.positionCount;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
            return line;
        }

        public static void Shockwave(Vector3 position, Color color, float radius, float duration)
        {
            var ring = GroundRing(null, color, radius, 0.22f);
            ring.gameObject.name = "Shockwave";
            ring.transform.position = position.WithY(0.11f);
            ring.gameObject.AddComponent<FxRing>().Init(ring, duration);
        }

        public static void Scorch(Vector3 position, float radius, float duration)
        {
            var go = Visuals.Part(PrimitiveType.Cylinder, null, position.WithY(0.03f), new Vector3(radius * 2f, 0.01f, radius * 2f),
                new Color(0.13f, 0.11f, 0.1f), castShadows: false);
            go.name = "Scorch";
            Object.Destroy(go, duration);
        }
    }

    public class FxParticle : MonoBehaviour
    {
        Vector3 velocity, startScale, spin;
        float life, total;

        public void Launch(Vector3 initialVelocity, float lifetime)
        {
            velocity = initialVelocity;
            life = total = lifetime;
            startScale = transform.localScale;
            spin = Random.insideUnitSphere * 720f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            velocity.y -= 20f * dt;
            Vector3 p = transform.position + velocity * dt;
            if (p.y < 0.05f) { p.y = 0.05f; velocity *= 0.45f; velocity.y = Mathf.Abs(velocity.y); }
            transform.position = p;
            transform.Rotate(spin * dt);
            life -= dt;
            transform.localScale = startScale * Mathf.Clamp01(life / total);
            if (life <= 0f) Destroy(gameObject);
        }
    }

    public class FxRing : MonoBehaviour
    {
        LineRenderer ring;
        float duration, t;

        public void Init(LineRenderer line, float seconds)
        {
            ring = line;
            duration = seconds;
            transform.localScale = Vector3.one * 0.01f;
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = t / duration;
            transform.localScale = Vector3.one * Mathf.Max(0.01f, Mathf.Sqrt(k));
            ring.widthMultiplier = Mathf.Max(0f, 1f - k);
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
