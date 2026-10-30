using System.Collections.Generic;
using UnityEngine;

namespace Rovernauts
{
    /// <summary>
    /// Reynolds-style steering. Each behaviour returns a steering force (desired velocity minus current velocity);
    /// the owning FSM state sums the forces it wants and <see cref="Integrate"/> applies them once per frame.
    /// </summary>
    public class SteeringAgent : MonoBehaviour
    {
        public float maxSpeed = 6f;
        public float maxForce = 30f;
        public float radius = 0.8f;
        public float turnSpeed = 540f;
        public Vector3 Velocity;

        /// <summary>Set by a state that wants to face somewhere other than its movement direction (e.g. aiming).</summary>
        public Vector3? FaceDirection;

        static readonly Collider[] overlap = new Collider[16];
        static readonly RaycastHit[] rayHits = new RaycastHit[8];
        float wanderAngle;

        Vector3 Pos => transform.position;

        public Vector3 Seek(Vector3 target)
        {
            Vector3 to = (target - Pos).Flat();
            if (to.sqrMagnitude < 0.0001f) return -Velocity;
            return to.normalized * maxSpeed - Velocity;
        }

        public Vector3 Flee(Vector3 threat)
        {
            Vector3 away = (Pos - threat).Flat();
            if (away.sqrMagnitude < 0.0001f) away = transform.right;
            return away.normalized * maxSpeed - Velocity;
        }

        /// <summary>Seek that slows down inside <paramref name="slowRadius"/> and stops on the target.</summary>
        public Vector3 Arrive(Vector3 target, float slowRadius)
        {
            Vector3 to = (target - Pos).Flat();
            float d = to.magnitude;
            if (d < 0.05f) return -Velocity;
            float speed = maxSpeed * Mathf.Clamp01(d / slowRadius);
            return to / d * speed - Velocity;
        }

        /// <summary>Where a moving target will be by the time we could reach it.</summary>
        public Vector3 Predict(Vector3 targetPos, Vector3 targetVelocity, float maxLookAhead = 1f)
        {
            float d = (targetPos - Pos).Flat().magnitude;
            float t = Mathf.Min(maxLookAhead, d / Mathf.Max(maxSpeed, 0.1f));
            return targetPos + targetVelocity.Flat() * t;
        }

        public Vector3 Brake(float strength = 1f) => -Velocity * 4f * strength;

        /// <summary>Circle a point at a fixed radius; <paramref name="direction"/> is +1 or -1.</summary>
        public Vector3 Orbit(Vector3 center, float orbitRadius, float direction)
        {
            Vector3 fromCenter = (Pos - center).Flat();
            if (fromCenter.sqrMagnitude < 0.01f) fromCenter = Vector3.right;
            Vector3 normal = fromCenter.normalized;
            Vector3 tangent = Vector3.Cross(Vector3.up, normal) * direction;
            Vector3 radial = (center + normal * orbitRadius - Pos).Flat();
            Vector3 desired = Vector3.ClampMagnitude(tangent * maxSpeed * 0.75f + radial * 1.5f, maxSpeed);
            return desired - Velocity;
        }

        public Vector3 Wander()
        {
            wanderAngle += Random.Range(-1f, 1f) * 240f * Time.deltaTime;
            Vector3 ahead = transform.forward * 3f;
            Vector3 jitter = Quaternion.Euler(0f, wanderAngle, 0f) * Vector3.forward * 1.5f;
            return Vector3.ClampMagnitude(ahead + jitter, maxSpeed * 0.4f) - Velocity;
        }

        public Vector3 FollowPath(List<Vector3> path, ref int index, float reachDistance)
        {
            while (index < path.Count - 1 && (path[index] - Pos).Flat().magnitude < reachDistance) index++;
            if (index >= path.Count) return Vector3.zero;
            return index == path.Count - 1 ? Arrive(path[index], 2f) : Seek(path[index]);
        }

        /// <summary>Push away from nearby enemies so packs spread out instead of stacking.</summary>
        public Vector3 Separation(IReadOnlyList<EnemyBase> others, float padding)
        {
            Vector3 push = Vector3.zero;
            for (int i = 0; i < others.Count; i++)
            {
                var other = others[i].Agent;
                if (other == null || other == this) continue;
                Vector3 away = (Pos - other.transform.position).Flat();
                float range = radius + other.radius + padding;
                float d = away.magnitude;
                if (d >= range) continue;
                if (d < 0.001f) away = Random.insideUnitSphere.Flat();
                push += away.normalized * (1f - d / range);
            }
            return push * maxSpeed;
        }

        /// <summary>Three whisker raycasts; steer along the surface normal of any obstacle they touch.</summary>
        public Vector3 AvoidObstacles(float lookAhead)
        {
            float speed = Velocity.magnitude;
            if (speed < 0.1f) return Vector3.zero;
            Vector3 forward = Velocity / speed;
            float length = radius + lookAhead * Mathf.Clamp01(speed / maxSpeed);
            Vector3 force = Vector3.zero;
            for (int w = -1; w <= 1; w++)
            {
                Vector3 dir = Quaternion.Euler(0f, w * 35f, 0f) * forward;
                float len = w == 0 ? length : length * 0.6f;
                int n = Physics.RaycastNonAlloc(Pos + Vector3.up, dir, rayHits, len, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    if (rayHits[i].collider.GetComponentInParent<Obstacle>() == null) continue;
                    Vector3 normal = rayHits[i].normal.Flat();
                    // A head-on hit gives a normal straight back at us; nudge sideways so we pick a way around.
                    if (Vector3.Dot(normal.normalized, -dir) > 0.95f) normal += Vector3.Cross(Vector3.up, dir) * 0.5f;
                    force += normal.normalized * (1f - rayHits[i].distance / len) * maxSpeed * 1.5f;
                }
            }
            return force;
        }

        public void Integrate(Vector3 force, float dt)
        {
            force = Vector3.ClampMagnitude(force.Flat(), maxForce);
            Velocity = Vector3.ClampMagnitude(Velocity + force * dt, maxSpeed);
            Vector3 next = ResolveObstacles(Pos + Velocity * dt);
            float limit = WorldBuilder.HalfSize - 1f;
            next.x = Mathf.Clamp(next.x, -limit, limit);
            next.z = Mathf.Clamp(next.z, -limit, limit);
            transform.position = next.WithY(0f);

            Vector3 face = (FaceDirection ?? Velocity).Flat();
            FaceDirection = null;
            if (face.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face), turnSpeed * dt);
        }

        /// <summary>Hard constraint: never end a frame inside a rock. Slides along the surface instead.</summary>
        Vector3 ResolveObstacles(Vector3 next)
        {
            Vector3 center = next + Vector3.up;
            int n = Physics.OverlapSphereNonAlloc(center, radius, overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (overlap[i].GetComponentInParent<Obstacle>() == null) continue;
                Vector3 closest = overlap[i].ClosestPoint(center);
                Vector3 away = (center - closest).Flat();
                float d = away.magnitude;
                if (d < 0.0001f)
                {
                    // Centre is inside the collider: push out from the collider's middle.
                    away = (center - overlap[i].bounds.center).Flat();
                    if (away.sqrMagnitude < 0.0001f) away = Vector3.right;
                    d = 0f;
                }
                if (d >= radius) continue;
                Vector3 normal = away.normalized;
                center += normal * (radius - d);
                float into = Vector3.Dot(Velocity, normal);
                if (into < 0f) Velocity -= normal * into;
            }
            return center - Vector3.up;
        }
    }
}
