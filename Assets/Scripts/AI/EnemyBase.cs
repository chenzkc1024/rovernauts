using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rovernauts
{
    public enum EnemyType { Crawler, Spitter, Dasher, Boss, MegaBoss }

    public struct EnemyStats
    {
        public float hp, speed, damage;

        public EnemyStats(float hp, float speed, float damage)
        {
            this.hp = hp;
            this.speed = speed;
            this.damage = damage;
        }
    }

    /// <summary>
    /// Shared plumbing for every alien: health, steering, the FSM, A* navigation and debug drawing.
    /// Subclasses only build their body and their states.
    /// </summary>
    public abstract class EnemyBase : MonoBehaviour
    {
        public static readonly List<EnemyBase> All = new List<EnemyBase>();
        /// <summary>"Crawler: Chase -> Attack" => count. Dumped by the autotest as evidence the FSMs run.</summary>
        public static readonly Dictionary<string, int> TransitionLog = new Dictionary<string, int>();
        public static event Action<EnemyBase> AnyKilled;

        public Health Health { get; private set; }
        public SteeringAgent Agent { get; private set; }
        public string StateName => fsm.Current != null ? fsm.Current.Name : "-";
        public readonly List<Vector3> Path = new List<Vector3>();
        public abstract string DisplayName { get; }
        public virtual bool IsBoss => false;
        public virtual int Score => 10;

        protected readonly StateMachine fsm = new StateMachine();
        protected EnemyStats stats;
        protected Transform body;
        protected Vector3 steering;
        protected bool separate = true, avoidObstacles = true;
        protected State spawnState, wanderState;

        protected abstract Color MainColor { get; }
        protected abstract float BodyRadius { get; }
        protected abstract State EntryState { get; }
        protected abstract void BuildBody(Transform root);
        protected abstract void BuildStates();

        const float SpawnTime = 0.8f;
        static readonly RaycastHit[] losHits = new RaycastHit[8];
        Renderer[] renderers;
        Material[] baseMaterials;
        float flashTimer, repathTimer;
        int pathIndex;
        LineRenderer pathLine;

        protected Rover Target => Rover.Instance;
        protected bool TargetAlive => Target != null && !Target.Health.IsDead;
        protected Vector3 Pos => transform.position;
        protected Vector3 TargetPos => Target != null ? Target.transform.position : Pos;
        protected Vector3 ToTarget => (TargetPos - Pos).Flat();
        protected float DistToTarget => ToTarget.magnitude;
        /// <summary>Surface-to-surface distance between this enemy and the rover.</summary>
        protected float GapToTarget => DistToTarget - Rover.Radius - Agent.radius;

        public static EnemyBase Spawn(EnemyType type, Vector3 position, EnemyStats stats)
        {
            var go = new GameObject(type.ToString());
            go.transform.SetPositionAndRotation(position.WithY(0f), Quaternion.LookRotation((-position).Flat().normalized + Vector3.forward * 0.001f));
            EnemyBase enemy = type switch
            {
                EnemyType.Spitter => go.AddComponent<SpitterEnemy>(),
                EnemyType.Dasher => go.AddComponent<DasherEnemy>(),
                EnemyType.Boss => go.AddComponent<BossEnemy>(),
                EnemyType.MegaBoss => go.AddComponent<BossEnemy>().MakeMega(),
                _ => go.AddComponent<CrawlerEnemy>(),
            };
            enemy.Setup(stats);
            return enemy;
        }

        void Setup(EnemyStats s)
        {
            stats = s;
            Health = gameObject.AddComponent<Health>();
            Health.Init(Team.Enemy, s.hp);
            Health.Damaged += OnDamaged;
            Health.Died += OnDied;

            Agent = gameObject.AddComponent<SteeringAgent>();
            Agent.maxSpeed = s.speed;
            Agent.maxForce = s.speed * 5f;
            Agent.radius = BodyRadius;

            var col = gameObject.AddComponent<SphereCollider>();
            col.center = Vector3.up;
            col.radius = BodyRadius;
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            BuildBody(body);
            renderers = body.GetComponentsInChildren<Renderer>();
            baseMaterials = new Material[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) baseMaterials[i] = renderers[i].sharedMaterial;

            spawnState = new State("Spawn",
                enter: () =>
                {
                    Health.Invulnerable = true;
                    Fx.Burst(Pos + Vector3.up * 0.3f, new Color(0.55f, 0.5f, 0.45f), 6, 5f, 0.35f);
                },
                tick: dt =>
                {
                    float k = Mathf.Clamp01(fsm.TimeInState / SpawnTime);
                    body.localScale = Vector3.one * Mathf.SmoothStep(0.05f, 1f, k);
                    body.localPosition = Vector3.down * (1f - k) * 1.5f;
                    if (k >= 1f) fsm.Change(EntryState);
                },
                exit: () =>
                {
                    body.localScale = Vector3.one;
                    body.localPosition = Vector3.zero;
                    Health.Invulnerable = false;
                });
            wanderState = new State("Wander", tick: dt =>
            {
                steering += Agent.Wander();
                if (TargetAlive) fsm.Change(EntryState);
            });

            BuildStates();
            fsm.Changed += (from, to) =>
            {
                string key = $"{DisplayName}: {(from != null ? from.Name : "none")} -> {to.Name}";
                TransitionLog.TryGetValue(key, out int count);
                TransitionLog[key] = count + 1;
            };
            All.Add(this);
            fsm.Change(spawnState);
        }

        protected virtual void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || Health.IsDead) return;

            steering = Vector3.zero;
            if (!TargetAlive && fsm.Current != wanderState && fsm.Current != spawnState) fsm.Change(wanderState);
            fsm.Tick(dt);
            if (separate) steering += Agent.Separation(All, 0.6f) * 1.5f;
            if (avoidObstacles) steering += Agent.AvoidObstacles(3.5f);
            Agent.Integrate(steering, dt);

            if (flashTimer > 0f && (flashTimer -= dt) <= 0f) SetMaterials(null);
            DrawDebugPath();
        }

        /// <summary>
        /// Go to <paramref name="goal"/>: straight steering when the way is clear, otherwise follow an A* path
        /// that is re-planned every ~0.5 s (staggered so enemies don't all path on the same frame).
        /// </summary>
        protected Vector3 NavigateTo(Vector3 goal, float slowRadius = 0f)
        {
            var grid = GridPathfinder.Instance;
            if (grid == null || grid.HasLineOfWalk(Pos, goal))
            {
                Path.Clear();
                return slowRadius > 0f ? Agent.Arrive(goal, slowRadius) : Agent.Seek(goal);
            }
            repathTimer -= Time.deltaTime;
            if (repathTimer <= 0f || pathIndex >= Path.Count)
            {
                repathTimer = UnityEngine.Random.Range(0.4f, 0.7f);
                pathIndex = 0;
                if (!grid.FindPath(Pos, goal, Path) || Path.Count == 0) return Agent.Seek(goal);
            }
            return Agent.FollowPath(Path, ref pathIndex, 1.5f);
        }

        /// <summary>Physical line of sight to the rover (rocks block shots).</summary>
        protected bool HasLineOfSight()
        {
            Vector3 from = Pos + Vector3.up;
            Vector3 to = ToTarget;
            int n = Physics.RaycastNonAlloc(from, to.normalized, losHits, to.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (losHits[i].collider.GetComponentInParent<Obstacle>() != null) return false;
            return true;
        }

        protected void FaceTarget() => Agent.FaceDirection = ToTarget;

        protected void Shake(float amount) => body.localPosition = UnityEngine.Random.insideUnitSphere.Flat() * amount;

        /// <summary>Swap every body part of one colour for another (boss enrage, charging glow, ...).</summary>
        protected void Recolor(Color from, Color to)
        {
            var source = Visuals.Mat(from);
            for (int i = 0; i < renderers.Length; i++)
                if (baseMaterials[i] == source) baseMaterials[i] = Visuals.Mat(to);
            SetMaterials(null);
        }

        void SetMaterials(Material overrideMaterial)
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].sharedMaterial = overrideMaterial != null ? overrideMaterial : baseMaterials[i];
        }

        void OnDamaged(float amount)
        {
            flashTimer = 0.06f;
            SetMaterials(Visuals.Mat(Color.white));
        }

        protected virtual void OnDied()
        {
            All.Remove(this);
            Fx.Burst(Pos + Vector3.up, MainColor, IsBoss ? 40 : 10, IsBoss ? 14f : 8f, IsBoss ? 0.6f : 0.3f, IsBoss ? 1.4f : 0.7f);
            AudioManager.Play("explode", IsBoss ? 1f : 0.45f);
            if (IsBoss)
            {
                Fx.Shockwave(Pos, MainColor, 12f, 0.8f);
                CameraRig.Shake(1f);
            }
            AnyKilled?.Invoke(this);
            Destroy(gameObject);
        }

        protected virtual void OnDestroy() => All.Remove(this);

        void DrawDebugPath()
        {
            bool show = GameManager.I != null && GameManager.I.AIDebug && Path.Count > 0;
            if (!show)
            {
                if (pathLine != null) pathLine.enabled = false;
                return;
            }
            if (pathLine == null) pathLine = Visuals.Line(transform, new Color(0.2f, 1f, 0.4f), 0.15f);
            pathLine.enabled = true;
            int remaining = Mathf.Max(0, Path.Count - pathIndex);
            pathLine.positionCount = remaining + 1;
            pathLine.SetPosition(0, Pos.WithY(0.3f));
            for (int i = 0; i < remaining; i++) pathLine.SetPosition(i + 1, Path[pathIndex + i].WithY(0.3f));
        }
    }
}
