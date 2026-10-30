using UnityEngine;

namespace Rovernauts
{
    public enum MoonEvent { None, AsteroidStorm, BloodMoon }

    /// <summary>
    /// Game-mode events and environment mood.
    ///  - Asteroid Storm (waves 3, 8, 13, ...): telegraphed meteors rain around the rover and hurt everything.
    ///  - Blood Moon (every 7th wave): red sky, enemies +20% speed and +25% damage.
    /// The sky, sun and ground also shift from calm blue to danger red as difficulty rises.
    /// </summary>
    public class EventDirector : MonoBehaviour
    {
        public MoonEvent Active { get; private set; }
        public float SpeedMultiplier => Active == MoonEvent.BloodMoon ? 1.2f : 1f;
        public float DamageMultiplier => Active == MoonEvent.BloodMoon ? 1.25f : 1f;

        float meteorTimer;
        int wave;
        GameObject bloodMoon;
        Material groundMaterial;

        public MoonEvent OnWaveStart(int waveNumber)
        {
            wave = waveNumber;
            if (wave % 7 == 0) Begin(MoonEvent.BloodMoon);
            else if (wave % 5 == 3) Begin(MoonEvent.AsteroidStorm);
            else Active = MoonEvent.None;
            return Active;
        }

        /// <summary>Ends whatever event is running; returns true if there was one (it earns an upgrade).</summary>
        public bool OnWaveEnd()
        {
            bool had = Active != MoonEvent.None;
            Active = MoonEvent.None;
            if (bloodMoon != null) Destroy(bloodMoon);
            Meteor.ClearAll(); // no surprise hits during the break
            return had;
        }

