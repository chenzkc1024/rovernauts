using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rovernauts
{
    [Serializable]
    internal sealed class LanSnapshot
    {
        static int lastObjectId;
        public static int NewObjectId() => ++lastObjectId;

        public int version = 1;
        public GameState state;
        public bool expedition, godMode;
        public int wave, queued, cleared, kills, score;
        public float time, countdown, hull, maxHull, boost;
        public MoonEvent moon;
        public Vector3 roverPosition, aim, velocity;
        public Quaternion roverRotation;
        public string[] upgrades, offers;
        public List<EnemyView> enemies = new List<EnemyView>();
        public List<ShotView> shots = new List<ShotView>();
        public List<MeteorView> meteors = new List<MeteorView>();
        public List<Cue> cues = new List<Cue>();

        [Serializable]
        internal struct EnemyView
        {
            public int id;
            public EnemyType type;
            public Vector3 position, bodyScale, bodyOffset, laneStart, laneEnd;
            public Quaternion rotation;
            public float hp, maxHp;
            public bool enraged, laneVisible;
        }

        [Serializable]
        internal struct ShotView
        {
            public int id;
            public Vector3 position, scale;
            public Quaternion rotation;
            public Color color;
        }

        [Serializable]
        internal struct MeteorView
        {
            public int id;
            public Vector3 target, rockPosition;
            public Quaternion rockRotation;
            public float radius, pulse;
            public bool falling;
        }

        [Serializable]
        internal struct Cue
        {
            public string name;
            public float volume;
        }

        public void Capture(GameManager gm)
        {
            state = gm.State;
            expedition = gm.Expedition;
            godMode = gm.GodMode;
            wave = gm.Waves.Wave;
            queued = gm.Waves.QueuedCount;
            cleared = gm.Waves.Cleared;
            time = gm.RunTime;
            countdown = gm.Waves.CountdownLeft;
            kills = gm.Kills;
            score = gm.Score;
            moon = gm.Events.Active;
            var rover = gm.Rover;
            hull = rover.Health.Current;
            maxHull = rover.Health.MaxHp;
            boost = rover.BoostReady;
            roverPosition = rover.transform.position;
            roverRotation = rover.transform.rotation;
            velocity = rover.Velocity;
            aim = rover.MainTurret.AimDirection;
            upgrades = gm.Upgrades.Taken.ToArray();
            offers = gm.Upgrades.Offer.ConvertAll(upgrade => upgrade.Name).ToArray();
            enemies.Clear();
            foreach (var enemy in EnemyBase.All) enemies.Add(enemy.CaptureLanView());
            Projectile.CaptureLanViews(shots);
            Meteor.CaptureLanViews(meteors);
        }
    }

    /// <summary>Interpolates visual replicas between snapshots. Only the host runs the simulation.</summary>
    internal sealed class LanWorld
    {
        internal const float Interval = 1f / 25f;
        readonly Dictionary<int, Replica<EnemyBase, LanSnapshot.EnemyView>> enemies = new();
        readonly Dictionary<int, Replica<Transform, LanSnapshot.ShotView>> shots = new();
        readonly Dictionary<int, Replica<Meteor, LanSnapshot.MeteorView>> meteors = new();
        readonly List<int> removed = new();
        Vector3 oldPosition;
        Quaternion oldRotation;
        LanSnapshot snapshot;
        float receivedAt;

        internal LanSnapshot Latest => snapshot;

        public void Receive(GameManager gm, LanSnapshot next)
        {
            var rover = gm.Rover;
            oldPosition = snapshot == null ? next.roverPosition : rover.transform.position;
            oldRotation = snapshot == null ? next.roverRotation : rover.transform.rotation;
            snapshot = next;
            receivedAt = Time.unscaledTime;
            Sync(enemies, next.enemies, v => v.id, EnemyBase.CreateLanReplica, (enemy, v) => enemy.ApplyLanView(v));
            Sync(shots, next.shots, v => v.id, Projectile.CreateLanReplica, (shot, v) =>
                shot.SetPositionAndRotation(v.position, v.rotation));
            Sync(meteors, next.meteors, v => v.id, Meteor.CreateLanReplica, (meteor, v) => meteor.ApplyLanView(v));

            gm.ApplyLanState(next);
            foreach (var cue in next.cues) AudioManager.Play(cue.name, cue.volume);
        }

        public void Tick(Rover rover)
        {
            if (snapshot == null) return;
            float fraction = Mathf.Clamp01((Time.unscaledTime - receivedAt) / Interval);
            rover.transform.SetPositionAndRotation(Vector3.Lerp(oldPosition, snapshot.roverPosition, fraction),
                Quaternion.Slerp(oldRotation, snapshot.roverRotation, fraction));
            rover.MainTurret.ApplyLanAim(snapshot.aim);

            foreach (var replica in enemies.Values)
                Move(replica, replica.View.position, replica.View.rotation, fraction);
            foreach (var replica in shots.Values)
                Move(replica, replica.View.position, replica.View.rotation, fraction);
        }

        static void Move<T, TView>(Replica<T, TView> replica, Vector3 position, Quaternion rotation, float fraction)
            where T : Component
        {
            replica.Object.transform.SetPositionAndRotation(Vector3.Lerp(replica.Position, position, fraction),
                Quaternion.Slerp(replica.Rotation, rotation, fraction));
        }

        void Sync<T, TView>(Dictionary<int, Replica<T, TView>> replicas, List<TView> views,
            Func<TView, int> getId, Func<TView, T> create, Action<T, TView> apply)
            where T : Component
        {
            foreach (var view in views)
            {
                int id = getId(view);
                if (!replicas.TryGetValue(id, out var replica))
                {
                    replica = new Replica<T, TView> { Object = create(view) };
                    replicas.Add(id, replica);
                }
                replica.View = view;
                replica.Position = replica.Object.transform.position;
                replica.Rotation = replica.Object.transform.rotation;
                replica.Seen = true;
                apply(replica.Object, view);
            }
            removed.Clear();
            foreach (var pair in replicas)
            {
                if (pair.Value.Seen) pair.Value.Seen = false;
                else
                {
                    if (pair.Value.Object is EnemyBase enemy) EnemyBase.All.Remove(enemy);
                    UnityEngine.Object.Destroy(pair.Value.Object.gameObject);
                    removed.Add(pair.Key);
                }
            }
            foreach (int id in removed) replicas.Remove(id);
        }

        sealed class Replica<T, TView>
        {
            public T Object;
            public TView View;
            public Vector3 Position;
            public Quaternion Rotation;
            public bool Seen;
        }
    }
}
