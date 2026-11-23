using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rovernauts
{
    /// <summary>Marks a scene root that stays put when the world wraps (the tiled terrain and the path grid view).</summary>
    public class WorldAnchor : MonoBehaviour { }

    /// <summary>
    /// Makes the moon endless. The terrain is one tile repeated on every side (see <see cref="WorldBuilder"/>),
    /// so the map has no visible edge. The rover always stays on the centre tile: when it crosses an edge,
    /// everything that moves (rover, camera, enemies, shots, meteors, effects) jumps one tile back together,
    /// which looks identical on screen. Enemies, shots and meteors are also kept on whichever repeat of the
    /// map is nearest the rover, so the swarm is never more than half a tile away. Driving off in a straight
    /// line brings it around in front of you; running away does not work.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class WorldWrap : MonoBehaviour
    {
        public static float Period => WorldBuilder.HalfSize * 2f;

        void Update()
        {
            var gm = GameManager.I;
            // The LAN gunner follows the host's coordinates (see LanWorld), so it never wraps on its own.
            if (gm == null || gm.Mode == CrewMode.LanGunner || gm.Rover == null) return;

            Vector3 rover = gm.Rover.transform.position;
            Vector3 offset = Vector3.zero;
            float half = WorldBuilder.HalfSize;
            if (rover.x > half) offset.x = -Period;
            else if (rover.x < -half) offset.x = Period;
            if (rover.z > half) offset.z = -Period;
            else if (rover.z < -half) offset.z = Period;
            if (offset != Vector3.zero)
            {
                ShiftWorld(offset);
                rover += offset;
            }
            KeepNear(rover);
        }

        /// <summary>The whole-tile offset that brings <paramref name="position"/> to its repeat nearest <paramref name="anchor"/>.</summary>
        public static Vector3 NearestOffset(Vector3 position, Vector3 anchor)
        {
            float p = Period;
            return new Vector3(Mathf.Round((anchor.x - position.x) / p) * p, 0f, Mathf.Round((anchor.z - position.z) / p) * p);
        }

        /// <summary>Moves every moving object by <paramref name="offset"/> (a whole number of tiles). Used by the host and the LAN gunner.</summary>
        public static void ShiftWorld(Vector3 offset)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.GetComponent<WorldAnchor>() != null) continue;
                Move(root, offset);
            }
            foreach (var enemy in EnemyBase.All) enemy.ShiftCachedPositions(offset);
            Meteor.ShiftCachedPositions(offset);
            CameraRig.I?.ShiftBy(offset);
            Physics.SyncTransforms();
        }

        /// <summary>Moves one object, its rigidbody and any world-space lines under it.</summary>
        public static void Move(GameObject go, Vector3 offset)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.position += offset;
            go.transform.position += offset;
            foreach (var line in go.GetComponentsInChildren<LineRenderer>(true))
            {
                if (!line.useWorldSpace) continue;
                for (int i = 0; i < line.positionCount; i++) line.SetPosition(i, line.GetPosition(i) + offset);
            }
        }

        static void KeepNear(Vector3 anchor)
        {
            for (int i = EnemyBase.All.Count - 1; i >= 0; i--)
            {
                var enemy = EnemyBase.All[i];
                if (enemy == null) continue;
                Vector3 offset = NearestOffset(enemy.transform.position, anchor);
                if (offset == Vector3.zero) continue;
                Move(enemy.gameObject, offset);
                enemy.ShiftCachedPositions(offset);
            }
            Projectile.KeepNear(anchor);
            Meteor.KeepNear(anchor);
        }
    }
}
