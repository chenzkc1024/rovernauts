using System.Collections.Generic;
using UnityEngine;

namespace Rovernauts
{
    /// <summary>
    /// Runs the survival loop as its own state machine:
    ///   Countdown -> Spawning -> Clearing -> (AwaitUpgrade) -> Intermission -> next wave.
    /// Progression triggers: a boss every 5th wave, a tougher Void Titan every 10th.
    /// Time trigger: enemy HP/damage/speed keep scaling with total time survived in the run.
    /// </summary>
    public class WaveDirector : MonoBehaviour
    {
        public static WaveDirector I { get; private set; }

        public int Wave { get; private set; }
        public bool IsBossWave => Wave % 5 == 0;
        public bool IsMegaBossWave => Wave % 10 == 0;
        public int QueuedCount => queue.Count;
        public string PhaseName => fsm.Current != null ? fsm.Current.Name : "-";
        public float CountdownLeft => fsm.Current == countdown ? Mathf.Max(0f, CountdownTime - fsm.TimeInState) : 0f;
        public BossEnemy ActiveBoss { get; private set; }

        public float HpMultiplier => (1f + 0.14f * (Wave - 1)) * (1f + RunTime / 300f);
        public float DamageMultiplier => (1f + 0.06f * (Wave - 1) + RunTime / 600f) * Events.DamageMultiplier;
        public float SpeedMultiplier => Mathf.Min(1.45f, 1f + 0.015f * (Wave - 1) + RunTime / 900f) * Events.SpeedMultiplier;

        const float CountdownTime = 3f, IntermissionTime = 2.5f, SpawnRadius = 45f;
        const int MaxAlive = 45;
        readonly StateMachine fsm = new StateMachine();
        readonly Queue<EnemyType> queue = new Queue<EnemyType>();
        State idle, countdown, spawning, clearing, awaitUpgrade, intermission;
        float spawnTimer;

        static float RunTime => GameManager.I != null ? GameManager.I.RunTime : 0f;
        static EventDirector Events => GameManager.I.Events;

        void Awake()
        {
            I = this;
            idle = new State("Idle");
            countdown = new State("Countdown", tick: dt =>
            {
                if (fsm.TimeInState >= CountdownTime) fsm.Change(spawning);
            });
            spawning = new State("Spawning",
                enter: () => spawnTimer = 0f,
                tick: dt =>
                {
                    if ((spawnTimer -= dt) <= 0f && EnemyBase.All.Count < MaxAlive)
                    {
                        SpawnGroup();
                        spawnTimer = Mathf.Max(0.35f, 1.3f - Wave * 0.06f);
                    }
                    if (queue.Count == 0) fsm.Change(clearing);
                });
            clearing = new State("Clearing", tick: dt =>
            {
                if (EnemyBase.All.Count == 0) WaveCleared();
            });
            awaitUpgrade = new State("Upgrade");
            intermission = new State("Intermission", tick: dt =>
            {
                if (fsm.TimeInState >= IntermissionTime) NextWave();
            });
            fsm.Change(idle);
        }

        void OnDestroy()
        {
            if (I == this) I = null;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (gm != null && gm.State == GameState.Playing) fsm.Tick(Time.deltaTime);
        }

        public void Begin(int startWave)
        {
            Wave = Mathf.Max(0, startWave - 1);
            NextWave();
        }

        public void ResumeAfterUpgrade() => fsm.Change(intermission);

        /// <summary>Dev shortcut (F3): wipe the field and jump to the next boss wave.</summary>
        public void SkipToNextBoss()
        {
            foreach (var e in EnemyBase.All.ToArray()) Destroy(e.gameObject);
            EnemyBase.All.Clear();
            queue.Clear();
            Events.OnWaveEnd();
            Wave = (Wave / 5 + 1) * 5 - 1;
            NextWave();
        }

        void NextWave()
        {
            Wave++;
            BuildQueue();
            var moonEvent = Events.OnWaveStart(Wave);
            if (IsBossWave)
            {
                string boss = IsMegaBossWave ? "VOID TITAN" : "CRATER BRUTE";
                HUD.Banner($"WAVE {Wave}: BOSS", $"The {boss} is coming", new Color(1f, 0.3f, 0.25f));
                AudioManager.Play("alarm", 0.8f, 0f);
            }
            else if (moonEvent == MoonEvent.None)
            {
                HUD.Banner($"WAVE {Wave}", $"{queue.Count} hostiles incoming", Color.white);
            }
            fsm.Change(countdown);
        }

