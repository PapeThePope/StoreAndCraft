using System.Collections.Generic;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Target chest memory for AutomationLoadedArea: per item type and link, the chest that last took that item
    /// (station output, ground items). The next one goes there first without searching the base; only when that
    /// chest is gone, full or no longer allowed does the caller search again and remember the new one.
    /// A search that found nothing is remembered for NoneSeconds, so a full base is not searched over and over.
    /// The caller still runs every normal check on the remembered chest (link, chest mode, existing item, room).
    /// </summary>
    internal static class TargetChests
    {
        private const float NoneSeconds = 5f;
        private const int MaxEntries = 2048;

        private struct Key : System.IEquatable<Key>
        {
            public string Shared;
            public int Link;

            public bool Equals(Key o) { return Link == o.Link && string.Equals(Shared, o.Shared); }
            public override bool Equals(object o) { return o is Key && Equals((Key)o); }
            public override int GetHashCode() { return (Shared != null ? Shared.GetHashCode() : 0) * 31 + Link; }
        }

        private static readonly Dictionary<Key, Container> Target = new Dictionary<Key, Container>();
        private static readonly Dictionary<Key, float> NoneUntil = new Dictionary<Key, float>();

        /// <summary>Only with "whole loaded base" on; off keeps the old nearest-chest choice.</summary>
        public static bool Active
        {
            get { return Plugin.Settings != null && Plugin.Settings.AutomationLoadedArea.Value; }
        }

        /// <summary>The remembered chest, or null (none, or it was destroyed).</summary>
        public static Container Get(string shared, int link)
        {
            if (string.IsNullOrEmpty(shared))
                return null;
            var key = new Key { Shared = shared, Link = link };
            Container c;
            if (!Target.TryGetValue(key, out c))
                return null;
            if (c == null)
            {
                Target.Remove(key);
                return null;
            }
            return c;
        }

        public static void Remember(string shared, int link, Container chest)
        {
            if (string.IsNullOrEmpty(shared) || chest == null)
                return;
            if (Target.Count >= MaxEntries)
                Target.Clear();
            var key = new Key { Shared = shared, Link = link };
            Target[key] = chest;
            NoneUntil.Remove(key);
        }

        public static void Forget(string shared, int link)
        {
            if (string.IsNullOrEmpty(shared))
                return;
            Target.Remove(new Key { Shared = shared, Link = link });
        }

        /// <summary>The last search found no chest: skip searching again for a few seconds.</summary>
        public static void MarkNone(string shared, int link)
        {
            if (string.IsNullOrEmpty(shared))
                return;
            if (NoneUntil.Count >= MaxEntries)
                NoneUntil.Clear();
            NoneUntil[new Key { Shared = shared, Link = link }] = Time.unscaledTime + NoneSeconds;
        }

        public static bool RecentlyNone(string shared, int link)
        {
            if (string.IsNullOrEmpty(shared))
                return false;
            float until;
            return NoneUntil.TryGetValue(new Key { Shared = shared, Link = link }, out until) && Time.unscaledTime < until;
        }
    }
}
