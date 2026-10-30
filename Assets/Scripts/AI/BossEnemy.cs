using System.Collections.Generic;
using UnityEngine;

namespace Rovernauts
{
    /// <summary>
    /// Boss with attack patterns and two phases. FSM:
    ///   Spawn -> Intro -> Pursue -> { Barrage | Summon | ChargeWindUp -> Charge } -> Recover -> Pursue
    ///   any combat state -> Enrage (once, at 50% HP) -> Pursue with faster, denser attacks.
    /// Pursue picks the next attack with a small rule-based decision (distance, cooldowns, minion count).
    /// The Mega variant ("Void Titan", every 10th wave) is bigger, tougher and fires extra aimed volleys.
    /// </summary>
    public class BossEnemy : EnemyBase
    {
        public override string DisplayName => mega ? "Void Titan" : "Crater Brute";
        public override bool IsBoss => true;
        public override int Score => mega ? 500 : 200;
        public bool Enraged { get; private set; }
        protected override Color MainColor => mega ? new Color(0.35f, 0.15f, 0.6f) : new Color(0.75f, 0.12f, 0.12f);
        protected override float BodyRadius => 2.2f * Size;
        protected override State EntryState => intro;

        static readonly Color EnragedColor = new Color(1f, 0.3f, 0.05f);
        static readonly Color ShotColor = new Color(1f, 0.6f, 0.1f);
        bool mega;
        float Size => mega ? 1.35f : 1f;
        State intro, pursue, barrage, summon, chargeWindUp, charge, recover, enrage;
        readonly List<EnemyBase> minions = new List<EnemyBase>();
        float attackCooldown = 1.5f, summonCooldown = 4f, chargeCooldown = 3f, pulseTimer, ringOffset;
        int pulsesLeft;
        bool chargeConnected;
        Vector3 chargeDirection;

        public BossEnemy MakeMega()
        {
            mega = true;
            return this;
        }

        protected override void BuildBody(Transform root)
        {
            float s = Size;
            Visuals.Part(PrimitiveType.Sphere, root, new Vector3(0f, 2f * s, 0f), Vector3.one * 4.2f * s, MainColor);
            Visuals.Part(PrimitiveType.Sphere, root, new Vector3(0f, 2.3f * s, 1.7f * s), Vector3.one * 1.1f * s, new Color(1f, 0.85f, 0.2f), castShadows: false);
            var spike = mega ? new Color(0.15f, 0.05f, 0.25f) : new Color(0.35f, 0.05f, 0.05f);
            for (int i = 0; i < 8; i++)
            {
                var t = Visuals.Part(PrimitiveType.Cube, root, Vector3.zero, new Vector3(0.5f, 0.5f, 1.8f) * s, spike).transform;
                t.localRotation = Quaternion.Euler(-20f, i * 45f, 0f);
                t.localPosition = new Vector3(0f, 2.2f * s, 0f) + t.forward * 2.1f * s;
            }
            if (mega)
                Visuals.Part(PrimitiveType.Cylinder, root, new Vector3(0f, 2f * s, 0f), new Vector3(6.5f, 0.05f, 6.5f) * s, new Color(0.8f, 0.5f, 1f));
        }

