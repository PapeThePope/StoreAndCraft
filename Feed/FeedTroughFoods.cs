using System.Collections.Generic;
using UnityEngine;

namespace StoreAndCraft
{
    /// <summary>
    /// Items tameable animals actually eat: union of MonsterAI.m_consumeItems over every
    /// ZNetScene prefab with a Tameable (vanilla + modded creatures). Built once when the
    /// scene is ready; the trough filter menu and trough auto-fill only offer these.
    /// </summary>
    internal static class FeedTroughFoods
    {
        private static List<string> _all;

        /// <summary>Shared names ($item_…), sorted by localized name. Empty until ZNetScene exists.</summary>
        public static List<string> All()
        {
            if (_all != null && _all.Count > 0)
                return _all;
            if (ZNetScene.instance == null || ZNetScene.instance.m_prefabs == null)
                return new List<string>();

            var seen = new HashSet<string>();
            var list = new List<string>();
            List<GameObject> prefabs = ZNetScene.instance.m_prefabs;
            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject go = prefabs[i];
                if (go == null || go.GetComponent<Tameable>() == null)
                    continue;
                MonsterAI ai = go.GetComponent<MonsterAI>();
                if (ai == null || ai.m_consumeItems == null)
                    continue;
                for (int k = 0; k < ai.m_consumeItems.Count; k++)
                {
                    ItemDrop drop = ai.m_consumeItems[k];
                    string shared = drop != null && drop.m_itemData?.m_shared != null
                        ? drop.m_itemData.m_shared.m_name
                        : null;
                    if (!string.IsNullOrEmpty(shared) && seen.Add(shared))
                        list.Add(shared);
                }
            }

            list.Sort((a, b) => string.Compare(
                StationPullFilter.DisplayName(a), StationPullFilter.DisplayName(b),
                System.StringComparison.CurrentCultureIgnoreCase));
            _all = list;
            Plugin.Log.LogDebug("Feed trough foods: " + list.Count + " items from tameable animals.");
            return _all;
        }

        public static bool IsAnimalFood(string shared)
        {
            return !string.IsNullOrEmpty(shared) && All().Contains(shared);
        }
    }
}
