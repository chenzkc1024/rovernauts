using UnityEngine;

namespace Rovernauts
{
    /// <summary>
    /// Basic melee swarmer. FSM: Spawn -> Chase -> Attack, back to Chase if the rover pulls away.
    /// Chase uses A* around rocks plus separation so the swarm surrounds the rover instead of queueing.
    /// </summary>
    public class CrawlerEnemy : EnemyBase
    {
        public override string DisplayName => "Crawler";
        protected override Color MainColor => new Color(0.35f, 0.85f, 0.3f);
        protected override float BodyRadius => 0.75f;
        protected override State EntryState => chase;

        const float BiteRange = 0.5f, BiteInterval = 0.9f;
        State chase, attack;
        float biteTimer;

        protected override void BuildBody(Transform root)
        {
            Visuals.Part(PrimitiveType.Sphere, root, new Vector3(0f, 0.75f, 0f), new Vector3(1.3f, 0.8f, 1.5f), MainColor);
            Visuals.Part(PrimitiveType.Sphere, root, new Vector3(0f, 1.05f, -0.2f), new Vector3(0.8f, 0.5f, 0.8f), new Color(0.25f, 0.6f, 0.2f));
            var eye = new Color(1f, 0.9f, 0.2f);
            Visuals.Part(PrimitiveType.Sphere, root, new Vector3(-0.25f, 0.95f, 0.65f), Vector3.one * 0.25f, eye, castShadows: false);
            Visuals.Part(PrimitiveType.Sphere, root, new Vector3(0.25f, 0.95f, 0.65f), Vector3.one * 0.25f, eye, castShadows: false);
            var leg = new Color(0.2f, 0.45f, 0.15f);
            for (int i = 0; i < 3; i++)
            {
                float z = -0.45f + i * 0.45f;
                Visuals.Part(PrimitiveType.Cube, root, new Vector3(-0.75f, 0.35f, z), new Vector3(0.6f, 0.12f, 0.12f), leg).transform.localRotation = Quaternion.Euler(0f, 0f, -30f);
                Visuals.Part(PrimitiveType.Cube, root, new Vector3(0.75f, 0.35f, z), new Vector3(0.6f, 0.12f, 0.12f), leg).transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
            }
        }

        protected override void BuildStates()
        {
            chase = new State("Chase", tick: dt =>
            {
                // Aim for the closest point on the rover's hull rather than its centre.
                Vector3 contact = TargetPos - ToTarget.normalized * (Rover.Radius + Agent.radius);
                steering += NavigateTo(contact, 2f);
                if (GapToTarget < BiteRange) fsm.Change(attack);
            });

            attack = new State("Attack",
                enter: () => biteTimer = 0.25f,
                tick: dt =>
                {
                    if (GapToTarget > BiteRange + 1f)
                    {
                        fsm.Change(chase);
                        return;
                    }
                    Vector3 contact = TargetPos - ToTarget.normalized * (Rover.Radius + Agent.radius);
                    steering += Agent.Arrive(contact, 1.5f);
                    FaceTarget();
                    biteTimer -= dt;
                    float lunge = Mathf.Clamp01(biteTimer / 0.2f);
                    body.localPosition = Vector3.forward * (1f - lunge) * 0.3f;
                    if (biteTimer <= 0f)
                    {
                        biteTimer = BiteInterval;
                        Target.TakeHit(stats.damage, ToTarget.normalized * 0.5f);
                        AudioManager.Play("bite", 0.4f);
                    }
                },
                exit: () => body.localPosition = Vector3.zero);
        }
    }
}
