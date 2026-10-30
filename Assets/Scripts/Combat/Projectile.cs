using System.Collections.Generic;
using UnityEngine;

namespace Rovernauts
{
    /// <summary>
    /// Straight-flying shot on the y = 1 combat plane. Uses sphere queries instead of rigidbody
    /// collisions so fast bullets never tunnel through thin enemies.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public const float PlaneHeight = 1f;

        static readonly RaycastHit[] castHits = new RaycastHit[24];
        static readonly Collider[] overlapHits = new Collider[24];
        static readonly HitSorter sorter = new HitSorter();

        readonly HashSet<Health> alreadyHit = new HashSet<Health>();
        Team owner;
        float damage, radius, lifeLeft;
        int pierceLeft;
        Vector3 velocity;
        Color color;

        public static void Fire(Vector3 position, Vector3 direction, float speed, float damage, Team owner, Color color,
            float size = 0.4f, float life = 2f, int pierce = 0)
        {
            var go = Visuals.Part(PrimitiveType.Sphere, null, position.WithY(PlaneHeight), Vector3.one * size, color, castShadows: false);
            go.name = owner == Team.Player ? "PlayerShot" : "EnemyShot";
            var p = go.AddComponent<Projectile>();
            p.owner = owner;
            p.damage = damage;
            p.velocity = direction.Flat().normalized * speed;
            p.radius = size * 0.5f;
            p.lifeLeft = life;
            p.pierceLeft = pierce;
            p.color = color;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 pos = transform.position;

            // Catch anything we're already inside (point-blank shots), then sweep along this frame's movement.
            int n = Physics.OverlapSphereNonAlloc(pos, radius, overlapHits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (TryHit(overlapHits[i], pos)) return;

            n = Physics.SphereCastNonAlloc(pos, radius, velocity.normalized, castHits, velocity.magnitude * dt, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(castHits, 0, n, sorter);
            for (int i = 0; i < n; i++)
                if (TryHit(castHits[i].collider, castHits[i].point)) return;

            transform.position = pos + velocity * dt;
            lifeLeft -= dt;
            if (lifeLeft <= 0f) Destroy(gameObject);
        }

        /// <summary>Returns true when the projectile has been used up.</summary>
        bool TryHit(Collider col, Vector3 point)
        {
            var target = col.GetComponentInParent<Health>();
            if (target == null)
            {
                if (col.GetComponentInParent<Obstacle>() == null) return false;
                Fx.Burst(point, color, 3, 4f, 0.15f, 0.3f);
                Destroy(gameObject);
                return true;
            }
            if (target.team == owner || target.IsDead || !alreadyHit.Add(target)) return false;

            target.TakeDamage(damage);
            Fx.Burst(point, color, 4, 6f, 0.15f, 0.3f);
            if (owner == Team.Player) AudioManager.Play("hit", 0.35f);
            if (pierceLeft-- > 0) return false;
            Destroy(gameObject);
            return true;
        }

        class HitSorter : IComparer<RaycastHit>
        {
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
