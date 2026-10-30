using UnityEngine;
using UnityEngine.InputSystem;

namespace Rovernauts
{
    public enum GunnerInput { Mouse, Gamepad, Auto }

    /// <summary>
    /// A gun seat on the rover controlled by one player. Aims independently of where the driver is going,
    /// which is the core co-op split: one person steers, the others shoot.
    /// </summary>
    public class Turret : MonoBehaviour
    {
        public int PlayerNumber { get; private set; }
        public Color Color { get; private set; }
        public GunnerInput Source { get; set; }
        public Gamepad Pad { get; private set; }

        public string ControlLabel => Source switch
        {
            GunnerInput.Mouse => "Mouse",
            GunnerInput.Gamepad => "Gamepad",
            _ => "Autopilot",
        };

        Rover rover;
        Transform head, barrel;
        Vector3 aim = Vector3.forward;
        float cooldown, recoil;

        public static Turret Create(Rover rover, Vector3 mount, int playerNumber, Color color, GunnerInput source, Gamepad pad = null)
        {
            var go = new GameObject($"Turret P{playerNumber}");
            go.transform.SetParent(rover.transform, false);
            go.transform.localPosition = mount;
            var turret = go.AddComponent<Turret>();
            turret.rover = rover;
            turret.PlayerNumber = playerNumber;
            turret.Color = color;
            turret.Source = source;
            turret.Pad = pad;

            Visuals.Part(PrimitiveType.Cylinder, go.transform, Vector3.zero, new Vector3(0.9f, 0.1f, 0.9f), new Color(0.2f, 0.2f, 0.22f));
            turret.head = new GameObject("Head").transform;
            turret.head.SetParent(go.transform, false);
            Visuals.Part(PrimitiveType.Sphere, turret.head, new Vector3(0f, 0.25f, 0f), new Vector3(0.75f, 0.55f, 0.75f), color);
            turret.barrel = Visuals.Part(PrimitiveType.Cube, turret.head, new Vector3(0f, 0.28f, 0.65f), new Vector3(0.22f, 0.22f, 1f), color * 0.7f).transform;
            rover.Turrets.Add(turret);
            return turret;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null || gm.State != GameState.Playing || rover.Health.IsDead) return;
            float dt = Time.deltaTime;

            bool fire = Source switch
            {
                GunnerInput.Mouse => ReadMouse(),
                GunnerInput.Gamepad => ReadGamepad(),
                _ => ReadAuto(),
            };

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

        bool ReadMouse()
        {
            var mouse = Mouse.current;
            var cam = CameraRig.I != null ? CameraRig.I.Cam : null;
            if (mouse == null || cam == null) return false;
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            var plane = new Plane(Vector3.up, Vector3.up * Projectile.PlaneHeight);
            if (plane.Raycast(ray, out float t))
            {
                Vector3 to = (ray.GetPoint(t) - transform.position).Flat();
                if (to.sqrMagnitude > 0.25f) aim = to.normalized;
            }
            return mouse.leftButton.isPressed;
        }

        bool ReadGamepad()
        {
            if (Pad == null || !Pad.added) return false;
            Vector2 stick = Pad.rightStick.ReadValue();
            if (stick.sqrMagnitude < 0.1f) stick = Pad.leftStick.ReadValue();
            // The camera looks along +Z, so stick up = world forward.
            if (stick.sqrMagnitude > 0.1f) aim = new Vector3(stick.x, 0f, stick.y).normalized;
            return Pad.rightTrigger.isPressed || Pad.rightShoulder.isPressed || Pad.buttonSouth.isPressed;
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
            var stats = rover.Stats;
            int count = 1 + stats.multishot;
            for (int i = 0; i < count; i++)
            {
                float angle = (i - (count - 1) * 0.5f) * 7f;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * aim;
                Projectile.Fire(transform.position + aim * 1.3f, dir, stats.projectileSpeed, stats.damage, Team.Player, Color, 0.35f, 1f, stats.pierce);
            }
            recoil = 1f;
            AudioManager.Play("shoot", 0.25f);
        }
    }
}
