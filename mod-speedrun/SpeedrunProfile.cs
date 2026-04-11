using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace IGTAPSpeedrun
{
    [Serializable]
    public class SplitDef
    {
        public string id;
        public string label;
        public bool startsRun;
        public bool endsRun;

        public SplitDef() { }

        public SplitDef(string id, string label, bool startsRun = false, bool endsRun = false)
        {
            this.id = id;
            this.label = label;
            this.startsRun = startsRun;
            this.endsRun = endsRun;
        }
    }

    [Serializable]
    public class SpeedrunProfile
    {
        public string name;
        public List<SplitDef> splits = new List<SplitDef>();
    }

    public static class ProfileManager
    {
        public static readonly SplitDef[] Catalog = new[]
        {
            // Lifecycle / start triggers
            new SplitDef("game_start", "Game Start (any spawn)"),
            new SplitDef("game_start_clean", "Game Start (fresh save)"),
            new SplitDef("course_start1", "Enter Course 1"),
            new SplitDef("course_start2", "Enter Course 2"),
            new SplitDef("course_start3", "Enter Course 3"),
            new SplitDef("course_start4", "Enter Course 4"),
            new SplitDef("course_start5", "Enter Course 5"),
            // Course completions
            new SplitDef("course1", "Course 1"),
            new SplitDef("course2", "Course 2"),
            new SplitDef("course3", "Course 3"),
            new SplitDef("course4", "Course 4"),
            new SplitDef("course5", "Course 5"),
            // Movement upgrades
            new SplitDef("dash", "Dash"),
            new SplitDef("wallJump", "Wall Jump"),
            new SplitDef("doubleJump", "Double Jump"),
            new SplitDef("blockSwap", "Block Swap"),
            new SplitDef("swapBlocksOnce", "Swap Blocks Once"),
            new SplitDef("openGate", "Open Gate"),
            new SplitDef("unlockPrestige", "Prestige"),
            // Player events
            new SplitDef("checkpoint", "Checkpoint Hit"),
            new SplitDef("death", "Death"),
            new SplitDef("respawn", "Respawn"),
        };

        private static string ProfileDir =>
            Path.Combine(BepInEx.Paths.ConfigPath, "speedrun", "profiles");

        private static string GetFilePath(string profileName) =>
            Path.Combine(ProfileDir, $"{profileName}.json");

        public static SpeedrunProfile GetDefault()
        {
            return new SpeedrunProfile
            {
                name = "Default",
                splits = new List<SplitDef>
                {
                    new SplitDef("course1", "Course 1"),
                    new SplitDef("wallJump", "Wall Jump"),
                    new SplitDef("course2", "Course 2"),
                    new SplitDef("dash", "Dash"),
                    new SplitDef("course3", "Course 3"),
                    new SplitDef("doubleJump", "Double Jump"),
                    new SplitDef("course5", "Course 5"),
                    new SplitDef("openGate", "Open Gate", startsRun: false, endsRun: true),
                }
            };
        }

        public static List<SpeedrunProfile> LoadAll()
        {
            var profiles = new List<SpeedrunProfile>();

            if (!Directory.Exists(ProfileDir))
            {
                // Create default profile on first run
                var def = GetDefault();
                Save(def);
                profiles.Add(def);
                return profiles;
            }

            foreach (var file in Directory.GetFiles(ProfileDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var profile = JsonUtility.FromJson<SpeedrunProfile>(json);
                    if (profile != null && !string.IsNullOrEmpty(profile.name))
                        profiles.Add(profile);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Failed to load profile {file}: {e.Message}");
                }
            }

            if (profiles.Count == 0)
            {
                var def = GetDefault();
                Save(def);
                profiles.Add(def);
            }

            return profiles;
        }

        public static SpeedrunProfile Load(string profileName)
        {
            string path = GetFilePath(profileName);
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                return JsonUtility.FromJson<SpeedrunProfile>(json);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Failed to load profile '{profileName}': {e.Message}");
                return null;
            }
        }

        public static void Save(SpeedrunProfile profile)
        {
            try
            {
                Directory.CreateDirectory(ProfileDir);
                string json = JsonUtility.ToJson(profile, true);
                File.WriteAllText(GetFilePath(profile.name), json);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Failed to save profile '{profile.name}': {e.Message}");
            }
        }

        public static void Delete(string profileName)
        {
            try
            {
                string path = GetFilePath(profileName);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Failed to delete profile '{profileName}': {e.Message}");
            }
        }

        public static SplitDef FindInCatalog(string id)
        {
            foreach (var def in Catalog)
            {
                if (def.id == id)
                    return def;
            }
            return null;
        }
    }
}
