using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StoreAndCraft
{
    internal static class ContainerFilter
    {
        private static readonly MethodInfo LoadInventory = AccessTools.Method(typeof(Container), "Load");
        private static readonly MethodInfo SaveInventoryMethod = AccessTools.Method(typeof(Container), "Save");
        private static readonly FieldInfo LastRevision = AccessTools.Field(typeof(Container), "m_lastRevision");
        // Valheim serializes chest contents under ZDOVars.s_items.
        private static readonly FieldInfo ItemsHash = AccessTools.Field(typeof(ZDOVars), "s_items");
        private static float _nextEmptyWriteLog;
        /// <summary>Our consume/withdraw emptied a chest on purpose — allow that Save.</summary>
        internal static int AllowEmptySave;

        public static bool IsUsable(Container container)
        {
            if (container == null)
                return false;

            ZNetView nv = Refs.View(container);
            if (nv == null || !nv.IsValid())
                return false;

            if (container.GetInventory() == null)
                return false;

            // Normal chests / boxes
            if (container.GetComponent<Piece>() != null)
                return true;

            // Carts (class name Vagon) & ships — mining runs / hauling
            if (container.GetComponentInParent<Vagon>() != null)
                return true;
            if (container.GetComponentInParent<Ship>() != null)
                return true;

            return false;
        }

        public static string PiecePrefab(Container container)
        {
            if (container == null)
                return null;

            Piece piece = container.GetComponent<Piece>();
            if (piece == null)
                piece = container.GetComponentInParent<Piece>();
            if (piece != null)
                return ItemIds.StripClone(piece.gameObject.name);

            Vagon cart = container.GetComponentInParent<Vagon>();
            if (cart != null)
                return ItemIds.StripClone(cart.gameObject.name);

            Ship ship = container.GetComponentInParent<Ship>();
            if (ship != null)
                return ItemIds.StripClone(ship.gameObject.name);

            return ItemIds.StripClone(container.gameObject.name);
        }

        public static bool PlayerMayUse(Container container, Vector3 from)
        {
            if (!IsUsable(container))
                return false;

            if (ChestNames.IsFullyIgnored(container))
                return false;

            if (!PrivateArea.CheckAccess(container.transform.position, 0f, false, true))
                return false;

            return true;
        }

        /// <summary>
        /// Dump / auto-store targets only. World chests (crypts, house spawns, …) have
        /// no creator and must not steal stacks from the player's base sorting.
        /// Carts and ships are always allowed.
        /// </summary>
        // Runs for every chest of every store / deposit / display check and walks the hierarchy (GetComponentInParent).
        // A "yes" never changes for a container (cart, ship, player piece): remembered per instance. A "no" is checked
        // again each time (the creator is set right after placement, a world chest stays no, troughs are few).
        private static readonly System.Collections.Generic.HashSet<int> PlayerBuiltIds = new System.Collections.Generic.HashSet<int>();
        private static ZNetScene _playerBuiltScene;

        public static bool IsPlayerBuiltStorage(Container container)
        {
            if (container == null)
                return false;

            if (_playerBuiltScene != ZNetScene.instance || PlayerBuiltIds.Count > 16384)
            {
                PlayerBuiltIds.Clear();
                _playerBuiltScene = ZNetScene.instance;
            }
            int id = container.GetInstanceID();
            if (PlayerBuiltIds.Contains(id))
                return true;

            // Feed troughs are animal food only — not dump / auto-store / craft targets.
            if (FeedTrough.IsTrough(container))
                return false;

            bool built;
            if (container.GetComponentInParent<Vagon>() != null || container.GetComponentInParent<Ship>() != null)
            {
                built = true;
            }
            else
            {
                Piece piece = container.GetComponent<Piece>();
                if (piece == null)
                    piece = container.GetComponentInParent<Piece>();
                built = piece != null && piece.IsPlacedByPlayer();
            }
            if (built)
                PlayerBuiltIds.Add(id);
            return built;
        }

        public static void RefreshInventory(Container container, bool force = false)
        {
            if (container == null || LoadInventory == null)
                return;

            try
            {
                ZNetView nv = Refs.View(container);
                ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
                Inventory inv = container.GetInventory();
                int localCount = inv != null ? inv.NrOfItems() : 0;
                bool zdoHas = ZdoHasItemPayload(zdo);

                // Populated local view + ZDO not ready: Load would clear the chest, then
                // vanilla Container.Save persists the wipe (Storage Displays used to do this).
                if (localCount > 0 && !zdoHas)
                    return;

                // Nothing in the ZDO to restore. Skip so we do not fire Inventory.Changed.
                if (!zdoHas)
                    return;

                // Already have a populated local bag — do not re-Load over it on idle reads
                // (Storage Display / craft counts). Re-Load only when forced (write prep) or
                // when the local view is empty and the ZDO still has items.
                if (!force && localCount > 0)
                    return;

                // Only force a re-read when the local view is empty but the ZDO still
                // has items (stale empty after travel). Never reset the revision over a full bag.
                // uint.MaxValue = vanilla's "never loaded" value. Not 0: after a world load the ZDO
                // DataRevision is 0, so 0 made Load() skip and vanilla CheckForChanges never loaded
                // the chest until someone opened it.
                if (force && localCount <= 0 && LastRevision != null)
                    LastRevision.SetValue(container, uint.MaxValue);

                LoadInventory.Invoke(container, null);

                inv = container.GetInventory();
                int after = inv != null ? inv.NrOfItems() : 0;
                ChestLoadGuard.NoteLoadResult(container, after <= 0, zdoHas);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Force-Load before mutating a chest. Returns false when the local inventory looks
        /// empty while the ZDO still has an item payload — writing in that state can wipe the chest.
        /// </summary>
        public static bool TryReadyForWrite(Container container)
        {
            if (container == null)
                return false;

            ZNetView nv = Refs.View(container);
            if (nv == null || !nv.IsValid() || !nv.IsOwner())
                return false;

            RefreshInventory(container, force: true);
            Inventory inv = container.GetInventory();
            if (inv == null)
                return false;

            // Coming back from a raid: local view is empty until the ZDO bytes arrive.
            // Writing then (dump / auto-store / auto-stack) persists empty and wipes the chest.
            if (inv.NrOfItems() <= 0)
            {
                bool payload = ZdoHasItemPayload(nv.GetZDO());
                if (payload || !container.IsInUse())
                {
                    // Only the race is worth a warning. No payload = the chest is really empty (nothing saved in
                    // it yet): skipped the same way, but silently, it is normal for every empty chest in the base.
                    if (payload && Plugin.Log != null && Time.unscaledTime >= _nextEmptyWriteLog)
                    {
                        _nextEmptyWriteLog = Time.unscaledTime + 10f;
                        Plugin.Log.LogWarning("Skipped chest write: local inventory empty but ZDO still has items (load race).");
                    }
                    return false;
                }
            }

            return true;
        }

        public static void SaveInventory(Container container)
        {
            ZNetView nv = Refs.View(container);
            if (nv == null || !nv.IsValid() || !nv.IsOwner() || SaveInventoryMethod == null)
                return;
            if (container.GetInventory() == null)
                return;

            AllowEmptySave++;
            try
            {
                SaveInventoryMethod.Invoke(container, null);
            }
            catch
            {
            }
            finally
            {
                if (AllowEmptySave > 0)
                    AllowEmptySave--;
            }
        }

        /// <summary>
        /// True when the ZDO still carries a serialized inventory larger than an empty package.
        /// Used to detect stale empty local views after ClaimOwnership / early Load.
        /// </summary>
        public static bool ZdoHasItemPayload(ZDO zdo)
        {
            if (zdo == null || ItemsHash == null)
                return false;

            try
            {
                object key = ItemsHash.GetValue(null);
                if (key == null)
                    return false;

                byte[] data = null;
                if (key is int hash)
                    data = zdo.GetByteArray(hash);
                else if (key is string name)
                    data = zdo.GetByteArray(name);

                // Empty inventories serialize to a tiny header; real contents are larger.
                return data != null && data.Length > 8;
            }
            catch
            {
                return false;
            }
        }

        public static float Distance(Vector3 a, Vector3 b)
        {
            return Vector3.Distance(a, b);
        }

        public static float SqrDistance(Vector3 a, Vector3 b)
        {
            return (a - b).sqrMagnitude;
        }
    }
}
