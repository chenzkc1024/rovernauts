using System.Collections.Generic;
using UnityEngine;

namespace Rovernauts
{
    /// <summary>
    /// A* pathfinding over a uniform grid baked from the arena's <see cref="Obstacle"/> colliders.
    /// 8-connected, no corner cutting, octile heuristic, followed by line-of-sight string pulling so
    /// enemies walk natural straight lines instead of grid staircases.
    /// </summary>
    public class GridPathfinder
    {
        public static GridPathfinder Instance;

        public readonly float CellSize;
        public readonly int Width, Height;
        public int BlockedCount { get; private set; }

        const int MaxExpansions = 3000;
        const float Diagonal = 1.41421356f;
        static readonly Collider[] buffer = new Collider[32];

        readonly Vector3 origin;
        readonly bool[] walkable;
        // A* scratch arrays reused between searches; stamps avoid clearing them every call.
        readonly float[] gCost;
        readonly int[] parent, openStamp, closedStamp;
        readonly MinHeap open = new MinHeap();
        readonly List<Vector3> raw = new List<Vector3>();
        int searchId;
        GameObject debugView;

        public GridPathfinder(float halfSize, float cellSize, float agentRadius)
        {
            CellSize = cellSize;
            Width = Height = Mathf.CeilToInt(halfSize * 2f / cellSize);
            origin = new Vector3(-halfSize, 0f, -halfSize);
            int n = Width * Height;
            walkable = new bool[n];
            gCost = new float[n];
            parent = new int[n];
            openStamp = new int[n];
            closedStamp = new int[n];
            Bake(agentRadius);
        }

        public void Bake(float agentRadius)
        {
            Physics.SyncTransforms();
            float half = CellSize * 0.5f + agentRadius;
            BlockedCount = 0;
            for (int z = 0; z < Height; z++)
            for (int x = 0; x < Width; x++)
            {
                int hits = Physics.OverlapBoxNonAlloc(CellCenter(x, z) + Vector3.up, new Vector3(half, 0.9f, half), buffer,
                    Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                bool free = true;
                for (int i = 0; i < hits && free; i++)
                    if (buffer[i].GetComponentInParent<Obstacle>() != null) free = false;
                walkable[x + z * Width] = free;
                if (!free) BlockedCount++;
            }
        }

        public Vector3 CellCenter(int x, int z) => origin + new Vector3((x + 0.5f) * CellSize, 0f, (z + 0.5f) * CellSize);

        public Vector2Int ToCell(Vector3 p) =>
            new Vector2Int(Mathf.FloorToInt((p.x - origin.x) / CellSize), Mathf.FloorToInt((p.z - origin.z) / CellSize));

        public bool IsWalkable(Vector3 p)
        {
            var c = ToCell(p);
            return Walkable(c.x, c.y);
        }

        bool Walkable(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Height && walkable[x + z * Width];

        /// <summary>True if a straight walk from a to b crosses no blocked cell (endpoints excluded).</summary>
        public bool HasLineOfWalk(Vector3 a, Vector3 b)
        {
            Vector3 d = (b - a).Flat();
            int steps = Mathf.CeilToInt(d.magnitude / (CellSize * 0.4f));
            for (int i = 1; i < steps; i++)
                if (!IsWalkable(a + d * (i / (float)steps))) return false;
            return true;
        }

        /// <summary>Fills <paramref name="result"/> with smoothed waypoints from start (exclusive) to goal (inclusive).</summary>
        public bool FindPath(Vector3 from, Vector3 to, List<Vector3> result)
        {
            result.Clear();
            Vector2Int s = NearestWalkable(ToCell(from)), t = NearestWalkable(ToCell(to));
            if (s.x < 0 || t.x < 0) return false;
            int start = s.x + s.y * Width, goal = t.x + t.y * Width;

            searchId++;
            open.Clear();
            gCost[start] = 0f;
            parent[start] = -1;
            openStamp[start] = searchId;
            open.Push(start, Heuristic(start, goal));

            int expanded = 0;
            while (open.Count > 0)
            {
                int current = open.Pop();
                if (closedStamp[current] == searchId) continue; // stale duplicate entry
                closedStamp[current] = searchId;
                if (current == goal)
                {
                    BuildPath(start, goal, from, to, result);
                    return true;
                }
                if (++expanded > MaxExpansions) break;

                int cx = current % Width, cz = current / Width;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = cx + dx, nz = cz + dz;
                    if (!Walkable(nx, nz)) continue;
                    bool diagonal = dx != 0 && dz != 0;
                    if (diagonal && (!Walkable(cx + dx, cz) || !Walkable(cx, cz + dz))) continue; // no corner cutting
                    int next = nx + nz * Width;
                    if (closedStamp[next] == searchId) continue;
                    float cost = gCost[current] + (diagonal ? Diagonal : 1f);
                    if (openStamp[next] == searchId && cost >= gCost[next]) continue;
                    openStamp[next] = searchId;
                    gCost[next] = cost;
                    parent[next] = current;
                    open.Push(next, cost + Heuristic(next, goal));
                }
            }
            return false;
        }

        float Heuristic(int a, int b)
        {
            int dx = Mathf.Abs(a % Width - b % Width), dz = Mathf.Abs(a / Width - b / Width);
            return dx + dz + (Diagonal - 2f) * Mathf.Min(dx, dz); // octile distance
        }

        void BuildPath(int start, int goal, Vector3 from, Vector3 to, List<Vector3> result)
        {
            raw.Clear();
            for (int c = goal; c != start && c >= 0; c = parent[c]) raw.Add(CellCenter(c % Width, c / Width));
            raw.Add(from);
            raw.Reverse();
            raw[raw.Count - 1] = to; // replace the goal cell centre with the exact target

            // String pulling: from each anchor jump to the furthest waypoint still in straight-line reach.
            int anchor = 0;
            while (anchor < raw.Count - 1)
            {
                int next = raw.Count - 1;
                while (next > anchor + 1 && !HasLineOfWalk(raw[anchor], raw[next])) next--;
                result.Add(raw[next]);
                anchor = next;
            }
        }

        Vector2Int NearestWalkable(Vector2Int c)
        {
            c.x = Mathf.Clamp(c.x, 0, Width - 1);
            c.y = Mathf.Clamp(c.y, 0, Height - 1);
            if (Walkable(c.x, c.y)) return c;
            for (int r = 1; r <= 8; r++)
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue; // ring only
                if (Walkable(c.x + dx, c.y + dz)) return new Vector2Int(c.x + dx, c.y + dz);
            }
            return new Vector2Int(-1, -1);
        }

