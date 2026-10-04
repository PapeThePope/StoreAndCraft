using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Linked stations: struck-through red text above the station naming what its linked chests
    /// ran out of ("Coal", "Ore"). Fed by StationAutoFill's own fill attempts — no extra chest
    /// scans. Re-validated only on the existing auto-fill pulse (station refilled by hand /
    /// unlinked / off / out of auto-fill range → the line goes away). Local only.
    /// </summary>
    internal static class StationMissingLabel
    {
        private const float CanvasScale = 0.01f;
        private const float FontSize = 26f;
        private const string Red = "#FF4D4D";

        private sealed class Entry
        {
            public Component Station;
            public readonly SortedDictionary<string, string> Missing = new SortedDictionary<string, string>();
            public GameObject Root;
            public TextMeshProUGUI Text;
            public float Height = -1f;
        }

        private static readonly Dictionary<int, Entry> Entries = new Dictionary<int, Entry>();
        private static readonly List<int> Scratch = new List<int>();

        /// <summary>Linked station could not get <paramref name="what"/> for <paramref name="slot"/>.</summary>
        internal static void SetMissing(Component station, string slot, string what)
        {
            if (station == null || string.IsNullOrEmpty(slot) || string.IsNullOrEmpty(what))
                return;
            if (StationLink.Get(station) <= 0)
            {
                ClearMissing(station, slot);
                return;
            }
            int id = station.GetInstanceID();
            Entry e;
            if (!Entries.TryGetValue(id, out e))
            {
                e = new Entry { Station = station };
                Entries[id] = e;
            }
            string old;
            if (e.Missing.TryGetValue(slot, out old) && old == what)
                return;
            e.Missing[slot] = what;
            Refresh(e);
        }

        internal static void ClearMissing(Component station, string slot)
        {
            if (station == null)
                return;
            Entry e;
            if (!Entries.TryGetValue(station.GetInstanceID(), out e) || !e.Missing.Remove(slot))
                return;
            LogCleared(station, slot, "filled from chests");
            Refresh(e);
        }

        /// <summary>LateUpdate: only turns existing labels to the camera (nothing when none).</summary>
        internal static void Tick()
        {
            if (Entries.Count == 0)
                return;

            Camera cam = Utils.GetMainCamera();
            if (cam == null)
                return;
            Vector3 camPos = cam.transform.position;
            foreach (Entry e in Entries.Values)
            {
                if (e.Root == null || !e.Root.activeSelf)
                    continue;
                Vector3 dir = e.Root.transform.position - camPos;
                if (dir.sqrMagnitude > 0.0001f)
                    e.Root.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }

        /// <summary>Called from the StationAutoFill pulse (every 2.5 s), not on its own timer.</summary>
        internal static void Validate()
        {
            if (Entries.Count == 0)
                return;
            Player player = Player.m_localPlayer;
            float range = Plugin.Settings != null ? Plugin.Settings.AutoFillRange.Value : 0f;
            Scratch.Clear();
            foreach (KeyValuePair<int, Entry> kv in Entries)
            {
                Entry e = kv.Value;
                if (e.Station == null || player == null)
                {
                    Scratch.Add(kv.Key);
                    continue;
                }
                string reason = StationLink.Get(e.Station) <= 0 ? "unlinked"
                    : !StationAutoFill.IsAutoFillOn(e.Station) ? "auto-fill off"
                    : ContainerFilter.SqrDistance(player.transform.position, e.Station.transform.position)
                        > range * range ? "out of range"
                    : null;
                if (reason != null)
                {
                    LogCleared(e.Station, "all", reason);
                    Scratch.Add(kv.Key);
                    continue;
                }

                bool changed = false;
                List<string> slots = new List<string>(e.Missing.Keys);
                for (int i = 0; i < slots.Count; i++)
                {
                    if (StationAutoFill.SlotStillEmpty(e.Station, slots[i]))
                        continue;
                    LogCleared(e.Station, slots[i], "slot no longer empty");
                    e.Missing.Remove(slots[i]);
                    changed = true;
                }
                if (e.Missing.Count == 0)
                    Scratch.Add(kv.Key);
                else if (changed)
                    Refresh(e);
            }

            for (int i = 0; i < Scratch.Count; i++)
                Remove(Scratch[i]);
            Scratch.Clear();
        }

        /// <summary>Only when a label actually goes away (rare) — shows why in LogOutput.</summary>
        private static void LogCleared(Component station, string slot, string reason)
        {
            Plugin.Log.LogDebug("Station missing-label cleared: "
                + (station != null ? station.name : "?") + " slot=" + slot + " (" + reason + ")");
        }

        private static void Remove(int id)
        {
            Entry e;
            if (!Entries.TryGetValue(id, out e))
                return;
            if (e.Text != null && e.Text.fontMaterial != null)
                Object.Destroy(e.Text.fontMaterial);
            if (e.Root != null)
                Object.Destroy(e.Root);
            Entries.Remove(id);
        }

        private static void Refresh(Entry e)
        {
            if (e.Missing.Count == 0)
            {
                if (e.Station != null)
                    Remove(e.Station.GetInstanceID());
                return;
            }
            if (e.Station == null)
                return;
            EnsureLabel(e);
            if (e.Text == null)
                return;

            var parts = new List<string>(e.Missing.Count);
            foreach (string what in e.Missing.Values)
                parts.Add("<s>" + what + "</s>");
            e.Text.text = "<color=" + Red + ">" + string.Join("   ", parts.ToArray()) + "</color>";
            e.Root.transform.position = e.Station.transform.position + Vector3.up * e.Height;
            e.Root.SetActive(true);
        }

        private static void EnsureLabel(Entry e)
        {
            if (e.Root != null)
                return;
            if (e.Height < 0f)
                e.Height = TopOffset(e.Station);

            // Not parented to the station: its scale / WearNTear renderer cache stay untouched.
            e.Root = new GameObject("SAC_StationMissing", typeof(RectTransform), typeof(Canvas));
            Canvas canvas = e.Root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform rt = e.Root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(400f, 60f);
            e.Root.transform.localScale = Vector3.one * CanvasScale;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(e.Root.transform, false);
            RectTransform trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            e.Text = UiFonts.CreateLabel(textGo, FontSize);
            e.Text.richText = true;
            e.Text.alignment = TextAlignmentOptions.Center;
            e.Text.textWrappingMode = TextWrappingModes.NoWrap;
            e.Text.overflowMode = TextOverflowModes.Overflow;
            // Own material instance, drawn after transparent effects (smelter smoke / fire
            // particles sit right where the label floats). Depth test unchanged: no x-ray.
            Material mat = e.Text.fontMaterial;
            if (mat != null)
                mat.renderQueue = 4000;
        }

        /// <summary>Height above the station pivot: top of its renderers + a small gap.</summary>
        private static float TopOffset(Component station)
        {
            Renderer[] rends = station.GetComponentsInChildren<Renderer>(false);
            float top = float.MinValue;
            for (int i = 0; i < rends.Length; i++)
            {
                Renderer r = rends[i];
                if (r == null || r is ParticleSystemRenderer)
                    continue;
                if (r.bounds.max.y > top)
                    top = r.bounds.max.y;
            }
            if (top == float.MinValue)
                return 2f;
            return Mathf.Clamp(top - station.transform.position.y, 0.5f, 6f) + 0.35f;
        }
    }
}