        void BuildQueue()
        {
            queue.Clear();
            if (IsMegaBossWave) queue.Enqueue(EnemyType.MegaBoss);
            else if (IsBossWave) queue.Enqueue(EnemyType.Boss);

            int count = IsBossWave ? 3 + Wave / 2 : 5 + Wave * 2;
            float dasherChance = Wave >= 3 ? Mathf.Min(0.25f, 0.08f + 0.02f * Wave) : 0f;
            float spitterChance = Wave >= 2 ? Mathf.Min(0.35f, 0.15f + 0.03f * Wave) : 0f;
            for (int i = 0; i < count; i++)
            {
                float roll = Random.value;
                if (roll < dasherChance) queue.Enqueue(EnemyType.Dasher);
                else if (roll < dasherChance + spitterChance) queue.Enqueue(EnemyType.Spitter);
                else queue.Enqueue(EnemyType.Crawler);
            }
        }

        void SpawnGroup()
        {
            Vector3 point = EdgeSpawnPoint();
            var first = queue.Peek();
            int groupSize = first == EnemyType.Boss || first == EnemyType.MegaBoss ? 1 : Random.Range(1, 4);
            for (int i = 0; i < groupSize && queue.Count > 0; i++)
            {
                var type = queue.Dequeue();
                Vector3 jitter = i == 0 ? Vector3.zero : Random.insideUnitSphere.Flat() * 2.5f;
                var enemy = EnemyBase.Spawn(type, point + jitter, StatsFor(type));
                if (enemy is BossEnemy boss)
                {
                    ActiveBoss = boss;
                    HUD.Banner(boss.DisplayName.ToUpper(), "Survive its barrages, dodge its charge", new Color(1f, 0.3f, 0.25f));
                }
            }
            AudioManager.Play("spawn", 0.3f);
        }

        /// <summary>Spawns a boss minion; it counts toward the current wave like any other enemy.</summary>
        public EnemyBase SpawnMinion(EnemyType type, Vector3 position)
        {
            float limit = WorldBuilder.HalfSize - 3f;
            position.x = Mathf.Clamp(position.x, -limit, limit);
            position.z = Mathf.Clamp(position.z, -limit, limit);
            return EnemyBase.Spawn(type, position, StatsFor(type));
        }

        public EnemyStats StatsFor(EnemyType type)
        {
            var s = type switch
            {
                EnemyType.Spitter => new EnemyStats(24f, 5.5f, 8f),
                EnemyType.Dasher => new EnemyStats(34f, 8f, 14f),
                EnemyType.Boss => new EnemyStats(650f, 4.6f, 9f),
                EnemyType.MegaBoss => new EnemyStats(1500f, 5f, 12f),
                _ => new EnemyStats(30f, 6.5f, 6f),
            };
            s.hp *= HpMultiplier;
            s.damage *= DamageMultiplier;
            s.speed *= SpeedMultiplier;
            return s;
        }

        /// <summary>Random walkable point on the arena rim, away from the rover so spawns are never unfair.</summary>
        Vector3 EdgeSpawnPoint()
        {
            Vector3 rover = Rover.Instance != null ? Rover.Instance.transform.position : Vector3.zero;
            Vector3 point = Vector3.forward * SpawnRadius;
            for (int tries = 0; tries < 20; tries++)
            {
                float a = Random.value * Mathf.PI * 2f;
                point = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * SpawnRadius;
                var grid = GridPathfinder.Instance;
                if ((grid == null || grid.IsWalkable(point)) && (point - rover).Flat().magnitude > 22f) break;
            }
            return point;
        }

        void WaveCleared()
        {
            ActiveBoss = null;
            bool hadEvent = Events.OnWaveEnd();
            var rover = Rover.Instance;
            if (rover != null) rover.Health.Heal(rover.Health.MaxHp * 0.08f);
            if (IsBossWave || hadEvent)
            {
                fsm.Change(awaitUpgrade);
                GameManager.I.OfferUpgrades();
            }
            else
            {
                HUD.Banner($"WAVE {Wave} CLEARED", "Hull patched +8%", new Color(0.5f, 1f, 0.6f));
                fsm.Change(intermission);
            }
        }
    }
}
