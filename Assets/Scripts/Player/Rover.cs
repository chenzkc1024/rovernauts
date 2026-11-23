using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rovernauts
{
    /// <summary>Rover stats modified by upgrades.</summary>
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
    /// The rover turns in place, throttles forward or back, and boosts. Its turret aims independently.
    /// </summary>
    public class Rover : MonoBehaviour
    {
        public static Rover Instance { get; private set; }
        public const float Radius = 1.6f;

        public Health Health { get; private set; }
        public RoverStats Stats { get; } = new RoverStats();
        public Turret MainTurret { get; private set; }
        public bool Autopilot;
        public Vector3 Velocity => lanReplica ? lanVelocity : rb != null ? rb.linearVelocity : Vector3.zero;
        public float BoostReady => lanReplica ? lanBoost : 1f - Mathf.Clamp01(boostCooldown / BoostCooldownTime);
        bool lanReplica;
        Vector3 lanVelocity;
        float lanBoost;

        const float Acceleration = 45f, TurnSpeed = 160f, BoostSpeed = 30f, BoostTime = 0.35f, BoostCooldownTime = 3f;
        static readonly RaycastHit[] feelerHits = new RaycastHit[8];
        Rigidbody rb;
        readonly List<Transform> wheels = new List<Transform>();
        float boostTimer, boostCooldown, boostDirection = 1f;

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
            // Screen shake and the red edge flash come from HUD, which watches the hull on host and LAN gunner alike.
            Health.Damaged += amount => AudioManager.Play("hurt", 0.5f);
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

            BuildChassis();
            MainTurret = Turret.Create(this);
        }

        void BuildChassis()
        {
            var t = transform;
            var hull = LunarArt.Alloy;
            var dark = LunarArt.Frame;
            var accent = LunarArt.Orange;
            LunarArt.Armor(t, new Vector3(0f, 0.63f, 0f), new Vector3(2.35f, 0.62f, 3.45f), dark);
            LunarArt.Armor(t, new Vector3(0f, 1.05f, 0f), new Vector3(2.3f, 0.55f, 3.3f), hull);
            LunarArt.Armor(t, new Vector3(0f, 1.4f, -0.73f), new Vector3(1.75f, 0.63f, 1.25f), new Color(0.14f, 0.24f, 0.34f));
            LunarArt.Armor(t, new Vector3(0f, 1.35f, 1.2f), new Vector3(1.8f, 0.21f, 0.7f), accent);
            LunarArt.Armor(t, new Vector3(0f, 0.65f, 1.67f), new Vector3(2.2f, 0.22f, 0.2f), dark);

            for (int side = -1; side <= 1; side += 2)
            {
                LunarArt.Armor(t, new Vector3(side * 1.15f, 0.99f, 0f), new Vector3(0.48f, 0.27f, 3.3f), accent);
                LunarArt.LightStrip(t, new Vector3(side * 0.73f, 0.99f, 1.63f), new Vector3(0.45f, 0.16f, 0.07f), LunarArt.Cyan);
                LunarArt.LightStrip(t, new Vector3(side * 0.78f, 0.94f, -1.63f), new Vector3(0.3f, 0.15f, 0.07f), Color.red);
                LunarArt.LightStrip(t, new Vector3(side * 0.56f, 1.71f, -0.74f), new Vector3(0.10f, 0.045f, 0.57f), LunarArt.Cyan);
                for (int i = -1; i <= 1; i++)
                {
                    var wheel = new GameObject("Wheel").transform;
                    wheel.SetParent(t, false);
                    wheel.localPosition = new Vector3(side * 1.28f, 0.5f, i * 1.08f);
                    wheel.localRotation = Quaternion.Euler(0f, 0f, 90f * side);
                    wheels.Add(wheel);
                    Visuals.Part(PrimitiveType.Cylinder, wheel, Vector3.zero, new Vector3(0.94f, 0.22f, 0.94f), dark);
                    Visuals.Part(PrimitiveType.Cylinder, wheel, Vector3.down * 0.23f, new Vector3(0.55f, 0.03f, 0.55f), hull);
                    LunarArt.LightStrip(wheel, Vector3.down * 0.27f, new Vector3(0.19f, 0.025f, 0.19f), accent);
                    for (int tread = 0; tread < 8; tread++)
                    {
                        var rotation = Quaternion.Euler(0f, tread * 45f, 0f);
                        var grip = LunarArt.Armor(wheel, rotation * new Vector3(0f, 0f, 0.455f),
                            new Vector3(0.19f, 0.43f, 0.075f), new Color(0.16f, 0.2f, 0.23f));
                        grip.transform.localRotation = rotation;
                    }
                }
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
            if (lanReplica) return;
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
                // Boost follows the throttle: holding reverse dashes backwards, otherwise forwards.
                boostDirection = input.y < 0f ? -1f : 1f;
                boostCooldown = BoostCooldownTime;
                AudioManager.Play("dash", 0.5f);
                Fx.Burst(transform.position - transform.forward * 1.8f + Vector3.up * 0.6f, new Color(1f, 0.7f, 0.3f), 8, 6f, 0.25f, 0.4f);
            }
            if (canDrive && boostTimer > 0f)
            {
                boostTimer -= dt;
                desired = transform.forward * (BoostSpeed * boostDirection);
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
            var profile = Controls.ProfileFor(GameManager.I.Mode);
            float x = (Controls.Held(kb, profile, Act.TurnRight) ? 1f : 0f) - (Controls.Held(kb, profile, Act.TurnLeft) ? 1f : 0f);
            float y = (Controls.Held(kb, profile, Act.Forward) ? 1f : 0f) - (Controls.Held(kb, profile, Act.Reverse) ? 1f : 0f);
            // Car steering: while reversing, "right" swings the back of the rover to the right.
            if (Controls.CarSteering && y < 0f) x = -x;
            boost = Controls.Held(kb, profile, Act.Boost);
            return new Vector2(x, y);
        }

        internal void ApplyLanState(LanSnapshot snapshot)
        {
            if (!lanReplica)
            {
                lanReplica = true;
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.None;
            }
            lanVelocity = snapshot.velocity;
            lanBoost = snapshot.boost;
            Health.ApplyLanValues(snapshot.hull, snapshot.maxHull);
            MainTurret.gameObject.SetActive(!Health.IsDead);
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
            MainTurret.gameObject.SetActive(false);
            GameManager.I?.OnRoverDestroyed();
        }
    }
}
