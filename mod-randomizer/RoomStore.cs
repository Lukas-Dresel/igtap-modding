using System;
using System.IO;
using UnityEngine;

namespace IGTAPRandomizer
{
    public static class RoomStore
    {
        // Layout JSON lives in a subfolder of BepInEx config so we don't clutter the root.
        public static string Directory(string configDir)
        {
            string dir = Path.Combine(configDir, "IGTAPRandomizer");
            try { System.IO.Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        public static string LayoutPath(string configDir, int courseNumber)
        {
            return Path.Combine(Directory(configDir), $"course{courseNumber}_layout.json");
        }

        public static CourseLayout Load(string configDir, int courseNumber)
        {
            string path = LayoutPath(configDir, courseNumber);
            if (!File.Exists(path))
            {
                return new CourseLayout { courseNumber = courseNumber };
            }
            try
            {
                string json = File.ReadAllText(path);
                var layout = JsonUtility.FromJson<CourseLayout>(json);
                if (layout == null) layout = new CourseLayout { courseNumber = courseNumber };
                if (layout.rooms == null) layout.rooms = new System.Collections.Generic.List<RoomDef>();
                layout.courseNumber = courseNumber;
                return layout;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Randomizer] Failed to load layout {path}: {e.Message}");
                return new CourseLayout { courseNumber = courseNumber };
            }
        }

        public static void Save(string configDir, CourseLayout layout)
        {
            if (layout == null) return;
            string path = LayoutPath(configDir, layout.courseNumber);
            try
            {
                string json = JsonUtility.ToJson(layout, true);
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Randomizer] Failed to save layout {path}: {e.Message}");
            }
        }
    }
}
