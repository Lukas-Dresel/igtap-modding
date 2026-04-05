using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.SceneManagement;

namespace IGTAPMinimap
{
    /// <summary>
    /// Scans tilemaps and scene objects to build a cached minimap texture.
    /// </summary>
    public static class MinimapData
    {
        public static Texture2D Texture { get; private set; }
        public static Vector2 WorldMin { get; private set; }
        public static Vector2 WorldMax { get; private set; }
        public static int TexWidth { get; private set; }
        public static int TexHeight { get; private set; }
        public static int CellSize { get; private set; } = 32;

        /// <summary>Per-course bounding box in world coordinates (min, max).</summary>
        public static List<CourseBounds> Courses { get; private set; } = new List<CourseBounds>();

        private static bool dirty = true;
        private static string lastScene;

        private static readonly Color colGround = new Color(0.45f, 0.45f, 0.5f, 1f);
        private static readonly Color colCheckpoint = new Color(0.2f, 1f, 0.4f, 1f);
        private static readonly Color colStartGate = new Color(1f, 1f, 0.3f, 1f);
        private static readonly Color colEndGate = new Color(0.3f, 0.6f, 1f, 1f);
        private static readonly Color colClear = new Color(0f, 0f, 0f, 0f);

        public struct CourseBounds
        {
            public string Name;
            public Vector2 Min;
            public Vector2 Max;
        }

        public static void MarkDirty()
        {
            dirty = true;
        }

        /// <summary>
        /// Ensures the minimap texture is up-to-date. Call each frame from the overlay.
        /// Returns false if no data is available yet.
        /// </summary>
        public static bool EnsureReady()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (scene != lastScene)
            {
                dirty = true;
                lastScene = scene;
            }

            if (!dirty && Texture != null)
                return true;

            dirty = false;
            return Scan();
        }

        /// <summary>
        /// Convert a world position to pixel coordinates in the minimap texture.
        /// </summary>
        public static Vector2 WorldToTex(Vector2 worldPos)
        {
            float px = (worldPos.x - WorldMin.x) / CellSize;
            float py = (worldPos.y - WorldMin.y) / CellSize;
            return new Vector2(px, py);
        }

        private static bool Scan()
        {
            var tilemaps = Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
            if (tilemaps == null || tilemaps.Length == 0)
                return false;

            // Collect ground-like tilemaps (ones that have a TilemapCollider2D or are named "ground")
            var groundMaps = new List<Tilemap>();

            foreach (var tm in tilemaps)
            {
                var bounds = tm.cellBounds;
                if (bounds.size.x == 0 && bounds.size.y == 0)
                    continue;

                string name = tm.gameObject.name.ToLowerInvariant();
                bool hasCollider = tm.GetComponent<TilemapCollider2D>() != null;

                if (hasCollider || name.Contains("ground") || name.Contains("block") ||
                    name.Contains("illusory") || name == "oob")
                {
                    groundMaps.Add(tm);
                }
            }

            if (groundMaps.Count == 0)
                return false;

            // Get grid offset from the first ground tilemap's parent grid
            Vector3 gridOffset = Vector3.zero;
            var parentGrid = groundMaps[0].layoutGrid;
            if (parentGrid != null)
                gridOffset = parentGrid.transform.position;
            CellSize = parentGrid != null ? Mathf.RoundToInt(parentGrid.cellSize.x) : 32;
            int cellSize = CellSize;

            // Compute bounds only from ground tilemaps (all share the same grid)
            BoundsInt totalBounds = groundMaps[0].cellBounds;
            for (int i = 1; i < groundMaps.Count; i++)
            {
                var bounds = groundMaps[i].cellBounds;
                int minX = Mathf.Min(totalBounds.xMin, bounds.xMin);
                int minY = Mathf.Min(totalBounds.yMin, bounds.yMin);
                int maxX = Mathf.Max(totalBounds.xMax, bounds.xMax);
                int maxY = Mathf.Max(totalBounds.yMax, bounds.yMax);
                totalBounds = new BoundsInt(minX, minY, 0, maxX - minX, maxY - minY, 1);
            }

            // Convert tile bounds to world bounds
            WorldMin = new Vector2(
                totalBounds.xMin * cellSize + gridOffset.x,
                totalBounds.yMin * cellSize + gridOffset.y);
            WorldMax = new Vector2(
                totalBounds.xMax * cellSize + gridOffset.x,
                totalBounds.yMax * cellSize + gridOffset.y);

            Plugin.Log.LogInfo($"Grid offset: {gridOffset}, cellSize: {cellSize}, totalBounds: {totalBounds}");

            TexWidth = totalBounds.size.x;
            TexHeight = totalBounds.size.y;

            if (TexWidth <= 0 || TexHeight <= 0)
                return false;

            // Cap texture size to something reasonable
            if (TexWidth > 2048 || TexHeight > 2048)
            {
                Plugin.Log.LogWarning($"Minimap texture would be {TexWidth}x{TexHeight}, capping to 2048");
                TexWidth = Mathf.Min(TexWidth, 2048);
                TexHeight = Mathf.Min(TexHeight, 2048);
            }

            // Build texture
            if (Texture == null || Texture.width != TexWidth || Texture.height != TexHeight)
            {
                if (Texture != null) Object.Destroy(Texture);
                Texture = new Texture2D(TexWidth, TexHeight, TextureFormat.RGBA32, false);
                Texture.filterMode = FilterMode.Point;
            }

            // Clear to transparent
            var clearPixels = new Color[TexWidth * TexHeight];
            for (int i = 0; i < clearPixels.Length; i++)
                clearPixels[i] = colClear;
            Texture.SetPixels(clearPixels);

            // Paint ground tiles
            foreach (var tm in groundMaps)
            {
                var bounds = tm.cellBounds;
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    for (int y = bounds.yMin; y < bounds.yMax; y++)
                    {
                        if (tm.HasTile(new Vector3Int(x, y, 0)))
                        {
                            int px = x - totalBounds.xMin;
                            int py = y - totalBounds.yMin;
                            if (px >= 0 && px < TexWidth && py >= 0 && py < TexHeight)
                                Texture.SetPixel(px, py, colGround);
                        }
                    }
                }
            }

