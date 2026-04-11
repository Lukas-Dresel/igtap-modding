using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace IGTAPSpeedrun
{
    [Serializable]
    public class PBSplit
    {
        public string id;
        public string label;
        public float time;
    }

    [Serializable]
    public class PBSegment
    {
        public string fromId; // "" for start
        public string toId;
        public float bestTime;
    }

    [Serializable]
    public class PBRecord
    {
        public float totalTime = float.MaxValue;
        public int deaths;
        public List<PBSplit> splits = new List<PBSplit>();
        public List<PBSegment> segments = new List<PBSegment>();
        public string timestamp;
    }

    public static class PBData
    {
        private static string GetDirectory()
        {
            return Path.Combine(BepInEx.Paths.ConfigPath, "speedrun");
        }

        private static string GetFilePath(string profileName = null)
        {
            if (string.IsNullOrEmpty(profileName))
                return Path.Combine(GetDirectory(), "pb.json");
            return Path.Combine(GetDirectory(), $"pb_{profileName}.json");
        }

        public static PBRecord Load(string profileName = null)
        {
            string path = GetFilePath(profileName);
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                var record = JsonUtility.FromJson<PBRecord>(json);
                if (record == null) return null;
                if (record.segments == null) record.segments = new List<PBSegment>();
                if (record.splits == null) record.splits = new List<PBSplit>();
                if (record.totalTime < float.MaxValue)
                    return record;
                // Still return if we have splits (segments saved mid-run)
                if (record.splits.Count > 0 || record.segments.Count > 0)
                    return record;
                return null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Failed to load PB: {e.Message}");
                return null;
            }
        }

        public static void Save(PBRecord record, string profileName = null)
        {
            try
            {
                string dir = GetDirectory();
                Directory.CreateDirectory(dir);
                if (record.segments == null) record.segments = new List<PBSegment>();
                string json = JsonUtility.ToJson(record, true);
                File.WriteAllText(GetFilePath(profileName), json);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Failed to save PB: {e.Message}");
            }
        }

        public static void Delete(string profileName = null)
        {
            try
            {
                string path = GetFilePath(profileName);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Failed to delete PB: {e.Message}");
            }
        }
    }
}
