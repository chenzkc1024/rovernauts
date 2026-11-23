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
            var shell = new Color(0.19f, 0.33f, 0.18f);
            LunarArt.Rock(root, new Vector3(0f, 0.7f, -0.1f), new Vector3(1.45f, 0.93f, 1.8f), shell);
            for (int plate = 0; plate < 3; plate++)
                LunarArt.Armor(root, new Vector3(0f, 1.04f, -0.6f + plate * 0.43f), new Vector3(1.08f, 0.28f, 0.42f), MainColor);
            for (int side = -1; side <= 1; side += 2)
            {
                LunarArt.LightStrip(root, new Vector3(side * 0.25f, 0.86f, 0.73f), Vector3.one * 0.19f, new Color(1f, 0.86f, 0.24f));
                for (int leg = -1; leg <= 1; leg++)
                {
                    var joint = LunarArt.Armor(root, new Vector3(side * 0.72f, 0.48f, leg * 0.43f),
                        new Vector3(0.75f, 0.18f, 0.23f), shell);
                    joint.transform.localRotation = Quaternion.Euler(0f, leg * 23f * side, -side * 28f);
                    var claw = LunarArt.Armor(root, new Vector3(side * 0.97f, 0.23f, leg * 0.49f),
                        new Vector3(0.19f, 0.47f, 0.22f), MainColor);
                    claw.transform.localRotation = Quaternion.Euler(0f, 0f, -side * 17f);
                }
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
