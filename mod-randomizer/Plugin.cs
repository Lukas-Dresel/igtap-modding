using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using IGTAPMod;
using UnityEngine;

namespace IGTAPRandomizer
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.igtapmod.plugin")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.igtapmod.randomizer";
        public const string PluginName = "IGTAP Randomizer";
        public const string PluginVersion = "0.1.0";

        // First course we target for the prototype. Everything non-course-4 is ignored for now.
        public const int PrototypeCourse = 4;

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> AnnotateMode;
        internal static ConfigEntry<bool> RandomizeOnCourseStart;
        internal static ConfigEntry<int> Seed;

        internal static CourseLayout Course4Layout;

        private void Awake()
        {
            Log = Logger;

            AnnotateMode = Config.Bind("General", "AnnotateMode", false,
                "When enabled, the in-game room annotation tool is active (Phase 2)");
            RandomizeOnCourseStart = Config.Bind("General", "RandomizeOnCourseStart", false,
                "When enabled, course starts will trigger a layout randomization (Phase 5)");
            Seed = Config.Bind("General", "Seed", 1,
                "Seed used to permute rooms. 'Re-roll' picks a new seed.");

            Course4Layout = RoomStore.Load(Paths.ConfigPath, PrototypeCourse);
            Log.LogInfo($"{PluginName} v{PluginVersion} loaded. Course {PrototypeCourse} layout: " +
                        $"{Course4Layout.rooms.Count} rooms annotated.");

            GameEvents.CourseStarted += OnCourseStarted;
            GameEvents.CourseStopped += OnCourseStopped;

            RandomizerMenu.Register();
        }

        private void Update()
        {
            RoomAuthoring.Tick();
        }

        private void OnGUI()
        {
            RoomAuthoring.DrawOverlay();
        }

        private void OnDestroy()
        {
            GameEvents.CourseStarted -= OnCourseStarted;
            GameEvents.CourseStopped -= OnCourseStopped;
        }

        private void OnCourseStarted(int courseNumber, courseScript script)
        {
            if (courseNumber != PrototypeCourse)
            {
                Log.LogInfo($"Course {courseNumber} started — randomizer ignores non-prototype courses.");
                return;
            }

            Log.LogInfo($"Course {courseNumber} started. Annotate={AnnotateMode.Value} " +
                        $"RandomizeOnStart={RandomizeOnCourseStart.Value} Seed={Seed.Value} " +
                        $"Rooms={Course4Layout.rooms.Count}");
        }

        private void OnCourseStopped(int courseNumber, bool completed, float courseTime)
        {
            if (courseNumber != PrototypeCourse) return;
            Log.LogInfo($"Course {courseNumber} stopped (completed={completed}, time={courseTime:F2}s).");
        }

        // Convenience for the menu to reload layout after external edits.
        internal static void ReloadLayout()
        {
            Course4Layout = RoomStore.Load(Paths.ConfigPath, PrototypeCourse);
            Log.LogInfo($"Reloaded course {PrototypeCourse} layout: {Course4Layout.rooms.Count} rooms.");
        }

        internal static void SaveLayout()
        {
            RoomStore.Save(Paths.ConfigPath, Course4Layout);
            Log.LogInfo($"Saved course {PrototypeCourse} layout: {Course4Layout.rooms.Count} rooms.");
        }
    }
}
