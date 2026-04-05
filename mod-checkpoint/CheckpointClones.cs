using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IGTAPMod;
using UnityEngine;

namespace IGTAPCheckpoint
{
    public static class CheckpointClones
    {
        private static readonly FieldInfo F_sr = AccessTools.Field(typeof(Movement), "sr");
        private static readonly FieldInfo F_metalLandParticles = AccessTools.Field(typeof(Movement), "metalLandParticles");
        private static readonly Dictionary<int, GameObject> cloneObjects = new Dictionary<int, GameObject>();
        private static readonly Color CloneColor = new Color(1f, 0.2f, 0.2f, 0.5f);
        private static readonly Color GlowColor = new Color(1f, 0.15f, 0.15f, 0.2f);
        private const float GlowScale = 1.15f;

        private static GameObject sparkleObj;
        private static ParticleSystem sparkleParts;
        private static bool debugLogged;

        public static void UpdateClones()
        {
            var player = GameState.Player;
            if (player == null) return;

            var sr = (SpriteRenderer)F_sr.GetValue(player);
            if (sr == null) return;

            var data = Plugin.Data;
            HashSet<int> activeIndices = new HashSet<int>();

            // Figure out which slot index is the respawn target
            int respawnIndex = GetRespawnSlotIndex(player, data);

            for (int i = 0; i < data.Slots.Count; i++)
            {
                var slot = data.Slots[i];
                if (!slot.HasPosition) continue;

                activeIndices.Add(i);

                if (!cloneObjects.TryGetValue(i, out var go) || go == null)
                {
                    go = new GameObject($"CheckpointClone_{i}");
                    var cloneSr = go.AddComponent<SpriteRenderer>();
                    cloneSr.color = CloneColor;
                    cloneSr.sortingOrder = sr.sortingOrder - 1;
                    cloneSr.sortingLayerID = sr.sortingLayerID;

                    // Glow child: slightly larger, more transparent
                    var glowChild = new GameObject("Glow");
                    glowChild.transform.SetParent(go.transform, false);
                    glowChild.transform.localScale = Vector3.one * GlowScale;
                    var glowSr = glowChild.AddComponent<SpriteRenderer>();
                    glowSr.color = GlowColor;
                    glowSr.sortingOrder = sr.sortingOrder - 2;
                    glowSr.sortingLayerID = sr.sortingLayerID;

                    cloneObjects[i] = go;
                }

                go.transform.position = new Vector3(slot.X, slot.Y, 0f);
                go.transform.localScale = player.transform.localScale;

                var cloneRenderer = go.GetComponent<SpriteRenderer>();
                cloneRenderer.sprite = sr.sprite;

                var glowRenderer = go.transform.Find("Glow")?.GetComponent<SpriteRenderer>();
                if (glowRenderer != null)
                    glowRenderer.sprite = sr.sprite;
            }

            // Remove clones for deleted/cleared slots
            List<int> toRemove = new List<int>();
            foreach (var kvp in cloneObjects)
            {
                if (!activeIndices.Contains(kvp.Key))
                {
                    if (kvp.Value != null)
                        Object.Destroy(kvp.Value);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (int key in toRemove)
                cloneObjects.Remove(key);

            // Attach sparkle to the respawn-target clone
            UpdateSparkle(respawnIndex);

            if (!debugLogged && sparkleObj != null && sparkleObj.activeSelf)
            {
                // Log player scale/bounds for sizing reference
                var playerSr = (SpriteRenderer)F_sr.GetValue(player);
                if (playerSr != null)
                {
                    Plugin.Log.LogInfo($"[Scale Debug] player.localScale={player.transform.localScale} sprite.bounds.size={playerSr.bounds.size} sprite.rect={playerSr.sprite?.rect}");
                }
                debugLogged = true;
                var ps = sparkleObj.GetComponent<ParticleSystem>();
                var r = sparkleObj.GetComponent<ParticleSystemRenderer>();
                Plugin.Log.LogInfo($"[Sparkle Debug] active={sparkleObj.activeSelf} pos={sparkleObj.transform.position}");
                Plugin.Log.LogInfo($"[Sparkle Debug] isPlaying={ps?.isPlaying} isEmitting={ps?.isEmitting} particleCount={ps?.particleCount}");
                Plugin.Log.LogInfo($"[Sparkle Debug] renderer enabled={r?.enabled} material={r?.material?.name} shader={r?.material?.shader?.name}");
                Plugin.Log.LogInfo($"[Sparkle Debug] main.maxParticles={ps?.main.maxParticles} emission.rate={ps?.emission.rateOverTime.constant}");
                Plugin.Log.LogInfo($"[Sparkle Debug] respawnIndex={respawnIndex} overrideRespawn={Plugin.OverrideRespawn.Value} cloneCount={cloneObjects.Count}");
                Plugin.Log.LogInfo($"[Sparkle Debug] renderMode={r?.renderMode} sortingLayer={r?.sortingLayerName} sortingOrder={r?.sortingOrder}");

                // Check what the game's own particle renderers look like
                var gameParts = (ParticleSystem)F_metalLandParticles.GetValue(player);
                if (gameParts != null)
                {
                    var gr = gameParts.GetComponent<ParticleSystemRenderer>();
                    Plugin.Log.LogInfo($"[Game Particles] material={gr?.material?.name} shader={gr?.material?.shader?.name} renderMode={gr?.renderMode}");
                    Plugin.Log.LogInfo($"[Game Particles] sortingLayer={gr?.sortingLayerName} sortingOrder={gr?.sortingOrder}");
                }

                // Try to find available particle shaders
                string[] shaderNames = new[] {
                    "Universal Render Pipeline/Particles/Unlit",
                    "Universal Render Pipeline/Particles/Lit",
                    "Universal Render Pipeline/Particles/Simple Lit",
                    "Particles/Standard Unlit",
                    "Sprites/Default",
                    "Universal Render Pipeline/2D/Sprite-Lit-Default",
                    "Hidden/InternalErrorShader"
                };
                foreach (var name in shaderNames)
                {
                    var s = Shader.Find(name);
                    Plugin.Log.LogInfo($"[Shader Find] '{name}' => {(s != null ? "FOUND" : "null")}");
                }
            }
        }

        private static int GetRespawnSlotIndex(Movement player, CheckpointData data)
        {
            // If override respawn is on, the active slot is the respawn target
            if (Plugin.OverrideRespawn.Value)
            {
                var active = data.ActiveSlot;
                if (active != null && active.HasPosition)
                    return data.ActiveSlotIndex;
                return -1;
            }

            // Otherwise, find whichever slot matches the game's current respawnPoint
            Vector2 rp = player.respawnPoint;
            for (int i = 0; i < data.Slots.Count; i++)
            {
                var slot = data.Slots[i];
                if (!slot.HasPosition) continue;
                if (Mathf.Abs(slot.X - rp.x) < 0.5f && Mathf.Abs(slot.Y - rp.y) < 0.5f)
                    return i;
            }
            return -1;
        }

        private static void UpdateSparkle(int targetIndex)
        {
            if (targetIndex < 0 || !cloneObjects.TryGetValue(targetIndex, out var target) || target == null)
            {
                // No respawn target among our clones — hide sparkle
                if (sparkleObj != null)
                    sparkleObj.SetActive(false);
                return;
            }

            if (sparkleParts == null)
                CreateSparkle();

            sparkleObj.SetActive(true);
            sparkleObj.transform.position = target.transform.position;
        }

        private static void CreateSparkle()
        {
            sparkleObj = new GameObject("CheckpointSparkle");

            sparkleParts = sparkleObj.AddComponent<ParticleSystem>();

            // Player sprite is ~272 units wide, scale to match
            var main = sparkleParts.main;
            main.startLifetime = 0.6f;
            main.startSpeed = 40f;
            main.startSize = 8f;
            main.maxParticles = 20;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = CloneColor;
            main.gravityModifier = -0.3f;

            var emission = sparkleParts.emission;
            emission.rateOverTime = 10f;

            var shape = sparkleParts.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 30f;

            var sizeOverLifetime = sparkleParts.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.Linear(0f, 1f, 1f, 0f));

            var colorOverLifetime = sparkleParts.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] {
                    new GradientColorKey(new Color(1f, 0.2f, 0.2f), 0f),
                    new GradientColorKey(new Color(1f, 0.2f, 0.2f), 1f)
                },
                new[] {
                    new GradientAlphaKey(0.5f, 0f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            colorOverLifetime.color = gradient;

            var renderer = sparkleObj.GetComponent<ParticleSystemRenderer>();
            renderer.sortingOrder = 100;

            // Grab material from the game's own particle systems (URP-compatible)
            var player = GameState.Player;
            if (player != null)
            {
                var gameParts = (ParticleSystem)F_metalLandParticles.GetValue(player);
                if (gameParts != null)
                {
                    var gameRenderer = gameParts.GetComponent<ParticleSystemRenderer>();
                    if (gameRenderer != null && gameRenderer.material != null)
                    {
                        renderer.material = new Material(gameRenderer.material);
                        renderer.material.color = Color.white;
                    }
                }
            }

            // Fallback if we couldn't get the game's material
            if (renderer.sharedMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                          ?? Shader.Find("Sprites/Default");
                if (shader != null)
                    renderer.material = new Material(shader);
            }
        }

        public static void DestroyAll()
        {
            foreach (var kvp in cloneObjects)
            {
                if (kvp.Value != null)
                    Object.Destroy(kvp.Value);
            }
            cloneObjects.Clear();

            if (sparkleObj != null)
            {
                Object.Destroy(sparkleObj);
                sparkleObj = null;
                sparkleParts = null;
            }
        }
    }
}
