using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace IGTAPCheckpoint
{
    [Serializable]
    public class CheckpointSlot
    {
        public string Name;
        public float X;
        public float Y;
        public bool HasPosition;
    }

    [Serializable]
    public class CheckpointData
    {
        public List<CheckpointSlot> Slots = new List<CheckpointSlot>();
        public int ActiveSlotIndex;

        [NonSerialized]
        private string filePath;

        public CheckpointSlot ActiveSlot =>
            (Slots.Count > 0 && ActiveSlotIndex >= 0 && ActiveSlotIndex < Slots.Count)
                ? Slots[ActiveSlotIndex]
                : null;

        public string ActiveSlotName => ActiveSlot?.Name ?? "???";

        public void SaveToSlot(float x, float y)
        {
            var slot = ActiveSlot;
            if (slot == null) return;
            slot.X = x;
            slot.Y = y;
            slot.HasPosition = true;
        }

        public void CycleSlot()
        {
            if (Slots.Count <= 1) return;
            ActiveSlotIndex = (ActiveSlotIndex + 1) % Slots.Count;
        }

        public void CycleSlotBack()
        {
            if (Slots.Count <= 1) return;
            ActiveSlotIndex = (ActiveSlotIndex - 1 + Slots.Count) % Slots.Count;
        }

        public void AddSlot(string name)
        {
            Slots.Add(new CheckpointSlot { Name = name });
        }

        public void RemoveSlot(int index)
        {
            if (Slots.Count <= 1) return;
            Slots.RemoveAt(index);
            if (ActiveSlotIndex >= Slots.Count)
                ActiveSlotIndex = Slots.Count - 1;
        }

        public void RenameSlot(int index, string newName)
        {
            if (index >= 0 && index < Slots.Count)
                Slots[index].Name = newName;
        }

        public void WriteToDisk()
        {
            try
            {
                string json = JsonUtility.ToJson(this, true);
                File.WriteAllText(filePath, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Checkpoints] Failed to save: {e.Message}");
            }
        }

        public static CheckpointData Load(string configDir)
        {
            string path = Path.Combine(configDir, "IGTAPCheckpoint.json");

            CheckpointData data;
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    data = JsonUtility.FromJson<CheckpointData>(json);
                    if (data.Slots == null || data.Slots.Count == 0)
                    {
                        data.Slots = new List<CheckpointSlot> { new CheckpointSlot { Name = "Slot 1" } };
                        data.ActiveSlotIndex = 0;
                    }
                }
                catch
                {
                    data = CreateDefault();
                }
            }
            else
            {
                data = CreateDefault();
            }

            data.filePath = path;
            return data;
        }

        private static CheckpointData CreateDefault()
        {
            var data = new CheckpointData();
            data.Slots.Add(new CheckpointSlot { Name = "Slot 1" });
            data.ActiveSlotIndex = 0;
            return data;
        }
    }
}