            // Paint checkpoints
            var checkpoints = Object.FindObjectsByType<checkpointScript>(FindObjectsSortMode.None);
            if (checkpoints != null)
            {
                foreach (var cp in checkpoints)
                {
                    var pos = WorldToTex(cp.transform.position);
                    PaintDot(pos, colCheckpoint, 1);
                }
            }

            // Paint gates
            var startGates = Object.FindObjectsByType<startGate>(FindObjectsSortMode.None);
            if (startGates != null)
            {
                foreach (var sg in startGates)
                {
                    var pos = WorldToTex(sg.transform.position);
                    PaintDot(pos, colStartGate, 1);
                }
            }

            var endGates = Object.FindObjectsByType<endGate>(FindObjectsSortMode.None);
            if (endGates != null)
            {
                foreach (var eg in endGates)
                {
                    var pos = WorldToTex(eg.transform.position);
                    PaintDot(pos, colEndGate, 1);
                }
            }

            Texture.Apply();

            // Scan course bounds
            ScanCourses();

            Plugin.Log.LogInfo($"Minimap scanned: {TexWidth}x{TexHeight} tiles, {groundMaps.Count} ground layers, world ({WorldMin} -> {WorldMax})");
            return true;
        }

        private static void PaintDot(Vector2 texPos, Color col, int radius)
        {
            int cx = Mathf.RoundToInt(texPos.x);
            int cy = Mathf.RoundToInt(texPos.y);
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int px = cx + dx;
                    int py = cy + dy;
                    if (px >= 0 && px < TexWidth && py >= 0 && py < TexHeight)
                        Texture.SetPixel(px, py, col);
                }
            }
        }

        private static void ScanCourses()
        {
            Courses.Clear();
            var courseScripts = Object.FindObjectsByType<courseScript>(FindObjectsSortMode.None);
            if (courseScripts == null) return;

            foreach (var cs in courseScripts)
            {
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 max = new Vector2(float.MinValue, float.MinValue);
                int count = 0;

                // Base bounds from checkpoints and gates (reliable course path markers)
                foreach (var cp in cs.GetComponentsInChildren<checkpointScript>(true))
                {
                    ExpandBounds(cp.transform.position, ref min, ref max);
                    count++;
                }
                foreach (var sg in cs.GetComponentsInChildren<startGate>(true))
                {
                    ExpandBounds(sg.transform.position, ref min, ref max);
                    count++;
                }
                foreach (var eg in cs.GetComponentsInChildren<endGate>(true))
                {
                    ExpandBounds(eg.transform.position, ref min, ref max);
                    count++;
                }

                if (count == 0) continue;

                // Include upgrade boxes only if they're within 1000 units of the base bounds
                // (filters out outlier prestige/block-swap boxes that are far from the course)
                float maxDist = 1000f;
                foreach (var ub in cs.GetComponentsInChildren<upgradeBox>(true))
                {
                    Vector3 p = ub.transform.position;
                    float dx = Mathf.Max(0, min.x - p.x, p.x - max.x);
                    float dy = Mathf.Max(0, min.y - p.y, p.y - max.y);
                    if (dx <= maxDist && dy <= maxDist)
                    {
                        ExpandBounds(p, ref min, ref max);
                        count++;
                    }
                }

                // Padding: ~10 tiles around the playable area
                min -= new Vector2(320f, 320f);
                max += new Vector2(320f, 320f);

                Courses.Add(new CourseBounds
                {
                    Name = cs.gameObject.name,
                    Min = min,
                    Max = max
                });

                Plugin.Log.LogInfo($"Course '{cs.gameObject.name}': {count} markers, bounds ({min.x:F0},{min.y:F0})->({max.x:F0},{max.y:F0})");
            }
        }

        private static void ExpandBounds(Vector3 pos, ref Vector2 min, ref Vector2 max)
        {
            if (pos.x < min.x) min.x = pos.x;
            if (pos.y < min.y) min.y = pos.y;
            if (pos.x > max.x) max.x = pos.x;
            if (pos.y > max.y) max.y = pos.y;
        }
    }
}
