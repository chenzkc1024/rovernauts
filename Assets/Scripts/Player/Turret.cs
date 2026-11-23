using UnityEngine;
using UnityEngine.InputSystem;

namespace Rovernauts
{
    /// <summary>The rover's independently aimed cannon.</summary>
    public class Turret : MonoBehaviour
    {
        public static readonly Color Accent = new Color(1f, 0.85f, 0.2f);
        public Vector3 AimDirection => aim;
        internal int BurstsFired { get; private set; }

        Rover rover;
        Transform head, barrel;
        Vector3 aim = Vector3.forward;
        float cooldown, recoil;

        public static Turret Create(Rover rover)
        {
            var go = new GameObject("Main Turret");
            go.transform.SetParent(rover.transform, false);
            go.transform.localPosition = new Vector3(0f, 1.15f, 0.45f);
            var turret = go.AddComponent<Turret>();
            turret.rover = rover;

            Visuals.Part(PrimitiveType.Cylinder, go.transform, Vector3.zero, new Vector3(0.9f, 0.1f, 0.9f), new Color(0.2f, 0.2f, 0.22f));
            turret.head = new GameObject("Head").transform;
            turret.head.SetParent(go.transform, false);
            LunarArt.Armor(turret.head, new Vector3(0f, 0.25f, 0f), new Vector3(0.82f, 0.51f, 0.82f), LunarArt.Frame);
            LunarArt.LightStrip(turret.head, new Vector3(0f, 0.517f, 0f), new Vector3(0.42f, 0.035f, 0.47f), Accent);
            turret.barrel = new GameObject("Cannon barrel").transform;
            turret.barrel.SetParent(turret.head, false);
            LunarArt.Armor(turret.barrel, Vector3.zero, new Vector3(0.22f, 0.24f, 1f), LunarArt.Alloy);
            LunarArt.Armor(turret.barrel, Vector3.forward * 0.48f, new Vector3(0.31f, 0.3f, 0.23f), LunarArt.Frame);
            LunarArt.LightStrip(turret.barrel, new Vector3(0f, 0.125f, 0.1f), new Vector3(0.08f, 0.04f, 0.56f), Accent);
            return turret;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null || gm.Mode == CrewMode.LanGunner || gm.State != GameState.Playing || rover.Health.IsDead) return;
            float dt = Time.deltaTime;

            bool fire = rover.Autopilot ? ReadAuto() : ApplyControls(gm.Mode == CrewMode.LanDriver
                ? gm.Lan.GunnerButtons : ReadButtons(Keyboard.current, gm.Mode == CrewMode.Local));

            head.rotation = Quaternion.LookRotation(aim);
            recoil = Mathf.Max(0f, recoil - dt * 6f);
            barrel.localPosition = new Vector3(0f, 0.28f, 0.65f - recoil * 0.25f);

            cooldown -= dt;
            if (fire && cooldown <= 0f)
            {
                cooldown = rover.Stats.fireInterval;
                Fire();
            }
        }

        internal static byte ReadButtons(Keyboard kb, bool local)
        {
            if (kb == null) return 0;
            return (byte)((kb.rightArrowKey.isPressed ? 1 : 0) | (kb.leftArrowKey.isPressed ? 2 : 0)
                | (kb.upArrowKey.isPressed ? 4 : 0) | (kb.downArrowKey.isPressed ? 8 : 0)
                | ((local ? kb.rightCtrlKey.isPressed : kb.spaceKey.isPressed) ? 16 : 0));
        }

        bool ApplyControls(byte buttons)
        {
            float x = ((buttons & 1) != 0 ? 1f : 0f) - ((buttons & 2) != 0 ? 1f : 0f);
            float z = ((buttons & 4) != 0 ? 1f : 0f) - ((buttons & 8) != 0 ? 1f : 0f);
            var direction = new Vector3(x, 0f, z);
            if (direction.sqrMagnitude > 0f) aim = direction.normalized;
            return (buttons & 16) != 0;
        }

        internal void ApplyLanAim(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.001f) return;
            aim = direction;
            head.rotation = Quaternion.LookRotation(aim);
            barrel.localPosition = new Vector3(0f, 0.28f, 0.65f);
        }

        /// <summary>Autopilot gunner for the demo mode: nearest enemy, leading its movement.</summary>
        bool ReadAuto()
        {
            EnemyBase best = null;
            float bestDist = 34f;
            foreach (var e in EnemyBase.All)
            {
                if (e.Health.Invulnerable) continue;
                float d = (e.transform.position - transform.position).Flat().magnitude - (e.IsBoss ? 10f : 0f);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = e;
                }
            }
            if (best == null) return false;
            Vector3 to = (best.transform.position - transform.position).Flat();
            float travel = to.magnitude / rover.Stats.projectileSpeed;
            aim = (to + best.Agent.Velocity * travel).Flat().normalized;
            return true;
        }

        void Fire()
        {
            BurstsFired++;
            var stats = rover.Stats;
            int count = 1 + stats.multishot;
            for (int i = 0; i < count; i++)
            {
                float angle = (i - (count - 1) * 0.5f) * 7f;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * aim;
                Projectile.Fire(transform.position + aim * 1.3f, dir, stats.projectileSpeed, stats.damage, Team.Player, Accent, 0.35f, 1f, stats.pierce);
            }
            Fx.Burst(barrel.position + aim * 0.54f, Accent, 3, 3.5f, 0.14f, 0.12f);
            recoil = 1f;
            AudioManager.Play("shoot", 0.25f);
        }
    }
}
