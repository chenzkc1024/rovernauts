using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rovernauts
{
    /// <summary>Stats shared by everyone on the rover; upgrades modify these.</summary>
    public class RoverStats
    {
        public float damage = 10f;
        public float fireInterval = 0.17f;
        public float projectileSpeed = 42f;
        public float moveSpeed = 13f;
        public float maxHull = 150f;
        public int multishot;
        public int pierce;
    }

    /// <summary>
    /// The shared space rover. Player 1 drives it (tank-style: turn in place, throttle forward/back, boost);
    /// gunners ride on <see cref="Turret"/>s mounted on top. If the rover dies, the run is over for everyone.
    /// </summary>
    public class Rover : MonoBehaviour
    {
        public static Rover Instance { get; private set; }
        public const float Radius = 1.6f;

        public Health Health { get; private set; }
        public RoverStats Stats { get; } = new RoverStats();
        public readonly List<Turret> Turrets = new List<Turret>();
        public bool Autopilot;
        public Vector3 Velocity => rb != null ? rb.linearVelocity : Vector3.zero;
        public float BoostReady => 1f - Mathf.Clamp01(boostCooldown / BoostCooldownTime);

        const float Acceleration = 45f, TurnSpeed = 160f, BoostSpeed = 30f, BoostTime = 0.35f, BoostCooldownTime = 3f;
        static readonly RaycastHit[] feelerHits = new RaycastHit[8];
        Rigidbody rb;
        readonly List<Transform> wheels = new List<Transform>();
        float boostTimer, boostCooldown;

        public static Rover Create(Vector3 position)
        {
            var go = new GameObject("Rover");
            go.transform.position = position;
            var rover = go.AddComponent<Rover>();
            rover.Build();
            return rover;
        }

        void Build()
        {
            Instance = this;
            Health = gameObject.AddComponent<Health>();
            Health.Init(Team.Player, Stats.maxHull);
            Health.Damaged += amount =>
            {
                CameraRig.Shake(0.12f + amount * 0.02f);
                AudioManager.Play("hurt", 0.5f);
            };
            Health.Died += OnDestroyed;

            rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 6f;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 1f, 0f);
            box.size = new Vector3(2.6f, 1.2f, 3.4f);

            var hull = new Color(0.86f, 0.86f, 0.82f);
            var accent = new Color(1f, 0.55f, 0.1f);
            var dark = new Color(0.18f, 0.18f, 0.2f);
            var t = transform;
            Visuals.Part(PrimitiveType.Cube, t, new Vector3(0f, 0.85f, 0f), new Vector3(2.3f, 0.6f, 3.2f), hull);
            Visuals.Part(PrimitiveType.Cube, t, new Vector3(0f, 1.25f, -0.9f), new Vector3(1.7f, 0.5f, 1.1f), hull);
            Visuals.Part(PrimitiveType.Cube, t, new Vector3(0f, 0.85f, 1.62f), new Vector3(2.32f, 0.3f, 0.1f), accent);
            Visuals.Part(PrimitiveType.Cube, t, new Vector3(0f, 1.52f, -1.5f), new Vector3(2.2f, 0.06f, 0.9f), new Color(0.15f, 0.25f, 0.6f));
            Visuals.Part(PrimitiveType.Cube, t, new Vector3(0.85f, 2.1f, -1.3f), new Vector3(0.05f, 1.2f, 0.05f), dark);
            Visuals.Part(PrimitiveType.Sphere, t, new Vector3(0.85f, 2.75f, -1.3f), Vector3.one * 0.18f, new Color(1f, 0.2f, 0.2f), castShadows: false);
            for (int side = -1; side <= 1; side += 2)
            for (int i = -1; i <= 1; i++)
            {
                var wheel = Visuals.Part(PrimitiveType.Cylinder, t, new Vector3(side * 1.3f, 0.5f, i * 1.15f), new Vector3(0.9f, 0.18f, 0.9f), dark).transform;
                wheel.localRotation = Quaternion.Euler(0f, 0f, 90f);
                wheels.Add(wheel);
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Damage plus an optional shove (velocity change) from big hits like dashes and charges.</summary>
        public void TakeHit(float damage, Vector3 knockback)
        {
            Health.TakeDamage(damage);
            if (!Health.IsDead && knockback.sqrMagnitude > 0f) rb.AddForce(knockback.Flat(), ForceMode.VelocityChange);
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (Health.IsDead)
            {
                rb.linearVelocity = Vector3.zero;
                return;
            }
            bool canDrive = GameManager.I != null && GameManager.I.State == GameState.Playing;
            Vector2 input = Vector2.zero;
            bool boost = false;
            if (canDrive)
            {
                if (Autopilot) input = AutopilotInput(out boost);
                else input = KeyboardInput(out boost);
            }

            rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, input.x * TurnSpeed * dt, 0f));
            Vector3 desired = transform.forward * input.y * Stats.moveSpeed;

            boostCooldown -= dt;
            if (boost && boostCooldown <= 0f)
            {
                boostTimer = BoostTime;
                boostCooldown = BoostCooldownTime;
                AudioManager.Play("dash", 0.5f);
                Fx.Burst(transform.position - transform.forward * 1.8f + Vector3.up * 0.6f, new Color(1f, 0.7f, 0.3f), 8, 6f, 0.25f, 0.4f);
            }
            if (boostTimer > 0f)
            {
                boostTimer -= dt;
                desired = transform.forward * BoostSpeed;
            }

            rb.linearVelocity = Vector3.MoveTowards(rb.linearVelocity.Flat(), desired, Acceleration * dt);
            rb.angularVelocity = Vector3.zero;

            float spin = Vector3.Dot(rb.linearVelocity, transform.forward) * dt * 120f;
            foreach (var wheel in wheels) wheel.Rotate(Vector3.up, spin, Space.Self);
        }

        static Vector2 KeyboardInput(out bool boost)
        {
            var kb = Keyboard.current;
            boost = false;
            if (kb == null) return Vector2.zero;
            float x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
            float y = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
            boost = kb.spaceKey.isPressed || kb.leftShiftKey.isPressed;
            return new Vector2(x, y);
        }

        /// <summary>Demo/test driver: kite away from threats, circle the arena, steer around rocks.</summary>
        Vector2 AutopilotInput(out bool boost)
        {
            Vector3 pos = transform.position;
            Vector3 desire = Vector3.zero;
            float closest = float.MaxValue;
            foreach (var e in EnemyBase.All)
            {
                Vector3 away = (pos - e.transform.position).Flat();
                float d = away.magnitude;
                closest = Mathf.Min(closest, d);
                if (d < 18f) desire += away.normalized * (1f - d / 18f) * (e.IsBoss ? 3f : 1f);
            }
            Vector3 toCenter = (-pos).Flat();
            float r = toCenter.magnitude;
            if (r > 0.1f)
            {
                desire += Vector3.Cross(Vector3.up, toCenter / r) * 0.8f;
                desire += toCenter / r * Mathf.Clamp01((r - 18f) / 15f) * 2f;
            }
            for (int w = -1; w <= 1; w++)
            {
                Vector3 dir = Quaternion.Euler(0f, w * 40f, 0f) * transform.forward;
                int n = Physics.RaycastNonAlloc(pos + Vector3.up, dir, feelerHits, 7f, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                    if (feelerHits[i].collider.GetComponentInParent<Obstacle>() != null)
                        desire += feelerHits[i].normal.Flat() * 2f * (1f - feelerHits[i].distance / 7f);
            }
            boost = closest < 4f;
            if (desire.sqrMagnitude < 0.0001f) return new Vector2(0f, 0.5f);
            float angle = Vector3.SignedAngle(transform.forward, desire, Vector3.up);
            return new Vector2(Mathf.Clamp(angle / 40f, -1f, 1f), Mathf.Abs(angle) < 110f ? 1f : -0.4f);
        }

        void OnDestroyed()
        {
            Fx.Burst(transform.position + Vector3.up, new Color(1f, 0.6f, 0.2f), 40, 14f, 0.5f, 1.5f);
            Fx.Shockwave(transform.position, new Color(1f, 0.5f, 0.1f), 10f, 0.8f);
            AudioManager.Play("explode", 1f, 0f);
            CameraRig.Shake(1.2f);
            foreach (var turret in Turrets) turret.gameObject.SetActive(false);
            GameManager.I?.OnRoverDestroyed();
        }
    }
}