        /// <summary>Shows blocked cells as red tiles (AI debug view).</summary>
        public void SetDebugVisible(bool visible)
        {
            if (debugView == null)
            {
                if (!visible) return;
                debugView = new GameObject("Path Grid (debug)");
                var color = new Color(0.75f, 0.12f, 0.12f);
                for (int z = 0; z < Height; z++)
                for (int x = 0; x < Width; x++)
                    if (!walkable[x + z * Width])
                        Visuals.Part(PrimitiveType.Cube, debugView.transform, CellCenter(x, z).WithY(0.1f),
                            new Vector3(CellSize * 0.85f, 0.04f, CellSize * 0.85f), color, castShadows: false);
            }
            debugView.SetActive(visible);
        }

        /// <summary>Binary min-heap of (cell, priority). Duplicates are allowed and skipped when popped.</summary>
        class MinHeap
        {
            readonly List<int> items = new List<int>();
            readonly List<float> keys = new List<float>();

            public int Count => items.Count;

            public void Clear()
            {
                items.Clear();
                keys.Clear();
            }

            public void Push(int item, float key)
            {
                items.Add(item);
                keys.Add(key);
                int i = items.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (keys[p] <= keys[i]) break;
                    Swap(i, p);
                    i = p;
                }
            }

            public int Pop()
            {
                int top = items[0];
                int last = items.Count - 1;
                Swap(0, last);
                items.RemoveAt(last);
                keys.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, smallest = i;
                    if (l < items.Count && keys[l] < keys[smallest]) smallest = l;
                    if (r < items.Count && keys[r] < keys[smallest]) smallest = r;
                    if (smallest == i) break;
                    Swap(i, smallest);
                    i = smallest;
                }
                return top;
            }

            void Swap(int a, int b)
            {
                (items[a], items[b]) = (items[b], items[a]);
                (keys[a], keys[b]) = (keys[b], keys[a]);
            }
        }
    }
}
