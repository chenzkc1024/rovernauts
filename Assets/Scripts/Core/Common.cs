using UnityEngine;

namespace Rovernauts
{
    public enum Team { Player, Enemy, Neutral }

    public static class VectorExtensions
    {
        /// <summary>Drops the vertical component. The whole game plays out on the XZ plane.</summary>
        public static Vector3 Flat(this Vector3 v) => new Vector3(v.x, 0f, v.z);

        public static Vector3 WithY(this Vector3 v, float y) => new Vector3(v.x, y, v.z);
    }
}
