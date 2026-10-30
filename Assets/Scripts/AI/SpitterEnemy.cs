using UnityEngine;

namespace Rovernauts
{
    /// <summary>
    /// Ranged kiter. FSM: Spawn -> Approach -> Strafe <-> Charge (telegraphed shot), with Retreat when the
    /// rover gets too close. Approach uses A*; Strafe orbits at a preferred range; Retreat uses Flee.
    /// </summary>
    public class SpitterEnemy : EnemyBase
    {
        public override string DisplayName => "Spitter";
        public override int Score => 15;
        protected override Color MainColor => new Color(0.6f, 0.3f, 0.9f);
        protected override float BodyRadius => 0.7f;
        protected override State EntryState => approach;

        const float PreferredRange = 14f, MinRange = 7f, ShotSpeed = 15f;
        static readonly Color SacColor = new Color(1f, 0.45f, 0.8f);
        State approach, strafe, charge, retreat;
        Transform sac;
        float fireTimer = 1f, flipTimer;
        float orbitDirection = 1f;

        protected override void BuildBody(Transform root)
        {
            Visuals.Part(PrimitiveType.Capsule, root, new Vector3(0f, 1.1f, 0f), new Vector3(1f, 0.75f, 1f), MainColor);
            sac = Visuals.Part(PrimitiveType.Sphere, root, new Vector3(0f, 1.9f, -0.1f), Vector3.one * 0.8f, SacColor).transform;
            Visuals.Part(PrimitiveType.Cylinder, root, new Vector3(0f, 1.2f, 0.5f), new Vector3(0.35f, 0.25f, 0.35f), new Color(0.3f, 0.1f, 0.4f))
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        protected override void BuildStates()
        {
            approach = new State("Approach", tick: dt =>
            {
                steering += NavigateTo(TargetPos);
                if (DistToTarget < PreferredRange + 1f && HasLineOfSight()) fsm.Change(strafe);
            });

            strafe = new State("Strafe",
                enter: () => flipTimer = Random.Range(2.5f, 4.5f),
                tick: dt =>
                {
                    steering += Agent.Orbit(TargetPos, PreferredRange, orbitDirection);
                    FaceTarget();
                    if ((flipTimer -= dt) <= 0f)
                    {
                        orbitDirection = -orbitDirection;
                        flipTimer = Random.Range(2.5f, 4.5f);
                    }
                    fireTimer -= dt;
                    if (DistToTarget < MinRange) fsm.Change(retreat);
                    else if (DistToTarget > PreferredRange + 6f || !HasLineOfSight()) fsm.Change(approach);
                    else if (fireTimer <= 0f) fsm.Change(charge);
                });

            charge = new State("Charge",
                tick: dt =>
                {
                    steering += Agent.Brake();
                    FaceTarget();
                    sac.localScale = Vector3.one * (0.8f + fsm.TimeInState * 1.2f);
                    if (fsm.TimeInState >= 0.45f)
                    {
                        Fire();
                        fireTimer = Random.Range(1.6f, 2.4f);
                        fsm.Change(strafe);
                    }
                },
                exit: () => sac.localScale = Vector3.one * 0.8f);

            retreat = new State("Retreat", tick: dt =>
            {
                steering += Agent.Flee(TargetPos) * 1.2f;
                FaceTarget();
                if (DistToTarget > PreferredRange - 2f || fsm.TimeInState > 1.5f) fsm.Change(strafe);
            });
        }

        void Fire()
        {
            if (!TargetAlive) return;
            // Lead the target: aim where the rover will be when the glob arrives.
            float travel = DistToTarget / ShotSpeed;
            Vector3 aim = (TargetPos + Target.Velocity.Flat() * travel * 0.7f - Pos).Flat().normalized;
            int shots = WaveDirector.I != null && WaveDirector.I.Wave >= 6 ? 3 : 1;
            for (int i = 0; i < shots; i++)
            {
                float angle = (i - (shots - 1) * 0.5f) * 12f;
                Projectile.Fire(Pos + aim * (Agent.radius + 0.4f), Quaternion.Euler(0f, angle, 0f) * aim, ShotSpeed, stats.damage,
                    Team.Enemy, SacColor, 0.55f, 2.5f);
            }
            AudioManager.Play("enemy_shot", 0.4f);
        }
    }
}