        protected override void BuildStates()
        {
            intro = new State("Intro",
                enter: () =>
                {
                    Health.Invulnerable = true;
                    AudioManager.Play("boss", 1f, 0f);
                    CameraRig.Shake(0.6f);
                    Fx.Shockwave(Pos, MainColor, 10f, 1f);
                },
                tick: dt =>
                {
                    Shake(0.15f);
                    if (fsm.TimeInState >= 1.5f) fsm.Change(pursue);
                },
                exit: () =>
                {
                    Health.Invulnerable = false;
                    body.localPosition = Vector3.zero;
                });

            pursue = new State("Pursue", tick: dt =>
            {
                steering += NavigateTo(TargetPos, 6f);
                if (TryEnrage()) return;
                attackCooldown -= dt;
                summonCooldown -= dt;
                chargeCooldown -= dt;
                if (attackCooldown > 0f) return;

                minions.RemoveAll(m => m == null);
                if (DistToTarget > 16f && chargeCooldown <= 0f && HasLineOfSight()) fsm.Change(chargeWindUp);
                else if (summonCooldown <= 0f && minions.Count < (Enraged ? 6 : 4)) fsm.Change(summon);
                else if (DistToTarget < 28f) fsm.Change(barrage);
            });

            barrage = new State("Barrage",
                enter: () =>
                {
                    pulsesLeft = (Enraged ? 5 : 3) + (mega ? 1 : 0);
                    pulseTimer = 0.2f;
                },
                tick: dt =>
                {
                    steering += Agent.Brake();
                    if (TryEnrage()) return;
                    if ((pulseTimer -= dt) > 0f) return;
                    FireRing();
                    pulseTimer = Enraged ? 0.35f : 0.45f;
                    if (--pulsesLeft <= 0) fsm.Change(recover);
                });

            summon = new State("Summon",
                tick: dt =>
                {
                    steering += Agent.Brake();
                    Shake(0.1f);
                    body.localScale = Vector3.one * (1f + Mathf.Sin(fsm.TimeInState * 25f) * 0.04f);
                    if (fsm.TimeInState < 1.1f) return;
                    SummonMinions();
                    summonCooldown = Enraged ? 7f : 10f;
                    fsm.Change(recover);
                },
                exit: () =>
                {
                    body.localScale = Vector3.one;
                    body.localPosition = Vector3.zero;
                });

            chargeWindUp = new State("ChargeWindUp",
                tick: dt =>
                {
                    steering += Agent.Brake();
                    float windUp = Enraged ? 0.6f : 0.9f;
                    if (fsm.TimeInState < windUp - 0.25f) chargeDirection = ToTarget.normalized;
                    Agent.FaceDirection = chargeDirection;
                    Shake(0.25f);
                    if (fsm.TimeInState >= windUp) fsm.Change(charge);
                },
                exit: () => body.localPosition = Vector3.zero);

            charge = new State("Charge",
                enter: () =>
                {
                    Agent.maxSpeed = 24f;
                    Agent.Velocity = chargeDirection * 24f;
                    separate = false;
                    chargeConnected = false;
                    AudioManager.Play("dash", 0.8f, 0f);
                },
                tick: dt =>
                {
                    steering += chargeDirection * Agent.maxForce;
                    Agent.FaceDirection = chargeDirection;
                    if (!chargeConnected && GapToTarget < 0.6f)
                    {
                        chargeConnected = true;
                        Target.TakeHit(stats.damage * 2.5f, chargeDirection * 14f);
                        CameraRig.Shake(0.6f);
                    }
                    if (fsm.TimeInState >= 0.85f) fsm.Change(recover);
                },
                exit: () =>
                {
                    Agent.maxSpeed = stats.speed;
                    separate = true;
                    chargeCooldown = Enraged ? 4.5f : 6.5f;
                });

            recover = new State("Recover", tick: dt =>
            {
                steering += Agent.Brake(0.7f);
                if (TryEnrage()) return;
                if (fsm.TimeInState < 0.6f) return;
                attackCooldown = Enraged ? 1.1f : 2.2f;
                fsm.Change(pursue);
            });

            enrage = new State("Enrage",
                enter: () =>
                {
                    Enraged = true;
                    Health.Invulnerable = true;
                    Recolor(MainColor, EnragedColor);
                    stats.speed *= 1.35f;
                    Agent.maxSpeed = stats.speed;
                    Agent.maxForce = stats.speed * 5f;
                    AudioManager.Play("boss", 1f, 0f);
                    Fx.Shockwave(Pos, EnragedColor, 14f, 0.9f);
                    CameraRig.Shake(0.8f);
                    HUD.Banner($"{DisplayName.ToUpper()} IS ENRAGED", "Faster attacks, denser barrages", EnragedColor);
                },
                tick: dt =>
                {
                    steering += Agent.Brake();
                    Shake(0.2f);
                    if (fsm.TimeInState >= 1.4f) fsm.Change(pursue);
                },
                exit: () =>
                {
                    Health.Invulnerable = false;
                    body.localPosition = Vector3.zero;
                    attackCooldown = 0.5f;
                });
        }

        bool TryEnrage()
        {
            if (Enraged || Health.Fraction > 0.5f) return false;
            fsm.Change(enrage);
            return true;
        }

        void FireRing()
        {
            int count = (Enraged ? 20 : 14) + (mega ? 6 : 0);
            ringOffset += 180f / count; // alternate rings interleave, forming a spiral
            Vector3 origin = Pos + Vector3.up;
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, ringOffset + i * 360f / count, 0f) * Vector3.forward;
                Projectile.Fire(origin + dir * (BodyRadius + 0.5f), dir, 11f, stats.damage, Team.Enemy, ShotColor, 0.7f, 4f);
            }
            if (mega && TargetAlive)
            {
                for (int i = -1; i <= 1; i++)
                {
                    Vector3 dir = Quaternion.Euler(0f, i * 8f, 0f) * ToTarget.normalized;
                    Projectile.Fire(origin + dir * (BodyRadius + 0.5f), dir, 18f, stats.damage, Team.Enemy, new Color(0.9f, 0.4f, 1f), 0.8f, 3f);
                }
            }
            AudioManager.Play("enemy_shot", 0.8f);
        }

        void SummonMinions()
        {
            if (WaveDirector.I == null) return;
            int crawlers = Enraged ? 2 : 3;
            int dashers = Enraged ? 2 : 0;
            for (int i = 0; i < crawlers + dashers; i++)
            {
                Vector3 offset = Quaternion.Euler(0f, i * 360f / (crawlers + dashers), 0f) * Vector3.forward * (BodyRadius + 2.5f);
                var type = i < crawlers ? EnemyType.Crawler : EnemyType.Dasher;
                minions.Add(WaveDirector.I.SpawnMinion(type, Pos + offset));
            }
            AudioManager.Play("spawn", 0.8f);
        }
    }
}
