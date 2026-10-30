using UnityEngine;

namespace Rovernauts
{
    /// <summary>
    /// Fast ambusher. FSM: Spawn -> Stalk (pursuit with prediction) -> WindUp (telegraph line) -> Dash -> Recover -> Stalk.
    /// The wind-up gives the driver a fair window to swerve out of the charge lane.
    /// </summary>
    public class DasherEnemy : EnemyBase
    {
        public override string DisplayName => "Dasher";
        public override int Score => 15;
        protected override Color MainColor => new Color(1f, 0.55f, 0.1f);
        protected override float BodyRadius => 0.75f;
        protected override State EntryState => stalk;

        const float TriggerRange = 13f, DashSpeed = 30f, DashTime = 0.45f, WindUpTime = 0.65f;
        State stalk, windUp, dash, recover;
        Vector3 dashDirection;
        float cooldown;
        bool connected;
        LineRenderer telegraph;

        protected override void BuildBody(Transform root)
        {
            Visuals.Part(PrimitiveType.Cube, root, new Vector3(0f, 1f, 0f), new Vector3(0.8f, 0.8f, 1.9f), MainColor)
                .transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var fin = new Color(0.7f, 0.3f, 0.05f);
            Visuals.Part(PrimitiveType.Cube, root, new Vector3(-0.6f, 1f, -0.5f), new Vector3(0.7f, 0.08f, 0.7f), fin);
            Visuals.Part(PrimitiveType.Cube, root, new Vector3(0.6f, 1f, -0.5f), new Vector3(0.7f, 0.08f, 0.7f), fin);
            Visuals.Part(PrimitiveType.Sphere, root, new Vector3(0f, 1.15f, 0.85f), Vector3.one * 0.3f, new Color(1f, 0.1f, 0.1f), castShadows: false);
        }

        protected override void BuildStates()
        {
            stalk = new State("Stalk", tick: dt =>
            {
                cooldown -= dt;
                steering += NavigateTo(Agent.Predict(TargetPos, Target.Velocity, 0.8f));
                if (cooldown <= 0f && DistToTarget < TriggerRange && HasLineOfSight()) fsm.Change(windUp);
            });

            windUp = new State("WindUp",
                enter: () =>
                {
                    if (telegraph == null) telegraph = Visuals.Line(transform, new Color(1f, 0.15f, 0.1f), 0.35f);
                    telegraph.enabled = true;
                    telegraph.positionCount = 2;
                },
                tick: dt =>
                {
                    steering += Agent.Brake();
                    // Keep tracking until the last moment, then the lane is locked in.
                    if (fsm.TimeInState < WindUpTime - 0.2f)
                        dashDirection = (TargetPos + Target.Velocity.Flat() * 0.25f - Pos).Flat().normalized;
                    Agent.FaceDirection = dashDirection;
                    Shake(0.12f);
                    telegraph.SetPosition(0, Pos.WithY(0.25f));
                    telegraph.SetPosition(1, (Pos + dashDirection * DashSpeed * DashTime).WithY(0.25f));
                    if (fsm.TimeInState >= WindUpTime) fsm.Change(dash);
                },
                exit: () =>
                {
                    telegraph.enabled = false;
                    body.localPosition = Vector3.zero;
                });

            dash = new State("Dash",
                enter: () =>
                {
                    Agent.maxSpeed = DashSpeed;
                    Agent.Velocity = dashDirection * DashSpeed;
                    separate = avoidObstacles = false;
                    connected = false;
                    AudioManager.Play("dash", 0.35f);
                },
                tick: dt =>
                {
                    steering += dashDirection * Agent.maxForce;
                    Agent.FaceDirection = dashDirection;
                    if (!connected && GapToTarget < 0.4f)
                    {
                        connected = true;
                        Target.TakeHit(stats.damage, dashDirection * 8f);
                        fsm.Change(recover);
                        return;
                    }
                    if (fsm.TimeInState >= DashTime) fsm.Change(recover);
                },
                exit: () =>
                {
                    Agent.maxSpeed = stats.speed;
                    separate = avoidObstacles = true;
                });

            recover = new State("Recover", tick: dt =>
            {
                steering += Agent.Brake(0.6f);
                if (fsm.TimeInState >= 1f)
                {
                    cooldown = 1.5f;
                    fsm.Change(stalk);
                }
            });
        }

        protected override void OnDied()
        {
            if (telegraph != null) telegraph.enabled = false;
            base.OnDied();
        }
    }
}