        void Begin(MoonEvent e)
        {
            Active = e;
            AudioManager.Play("alarm", 0.8f, 0f);
            if (e == MoonEvent.AsteroidStorm)
            {
                meteorTimer = 4f; // let the wave countdown finish first
                HUD.Banner($"WAVE {wave}: ASTEROID STORM", "Watch the red markers - impacts hurt everyone", new Color(1f, 0.65f, 0.3f));
            }
            else
            {
                HUD.Banner($"WAVE {wave}: BLOOD MOON", "Aliens are faster and hit harder", new Color(1f, 0.15f, 0.15f));
                var cam = CameraRig.I;
                if (cam != null)
                {
                    // Parked just in front of the lens in the top-right corner so it reads as sky, not terrain.
                    bloodMoon = Visuals.Part(PrimitiveType.Sphere, cam.transform, new Vector3(6.5f, 3.6f, 12f), Vector3.one * 1.8f,
                        new Color(0.9f, 0.08f, 0.05f), castShadows: false);
                    bloodMoon.name = "Blood Moon";
                }
            }
        }

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null) return;
            if (Active == MoonEvent.AsteroidStorm && gm.State == GameState.Playing && Rover.Instance != null)
            {
                if ((meteorTimer -= Time.deltaTime) <= 0f)
                {
                    meteorTimer = Mathf.Max(0.45f, 1.1f - wave * 0.03f);
                    var rover = Rover.Instance;
                    Vector3 target = rover.transform.position + rover.Velocity.Flat() * 0.8f + Random.insideUnitSphere.Flat() * 16f;
                    float limit = WorldBuilder.HalfSize - 2f;
                    target.x = Mathf.Clamp(target.x, -limit, limit);
                    target.z = Mathf.Clamp(target.z, -limit, limit);
                    Meteor.Launch(target, 3.5f, 12f * (1f + 0.05f * wave));
                }
            }
            UpdateMood(gm);
        }

        void UpdateMood(GameManager gm)
        {
            var cam = CameraRig.I != null ? CameraRig.I.Cam : null;
            var sun = WorldBuilder.Sun;
            if (cam == null || sun == null || WorldBuilder.Ground == null) return;
            if (groundMaterial == null) groundMaterial = WorldBuilder.Ground.material; // per-instance copy

            int w = WaveDirector.I != null ? WaveDirector.I.Wave : 1;
            float threat = Mathf.Clamp01((w - 1) / 12f + gm.RunTime / 900f);
            Color sky = threat < 0.5f
                ? Color.Lerp(new Color(0.02f, 0.03f, 0.09f), new Color(0.09f, 0.03f, 0.14f), threat * 2f)
                : Color.Lerp(new Color(0.09f, 0.03f, 0.14f), new Color(0.15f, 0.02f, 0.04f), threat * 2f - 1f);
            Color light = Color.Lerp(new Color(0.9f, 0.93f, 1f), new Color(1f, 0.7f, 0.55f), threat);
            Color ground = Color.Lerp(new Color(0.43f, 0.42f, 0.46f), new Color(0.42f, 0.36f, 0.42f), threat);
            float intensity = 1.15f;

            if (Active == MoonEvent.BloodMoon)
            {
                sky = new Color(0.22f, 0.01f, 0.02f);
                light = new Color(1f, 0.3f, 0.25f);
                ground = new Color(0.45f, 0.3f, 0.3f);
            }
            else if (Active == MoonEvent.AsteroidStorm)
            {
                sky = new Color(0.13f, 0.08f, 0.04f);
                light = new Color(1f, 0.75f, 0.5f);
                intensity = 0.85f;
            }

            float k = Time.deltaTime * 1.5f;
            cam.backgroundColor = Color.Lerp(cam.backgroundColor, sky, k);
            sun.color = Color.Lerp(sun.color, light, k);
            sun.intensity = Mathf.Lerp(sun.intensity, intensity, k);
            groundMaterial.color = Color.Lerp(groundMaterial.color, ground, k);
        }
    }

    /// <summary>One telegraphed meteor: red warning ring, falling rock, area damage to every team, scorch mark.</summary>
    public class Meteor : MonoBehaviour
    {
        const float WarningTime = 1.4f, FallTime = 0.45f;
        static readonly Collider[] hits = new Collider[64];
        static readonly System.Collections.Generic.List<Meteor> active = new System.Collections.Generic.List<Meteor>();
        Vector3 target, start;
        float radius, damage, t;
        Transform marker, rock;
        bool whistled;

        public static void Launch(Vector3 target, float radius, float damage)
        {
            var go = new GameObject("Meteor");
            var m = go.AddComponent<Meteor>();
            m.target = target.WithY(0f);
            m.radius = radius;
            m.damage = damage;
            m.start = m.target + new Vector3(8f, 40f, 10f);
            m.marker = Visuals.Part(PrimitiveType.Cylinder, go.transform, m.target.WithY(0.07f), new Vector3(radius * 2f, 0.01f, radius * 2f),
                new Color(0.9f, 0.15f, 0.1f), castShadows: false).transform;
            m.rock = Visuals.Part(PrimitiveType.Sphere, go.transform, m.start, Vector3.one * 1.8f, new Color(0.45f, 0.3f, 0.2f)).transform;
            m.rock.gameObject.SetActive(false);
            active.Add(m);
        }

        public static void ClearAll()
        {
            foreach (var m in active)
                if (m != null) Destroy(m.gameObject);
            active.Clear();
        }

        void OnDestroy() => active.Remove(this);

        void Update()
        {
            t += Time.deltaTime;
            float pulse = 0.85f + Mathf.PingPong(t * 4f, 0.3f);
            marker.localScale = new Vector3(radius * 2f * pulse, 0.01f, radius * 2f * pulse);
            if (t < WarningTime) return;
            if (!whistled)
            {
                whistled = true;
                rock.gameObject.SetActive(true);
                AudioManager.Play("meteor", 0.35f);
            }
            float k = Mathf.Clamp01((t - WarningTime) / FallTime);
            rock.position = Vector3.Lerp(start, target.WithY(0.6f), k * k);
            if (k >= 1f) Impact();
        }

        void Impact()
        {
            int n = Physics.OverlapSphereNonAlloc(target + Vector3.up, radius, hits, ~0, QueryTriggerInteraction.Ignore);
            var damaged = new System.Collections.Generic.HashSet<Health>();
            for (int i = 0; i < n; i++)
            {
                var h = hits[i].GetComponentInParent<Health>();
                if (h != null && damaged.Add(h)) h.TakeDamage(damage);
            }
            Fx.Burst(target + Vector3.up * 0.5f, new Color(0.5f, 0.35f, 0.25f), 14, 10f, 0.4f, 0.9f);
            Fx.Shockwave(target, new Color(1f, 0.6f, 0.2f), radius, 0.4f);
            Fx.Scorch(target, radius * 0.6f, 8f);
            AudioManager.Play("explode", 0.6f);
            if (Rover.Instance != null)
            {
                float d = (Rover.Instance.transform.position - target).Flat().magnitude;
                CameraRig.Shake(Mathf.Clamp01(1f - d / 25f) * 0.5f);
            }
            Destroy(gameObject);
        }
    }
}
