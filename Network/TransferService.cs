using System.Collections.Generic;
using UnityEngine;

namespace StoreAndCraft
{
    internal static class TransferService
    {
        public const string RpcRemove = "KAC_Remove";
        public const string RpcConsume = "KAC_Consume";
        public const string RpcDeposit = "KAC_Deposit";
        public const string RpcGrant = "KAC_Grant";
        public const string RpcStoreDrop = "KAC_StoreDrop";

        private static readonly Queue<PendingMove> Queue = new Queue<PendingMove>();
        private static bool _grantRegistered;

        private struct PendingMove
        {
            public Container Chest;
            public ItemDrop Drop;
            public int Amount;
            public float Deadline;
        }

        internal static int FillsQueued;

        public static void RegisterGrant()
        {
            if (_grantRegistered || ZRoutedRpc.instance == null)
                return;
            ZRoutedRpc.instance.Register<ZPackage>(RpcGrant, RPC_Grant);
            _grantRegistered = true;
        }

        public static void Tick()
        {
            int budget = Plugin.Settings != null ? Plugin.Settings.MaxTransfersPerTick.Value : 8;
            int n = 0;
            int safety = Queue.Count;
            while (Queue.Count > 0 && n < budget && safety-- > 0)
            {
                PendingMove op = Queue.Dequeue();
                if (Time.time > op.Deadline)
                    continue;

                if (op.Chest == null || !ContainerFilter.IsUsable(op.Chest))
                    continue;

                // Never steal ownership here — that kicks players out of open chests
                // and can wipe items on ZDO rollback.
                if (ExecuteWithoutStealing(op))
                    n++;
                else
                    Queue.Enqueue(op);
            }
        }

        /// <summary>
        /// Create <paramref name="amount"/> of <paramref name="token"/> directly in a chest
        /// (station output → chest). Ownership-safe; no player bag involved.
        /// </summary>
        public static bool TryDepositCreated(Container chest, string token, int amount)
        {
            if (chest == null || amount <= 0 || string.IsNullOrEmpty(token))
                return false;
            if (ChestNames.BlocksDeposit(chest))
                return false;
            if (!ContainerFilter.IsPlayerBuiltStorage(chest))
                return false;

            NearbyIndex.EnsureInventory(chest, force: true);
            ZNetView nv = Refs.View(chest);
            Inventory peek = chest.GetInventory();
            bool localReady = peek != null;
            if (nv != null && nv.IsValid() && !nv.IsOwner() && !chest.IsInUse() && localReady)
                nv.ClaimOwnership();

            if (!IsChestOwner(chest))
                return false;
            if (!ContainerFilter.TryReadyForWrite(chest))
                return false;

            Inventory inv = chest.GetInventory();
            if (inv == null)
                return false;

            GameObject prefab = ItemIds.PrefabFromToken(token);
            if (prefab == null && ObjectDB.instance != null)
                prefab = ObjectDB.instance.GetItemPrefab(token);
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop?.m_itemData?.m_shared == null)
                return false;

            ItemDrop.ItemData probe = drop.m_itemData.Clone();
            probe.m_stack = amount;
            if (!inv.CanAddItem(probe, amount))
                return false;

            string addToken = prefab.name;
            if (!TryAddItem(inv, addToken, amount, probe.m_quality, probe.m_variant, 0L, "", UsableWorldLevel(0)))
                return false;

            ContainerFilter.SaveInventory(chest);
            Highlight(chest);
            return true;
        }

        public static bool StoreItem(Container chest, Inventory from, ItemDrop.ItemData item, int amount)
        {
            if (chest == null || from == null || item == null || amount <= 0)
                return false;
            // The player's dump / store one item: Only build/craft chests take it too (see ChestNames.BlocksDump).
            if (ChestNames.BlocksDump(chest))
                return false;
            if (!ContainerFilter.IsPlayerBuiltStorage(chest))
                return false;

            // Prefer a real local deposit: after travel / unload the client inventory view
            // is often empty or stale. Remote Deposit then "succeeds" (items removed + RPC)
            // and the owner refunds — TopLeft says stored, bag still has items, no ping.
            // Do not steal ownership while the local view is empty — Claim + Save wipes
            // the chest when you walk back into base after a boss.
            NearbyIndex.EnsureInventory(chest, force: true);
            ZNetView nv = Refs.View(chest);
            Inventory peek = chest.GetInventory();
            bool localReady = peek != null && peek.NrOfItems() > 0;
            if (nv != null && nv.IsValid() && !nv.IsOwner() && !chest.IsInUse() && localReady)
                nv.ClaimOwnership();

            if (IsChestOwner(chest))
            {
                if (!ContainerFilter.TryReadyForWrite(chest))
                    return false;
                return DepositLocal(chest, from, item, amount);
            }

            NearbyIndex.EnsureInventory(chest, force: true);
            return DepositRemote(chest, from, item, amount);
        }

        public static bool StoreDrop(Container chest, ItemDrop drop)
        {
            if (chest == null || drop == null || drop.m_itemData == null)
                return false;
            if (ChestNames.BlocksDeposit(chest))
                return false;
            if (!ContainerFilter.IsPlayerBuiltStorage(chest))
                return false;

            ZNetView dropView = Refs.View(drop);
            bool ownDrop = dropView != null && dropView.IsValid() && dropView.IsOwner();
            bool ownChest = IsChestOwner(chest);

            if (ownDrop && ownChest)
                return StoreDropLocal(chest, drop);

            // Kiln / smelter / blast furnace output is owned by whoever last used the
            // station. Asking the chest owner to steal that drop fails while we stand
            // on it and only stores after we walk back into range. Send the item
            // instead; we already own the drop so we can destroy it.
            if (ownDrop)
                return StoreDropRemote(chest, drop);

            if (dropView != null && dropView.IsValid())
                drop.RequestOwn();

            Enqueue(new PendingMove
            {
                Chest = chest,
                Drop = drop,
                Amount = drop.m_itemData.m_stack,
                Deadline = Time.time + 2.5f
            });
            return true;
        }

        public static int Withdraw(Container chest, string sharedName, int amount, Inventory playerInv, bool leaveOne)
        {
            if (chest == null || playerInv == null || amount <= 0 || string.IsNullOrEmpty(sharedName))
                return 0;

            leaveOne = ItemIds.ShouldLeaveOne(leaveOne, sharedName);

            if (IsChestOwner(chest))
                return WithdrawLocal(chest, sharedName, amount, playerInv, leaveOne);

            // Never steal ownership from an open chest — ask the owner via RPC.
            ZNetView view = Refs.View(chest);
            if (view != null && view.IsValid())
                view.InvokeRPC(RpcRemove, sharedName, amount, leaveOne ? 1 : 0);
            return 0;
        }

        /// <summary>
        /// Hammer C+place grab: always land stacks in the player bag (inventory UI may be closed).
        /// Claims ownership when safe so we never fire RpcRemove without a verified local add.
        /// </summary>
        /// <summary>> 0 while the restock key runs: the carry weight does not limit what is taken out of the chests.</summary>
        internal static int IgnoreWeight;

        public static int WithdrawForGrab(Container chest, string sharedName, int amount, Player player, bool leaveOne)
        {
            if (chest == null || player == null || amount <= 0 || string.IsNullOrEmpty(sharedName))
                return 0;

            Inventory playerInv = player.GetInventory();
            if (playerInv == null)
                return 0;

            ZNetView nv = Refs.View(chest);
            if (nv != null && nv.IsValid() && !nv.IsOwner())
            {
                // Someone else has the chest open — async grant path only.
                if (chest.IsInUse())
                {
                    Withdraw(chest, sharedName, amount, playerInv, leaveOne);
                    return 0;
                }

                nv.ClaimOwnership();
            }

            if (!ContainerFilter.TryReadyForWrite(chest))
                return 0;
            leaveOne = ItemIds.ShouldLeaveOne(leaveOne, sharedName);
            return WithdrawLocal(chest, sharedName, amount, playerInv, leaveOne);
        }

        /// <summary>
        /// Destroy items in a chest for craft/build cost. Does NOT move them into the player
        /// inventory (avoids filling free slots so the crafted item cannot be added).
        /// </summary>
        public static int Consume(Container chest, string sharedName, int amount, bool leaveOne, int quality = -1)
        {
            if (chest == null || amount <= 0 || string.IsNullOrEmpty(sharedName))
                return 0;

            leaveOne = ItemIds.ShouldLeaveOne(leaveOne, sharedName);

            if (IsChestOwner(chest))
                return ConsumeLocal(chest, sharedName, amount, leaveOne, quality);

            int take = PeekConsumeAmount(chest, sharedName, amount, leaveOne, quality);
            if (take <= 0)
                return 0;

            ZNetView view = Refs.View(chest);
            if (view == null || !view.IsValid())
                return 0;

            view.InvokeRPC(RpcConsume, sharedName, take, leaveOne ? 1 : 0, quality);
            PendingChestDebit.Add(chest, sharedName, take);
            return take;
        }

        /// <summary>
        /// Destroy a specific ItemData instance in a chest (Epic Loot sacrifice / exact spend).
        /// Owner path removes by reference; remote falls back to name consume (no leave-one).
        /// </summary>
        public static int ConsumeExact(Container chest, ItemDrop.ItemData item, int amount)
        {
            if (chest == null || item?.m_shared == null || amount <= 0)
                return 0;

            if (IsChestOwner(chest))
                return ConsumeExactLocal(chest, item, amount);

            string shared = item.m_shared.m_name;
            return Consume(chest, shared, amount, leaveOne: false, quality: item.m_quality);
        }

        private static int ConsumeExactLocal(Container chest, ItemDrop.ItemData item, int amount)
        {
            Inventory inv = chest.GetInventory();
            if (inv == null || !inv.ContainsItem(item))
                return 0;

            int take = Mathf.Min(amount, item.m_stack);
            if (take <= 0)
                return 0;

            inv.RemoveItem(item, take);
            ContainerFilter.SaveInventory(chest);
            return take;
        }

        private static int PeekConsumeAmount(Container chest, string sharedName, int amount, bool leaveOne, int quality)
        {
            Inventory inv = chest != null ? chest.GetInventory() : null;
            if (inv == null || amount <= 0)
                return 0;

            int available = inv.CountItems(sharedName, quality, true);
            available -= PendingChestDebit.Of(chest, sharedName);
            if (leaveOne && available > 0)
                available -= 1;
            return Mathf.Min(amount, Mathf.Max(0, available));
        }

        public static void RegisterOn(Container container)
        {
            ZNetView nv = Refs.View(container);
            if (nv == null || !nv.IsValid())
                return;

            TryRegister(nv, RpcRemove, () => nv.Register<string, int, int>(RpcRemove,
                (long sender, string name, int amount, int leaveOne) =>
                    OnRemove(container, sender, name, amount, leaveOne != 0)));

            TryRegister(nv, RpcConsume, () => nv.Register<string, int, int, int>(RpcConsume,
                (long sender, string name, int amount, int leaveOne, int quality) =>
                    OnConsume(container, sender, name, amount, leaveOne != 0, quality)));

            TryRegister(nv, RpcDeposit, () => nv.Register<ZPackage>(RpcDeposit,
                (long sender, ZPackage pkg) => OnDeposit(container, sender, pkg)));

            TryRegister(nv, RpcStoreDrop, () => nv.Register<ZDOID>(RpcStoreDrop,
                (long sender, ZDOID dropId) => OnStoreDrop(container, sender, dropId)));
        }

        private static void TryRegister(ZNetView nv, string name, System.Action register)
        {
            try
            {
                register();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogDebug("RPC register " + name + ": " + ex.Message);
            }
        }

        internal static void OnRemove(Container container, long sender, string sharedName, int amount, bool leaveOne)
        {
            ZNetView nv = Refs.View(container);
            if (container == null || nv == null || !nv.IsOwner() || amount <= 0)
                return;
            if (!ValidateRpc(container, sender,
                    Plugin.Settings != null ? Plugin.Settings.StationPullRange() : 20f))
                return;

            if (!ContainerFilter.TryReadyForWrite(container))
                return;

            Inventory inv = container.GetInventory();
            if (inv == null)
                return;

            int available = inv.CountItems(sharedName, -1, true);
            if (leaveOne && available > 0)
                available -= 1;
            int take = Mathf.Min(amount, available);
            if (take <= 0)
                return;

            // Move real stacks (quality / worldLevel / crafter), never spawn fresh prefab defaults.
            int granted = 0;
            var items = new List<ItemDrop.ItemData>(inv.GetAllItems());
            foreach (ItemDrop.ItemData src in items)
            {
                if (granted >= take)
                    break;
                if (src?.m_shared == null || src.m_stack <= 0)
                    continue;
                if (src.m_shared.m_name != sharedName)
                    continue;

                int move = Mathf.Min(take - granted, src.m_stack);
                if (move <= 0)
                    continue;

                string prefabName = ItemIds.PrefabName(src) ?? sharedName;
                SendGrant(
                    sender,
                    prefabName,
                    move,
                    src.m_quality,
                    src.m_variant,
                    src.m_crafterID,
                    src.m_crafterName ?? "",
                    src.m_worldLevel);

                src.m_stack -= move;
                granted += move;
                if (src.m_stack <= 0)
                    inv.RemoveItem(src);
            }

            if (granted > 0)
                ContainerFilter.SaveInventory(container);
        }

        internal static void OnConsume(Container container, long sender, string sharedName, int amount, bool leaveOne, int quality)
        {
            ZNetView nv = Refs.View(container);
            if (container == null || nv == null || !nv.IsOwner() || amount <= 0)
                return;
            if (!ValidateRpc(container, sender,
                    Plugin.Settings != null
                        ? Mathf.Max(Plugin.Settings.CraftRange.Value, Plugin.Settings.AutoFillPlayerToChestRange())
                        : 20f))
                return;

            // ConsumeLocal loads inventory once — required so client RPCs don't no-op.
            ConsumeLocal(container, sharedName, amount, leaveOne, quality);
        }

        internal static void OnDeposit(Container container, long sender, ZPackage pkg)
        {
            ZNetView nv = Refs.View(container);
            if (container == null || nv == null || !nv.IsOwner() || pkg == null)
                return;
            if (!ValidateRpc(container, sender, MaxStoreRange()))
            {
                RefundDeposit(sender, pkg, true);
                return;
            }

            long resetPos = pkg.GetPos();
            string name = pkg.ReadString();
            int stack = pkg.ReadInt();
            int quality = pkg.ReadInt();
            int variant = pkg.ReadInt();
            long crafterId = pkg.ReadLong();
            string crafterName = pkg.ReadString();
            if (stack <= 0)
                return;

            // Never Add+Save over a stale empty local view — that permanently wipes the chest.
            if (!ContainerFilter.TryReadyForWrite(container))
            {
                RefundDeposit(sender, name, stack, quality, variant, crafterId, crafterName);
                return;
            }

            Inventory inv = container.GetInventory();
            if (inv == null)
            {
                RefundDeposit(sender, name, stack, quality, variant, crafterId, crafterName);
                return;
            }

            GameObject prefab = ItemIds.PrefabFromToken(name);
            if (prefab == null)
            {
                RefundDeposit(sender, name, stack, quality, variant, crafterId, crafterName);
                return;
            }

            ItemDrop.ItemData probe = prefab.GetComponent<ItemDrop>()?.m_itemData?.Clone();
            if (probe == null)
            {
                RefundDeposit(sender, name, stack, quality, variant, crafterId, crafterName);
                return;
            }

            probe.m_stack = stack;
            probe.m_quality = quality;
            probe.m_variant = variant;
            probe.m_crafterID = crafterId;
            probe.m_crafterName = crafterName ?? "";

            if (!inv.CanAddItem(probe, stack))
            {
                RefundDeposit(sender, name, stack, quality, variant, crafterId, crafterName);
                return;
            }

            ItemDrop.ItemData added = inv.AddItem(name, stack, quality, variant, crafterId, crafterName, false, false);
            if (added == null)
            {
                // Fallback: clone path
                if (!inv.AddItem(probe))
                {
                    RefundDeposit(sender, name, stack, quality, variant, crafterId, crafterName);
                    return;
                }
            }

            ContainerFilter.SaveInventory(container);
            Highlight(container);
        }

        private static void RefundDeposit(long sender, ZPackage pkg, bool reset)
        {
            if (pkg == null)
                return;
            if (reset)
                pkg.SetPos(0);
            try
            {
                string name = pkg.ReadString();
                int stack = pkg.ReadInt();
                int quality = pkg.ReadInt();
                int variant = pkg.ReadInt();
                long crafterId = pkg.ReadLong();
                string crafterName = pkg.ReadString();
                RefundDeposit(sender, name, stack, quality, variant, crafterId, crafterName);
            }
            catch
            {
            }
        }

        private static void RefundDeposit(long sender, string name, int stack, int quality, int variant, long crafterId, string crafterName)
        {
            if (stack <= 0 || ZRoutedRpc.instance == null)
                return;
            // World level unknown on refund path — use current world level so items stay usable.
            int worldLevel = Game.m_worldLevel;
            SendGrant(sender, name, stack, quality, variant, crafterId, crafterName ?? "", worldLevel);
            Plugin.Log.LogDebug("Refunded deposit to " + sender + ": " + name + " x" + stack);
        }

        private static void SendGrant(
            long sender,
            string prefabOrShared,
            int amount,
            int quality,
            int variant,
            long crafterId,
            string crafterName,
            int worldLevel)
        {
            if (amount <= 0 || ZRoutedRpc.instance == null || string.IsNullOrEmpty(prefabOrShared))
                return;

            var pkg = new ZPackage();
            pkg.Write(prefabOrShared);
            pkg.Write(amount);
            pkg.Write(quality);
            pkg.Write(variant);
            pkg.Write(crafterId);
            pkg.Write(crafterName ?? "");
            pkg.Write(worldLevel);
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcGrant, pkg);
        }


        internal static void OnStoreDrop(Container container, long sender, ZDOID dropId)
        {
            ZNetView nv = Refs.View(container);
            if (container == null || nv == null || !nv.IsOwner() || ZNetScene.instance == null)
                return;
            if (!ValidateRpc(container, sender, MaxStoreRange()))
                return;

            GameObject go = ZNetScene.instance.FindInstance(dropId);
            if (go == null)
                return;

            ItemDrop drop = go.GetComponent<ItemDrop>();
            if (drop == null || drop.m_itemData == null)
                return;
            if (!ContainerFilter.IsPlayerBuiltStorage(container))
                return;
            if (Vector3.Distance(drop.transform.position, container.transform.position) > MaxStoreRange() + 6f)
                return;

            ZNetView dropView = Refs.View(drop);
            if (dropView != null && !dropView.IsOwner())
            {
                // Buddy standing on eggs / kiln output owns the drop. Chest owner
                // must take it and retry. Returning here left items on the floor
                // until the chest owner walked into range.
                drop.RequestOwn();
                Enqueue(new PendingMove
                {
                    Chest = container,
                    Drop = drop,
                    Amount = drop.m_itemData.m_stack,
                    Deadline = Time.time + 6f
                });
                return;
            }

            StoreDropLocal(container, drop);
        }

        private static void RPC_Grant(long sender, ZPackage pkg)
        {
            Player player = Player.m_localPlayer;
            if (player == null || pkg == null)
                return;

            string token = pkg.ReadString();
            int amount = pkg.ReadInt();
            if (amount <= 0 || string.IsNullOrEmpty(token))
                return;

            int quality = 1;
            int variant = 0;
            long crafterId = 0L;
            string crafterName = "";
            int worldLevel = Game.m_worldLevel;
            try
            {
                if (pkg.Size() > pkg.GetPos())
                {
                    quality = pkg.ReadInt();
                    variant = pkg.ReadInt();
                    crafterId = pkg.ReadLong();
                    crafterName = pkg.ReadString() ?? "";
                    if (pkg.Size() > pkg.GetPos())
                        worldLevel = pkg.ReadInt();
                }
            }
            catch
            {
                // Older grants were name+amount only.
            }

            Inventory inv = player.GetInventory();
            if (inv == null)
                return;

            if (!TryAddItem(inv, token, amount, quality, variant, crafterId, crafterName, UsableWorldLevel(worldLevel)))
                Plugin.Log.LogWarning("Grant failed: " + token + " x" + amount);
            else
            {
                Refs.NotifyChanged(inv);
                ActivityLog.Move(Loc.T("Received", "Erhalten"), Loc.T("Chest", "Kiste"), amount, token, ActivityLog.Bag());
            }
        }

        private static bool ExecuteWithoutStealing(PendingMove op)
        {
            if (op.Drop != null)
            {
                ZNetView dropView = Refs.View(op.Drop);
                if (dropView == null || !dropView.IsValid())
                    return true; // drop gone
                if (!dropView.IsOwner())
                {
                    op.Drop.RequestOwn();
                    return false;
                }

                if (IsChestOwner(op.Chest))
                    return StoreDropLocal(op.Chest, op.Drop);

                return StoreDropRemote(op.Chest, op.Drop);
            }

            return true;
        }

        private static bool DepositRemote(Container chest, Inventory from, ItemDrop.ItemData item, int amount)
        {
            if (chest == null || from == null || item == null || item.m_shared == null)
                return false;

            // The deposit RPC only carries prefab / stack / quality / variant / crafter.
            // Custom data (Epic Loot enchantments, other mods' tags) would be rebuilt as a
            // plain item on the owner, so such items only move when we own the chest.
            if (item.m_customData != null && item.m_customData.Count > 0)
                return false;

            int take = Mathf.Min(amount, item.m_stack);
            if (take <= 0)
                return false;

            // Always re-check room on a fresh Load — stale empty views caused false deposits.
            NearbyIndex.EnsureInventory(chest, force: true);
            Inventory chestInv = chest.GetInventory();
            if (chestInv == null)
                return false;

            int fit = ChestPicker.AmountThatFits(chestInv, item);
            take = Mathf.Min(take, fit);
            if (take <= 0)
                return false;

            string prefabName = ItemIds.PrefabName(item) ?? ItemIds.SharedName(item) ?? "";
            if (string.IsNullOrEmpty(prefabName))
                return false;

            int quality = item.m_quality;
            int variant = item.m_variant;
            long crafterId = item.m_crafterID;
            string crafterName = item.m_crafterName ?? "";

            from.RemoveItem(item, take);
            Refs.NotifyChanged(from);

            var pkg = new ZPackage();
            pkg.Write(prefabName);
            pkg.Write(take);
            pkg.Write(quality);
            pkg.Write(variant);
            pkg.Write(crafterId);
            pkg.Write(crafterName);

            ZNetView nv = Refs.View(chest);
            if (nv == null || !nv.IsValid())
            {
                // Chest view lost — refund locally
                RefundLocal(from, prefabName, take);
                return false;
            }

            nv.InvokeRPC(RpcDeposit, pkg);
            // Visual feedback only when we actually handed items off (owner may still refund).
            Highlight(chest);
            return true;
        }

        private static void RefundLocal(Inventory inv, string token, int amount)
        {
            if (inv == null || amount <= 0)
                return;
            TryAddItem(inv, token, amount, 1, 0, 0L, "", UsableWorldLevel(0));
            Refs.NotifyChanged(inv);
        }

        /// <summary>
        /// Chest stacks often have worldLevel 0. Valheim rejects those for smelt / craft /
        /// store when Game.m_worldLevel is higher ("false" coal / scrap in the bag).
        /// Never downgrade below the current world when giving items to a player.
        /// </summary>
        private static int UsableWorldLevel(int sourceLevel)
        {
            int world = Game.m_worldLevel;
            return sourceLevel > world ? sourceLevel : world;
        }

        private static bool TryAddItem(
            Inventory inv,
            string token,
            int amount,
            int quality,
            int variant,
            long crafterId,
            string crafterName,
            int worldLevel)
        {
            if (inv == null || amount <= 0 || string.IsNullOrEmpty(token))
                return false;

            worldLevel = UsableWorldLevel(worldLevel);

            // 8-arg AddItem already stamps Game.m_worldLevel. Do not overwrite with a
            // lower chest worldLevel (that created unusable coal / scrap).
            ItemDrop.ItemData added = inv.AddItem(
                token,
                amount,
                quality,
                variant,
                crafterId,
                crafterName ?? "",
                false,
                false);
            if (added != null)
            {
                if (worldLevel > added.m_worldLevel)
                    added.m_worldLevel = worldLevel;
                if (added.m_dropPrefab == null)
                {
                    GameObject prefab = ItemIds.PrefabFromToken(token);
                    if (prefab != null)
                        added.m_dropPrefab = prefab;
                }
                return true;
            }

            GameObject go = ItemIds.PrefabFromToken(token);
            if (go == null && ObjectDB.instance != null)
                go = ObjectDB.instance.GetItemPrefab(token);
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop?.m_itemData == null)
                return false;

            ItemDrop.ItemData clone = drop.m_itemData.Clone();
            clone.m_stack = amount;
            clone.m_quality = quality;
            clone.m_variant = variant;
            clone.m_crafterID = crafterId;
            clone.m_crafterName = crafterName ?? "";
            clone.m_worldLevel = worldLevel;
            clone.m_dropPrefab = go;
            return inv.AddItem(clone);
        }

        private static bool DepositLocal(Container chest, Inventory from, ItemDrop.ItemData item, int amount)
        {
            // Fresh ZDO Load — stale in-memory inventories after leaving the area were the
            // main "Stored N stacks" / items still in bag / no ping bug.
            // TryReadyForWrite also blocks the empty-local / full-ZDO wipe race.
            if (!ContainerFilter.TryReadyForWrite(chest))
                return false;
            Inventory inv = chest.GetInventory();
            if (inv == null || item == null || item.m_shared == null)
                return false;

            int take = Mathf.Min(amount, item.m_stack);
            int fit = ChestPicker.AmountThatFits(inv, item);
            take = Mathf.Min(take, fit);
            if (take <= 0)
                return false;

            string shared = item.m_shared.m_name;
            int before = inv.CountItems(shared, item.m_quality, true);

            ItemDrop.ItemData clone = item.Clone();
            clone.m_stack = take;
            inv.AddItem(clone);

            int added = inv.CountItems(shared, item.m_quality, true) - before;
            if (added <= 0)
                return false;

            from.RemoveItem(item, added);
            Refs.NotifyChanged(from);
            ContainerFilter.SaveInventory(chest);
            Highlight(chest);
            return true;
        }

        public static int TakeIntoExistingStack(Container chest, ItemDrop.ItemData dest, int amount)
        {
            if (chest == null || dest == null || dest.m_shared == null || amount <= 0)
                return 0;
            if (ChestNames.IsIgnored(chest))
                return 0;

            // Do not steal ownership from someone using the chest.
            if (IsChestOwner(chest))
            {
                if (!ContainerFilter.TryReadyForWrite(chest))
                    return 0;
                return TakeIntoExistingStackLocal(chest, dest, amount);
            }

            if (chest.IsInUse())
                return 0;

            // Safe path: ask owner to remove; Grant merges into inventory stacks.
            Player player = Player.m_localPlayer;
            if (player == null)
                return 0;
            Withdraw(chest, dest.m_shared.m_name, amount, player.GetInventory(),
                Plugin.Settings != null && Plugin.Settings.LeaveOneItem.Value);
            FillsQueued++;
            return 0;
        }

        private static int TakeIntoExistingStackLocal(Container chest, ItemDrop.ItemData dest, int amount)
        {
            Inventory inv = chest.GetInventory();
            if (inv == null || dest == null || dest.m_shared == null || amount <= 0)
                return 0;

            int taken = 0;
            var items = new List<ItemDrop.ItemData>(inv.GetAllItems());
            int available = 0;
            foreach (ItemDrop.ItemData src in items)
            {
                if (CanFillFrom(dest, src))
                    available += src.m_stack;
            }
            if (Plugin.Settings != null && Plugin.Settings.LeaveOneItem.Value && available > 0)
                available -= 1;
            amount = Mathf.Min(amount, available);
            if (amount <= 0)
                return 0;

            foreach (ItemDrop.ItemData src in items)
            {
                if (taken >= amount)
                    break;
                if (!CanFillFrom(dest, src))
                    continue;

                int move = Mathf.Min(amount - taken, src.m_stack);
                if (move <= 0)
                    continue;

                dest.m_stack += move;
                src.m_stack -= move;
                taken += move;
                if (src.m_stack <= 0)
                    inv.RemoveItem(src);
            }

            if (taken <= 0)
                return 0;

            Player player = Player.m_localPlayer;
            if (player != null)
                Refs.NotifyChanged(player.GetInventory());
            Refs.NotifyChanged(inv);
            ContainerFilter.SaveInventory(chest);
            Highlight(chest);
            return taken;
        }

        private static bool CanFillFrom(ItemDrop.ItemData dest, ItemDrop.ItemData src)
        {
            if (dest?.m_shared == null || src?.m_shared == null || src.m_stack <= 0)
                return false;
            if (dest.m_shared.m_name != src.m_shared.m_name)
                return false;
            if (dest.m_worldLevel != src.m_worldLevel)
                return false;
            if (dest.m_shared.m_maxStackSize <= 1)
                return dest.m_quality == src.m_quality;
            return true;
        }

        private static bool StoreDropRemote(Container chest, ItemDrop drop)
        {
            if (drop == null || drop.m_itemData == null)
                return false;
            if (!ContainerFilter.IsPlayerBuiltStorage(chest))
                return false;

            ZNetView dropView = Refs.View(drop);
            if (dropView == null || !dropView.IsValid() || !dropView.IsOwner())
                return false;

            ZNetView nv = Refs.View(chest);
            if (nv == null || !nv.IsValid())
                return false;

            ItemDrop.ItemData item = drop.m_itemData;
            int take = item.m_stack;
            Inventory chestInv = chest.GetInventory();
            if (chestInv != null)
            {
                int fit = ChestPicker.AmountThatFits(chestInv, item);
                take = Mathf.Min(take, fit);
                if (take <= 0)
                    return false;
            }

            string prefabName = ItemIds.PrefabName(item) ?? ItemIds.SharedName(item) ?? "";
            if (string.IsNullOrEmpty(prefabName) || take <= 0)
                return false;

            var pkg = new ZPackage();
            pkg.Write(prefabName);
            pkg.Write(take);
            pkg.Write(item.m_quality);
            pkg.Write(item.m_variant);
            pkg.Write(item.m_crafterID);
            pkg.Write(item.m_crafterName ?? "");
            nv.InvokeRPC(RpcDeposit, pkg);
            ConsumeDrop(drop, take);
            return true;
        }

        private static bool StoreDropLocal(Container chest, ItemDrop drop)
        {
            if (drop == null || drop.m_itemData == null)
                return false;
            if (!ContainerFilter.IsPlayerBuiltStorage(chest))
                return false;

            ZNetView dropView = Refs.View(drop);
            if (dropView == null || !dropView.IsValid() || !dropView.IsOwner())
                return false;

            if (!ContainerFilter.TryReadyForWrite(chest))
                return false;

            Inventory inv = chest.GetInventory();
            if (inv == null)
                return false;

            int take = ChestPicker.AmountThatFits(inv, drop.m_itemData);
            if (take <= 0)
                return false;

            ItemDrop.ItemData clone = drop.m_itemData.Clone();
            clone.m_stack = take;
            if (!inv.AddItem(clone))
                return false;

            ConsumeDrop(drop, take);
            ContainerFilter.SaveInventory(chest);
            Highlight(chest);
            return true;
        }

        private static void ConsumeDrop(ItemDrop drop, int take)
        {
            if (drop == null || drop.m_itemData == null || take <= 0)
                return;

            int left = drop.m_itemData.m_stack - take;
            if (left > 0)
            {
                drop.SetStack(left);
                return;
            }

            if (ZNetScene.instance != null)
                ZNetScene.instance.Destroy(drop.gameObject);
            else
                Object.Destroy(drop.gameObject);
        }

        private static int WithdrawLocal(Container chest, string sharedName, int amount, Inventory playerInv, bool leaveOne)
        {
            Inventory inv = chest.GetInventory();
            if (inv == null || playerInv == null || amount <= 0 || string.IsNullOrEmpty(sharedName))
                return 0;

            // Clone real chest stacks so quality / worldLevel stay valid for crafting & storing.
            // AddItem(prefab, n) spawned worldLevel-0 items that could not finish crafts or be re-stored.
            var items = new List<ItemDrop.ItemData>(inv.GetAllItems());
            int available = 0;
            foreach (ItemDrop.ItemData src in items)
            {
                if (src?.m_shared != null && src.m_shared.m_name == sharedName && src.m_stack > 0)
                    available += src.m_stack;
            }
            if (leaveOne && available > 0)
                available -= 1;

            int take = Mathf.Min(amount, available);
            if (take <= 0)
                return 0;

            Player player = Player.m_localPlayer;
            int taken = 0;
            foreach (ItemDrop.ItemData src in items)
            {
                if (taken >= take)
                    break;
                if (src?.m_shared == null || src.m_stack <= 0)
                    continue;
                if (src.m_shared.m_name != sharedName)
                    continue;

                int move = Mathf.Min(take - taken, src.m_stack);
                if (player != null && IgnoreWeight == 0)
                    move = StackLimits.FitByWeight(player, src, move);
                if (move <= 0)
                    break;

                ItemDrop.ItemData clone = src.Clone();
                clone.m_stack = move;
                clone.m_worldLevel = UsableWorldLevel(src.m_worldLevel);
                clone.m_gridPos = new Vector2i(-1, -1);
                if (clone.m_dropPrefab == null)
                    clone.m_dropPrefab = ItemIds.PrefabFromToken(ItemIds.PrefabName(src) ?? sharedName);

                while (move > 0 && !playerInv.CanAddItem(clone, move))
                {
                    move--;
                    clone.m_stack = move;
                }
                if (move <= 0)
                    break;

                // Prefer real-stack clone; fall back to prefab AddItem if clone path fails
                // (can happen while InventoryGui is closed in hammer mode).
                bool added = playerInv.AddItem(clone);
                if (!added)
                {
                    string prefab = ItemIds.PrefabName(src) ?? sharedName;
                    added = TryAddItem(
                        playerInv,
                        prefab,
                        move,
                        src.m_quality,
                        src.m_variant,
                        src.m_crafterID,
                        src.m_crafterName ?? "",
                        UsableWorldLevel(src.m_worldLevel));
                }
                if (!added)
                    break;

                src.m_stack -= move;
                taken += move;
                if (src.m_stack <= 0)
                    inv.RemoveItem(src);
            }

            if (taken <= 0)
                return 0;

            Refs.NotifyChanged(playerInv);
            ContainerFilter.SaveInventory(chest);
            return taken;
        }

        private static int ConsumeLocal(Container chest, string sharedName, int amount, bool leaveOne, int quality)
        {
            if (chest == null || amount <= 0 || string.IsNullOrEmpty(sharedName))
                return 0;

            if (!ContainerFilter.TryReadyForWrite(chest))
                return 0;

            Inventory inv = chest.GetInventory();
            if (inv == null)
                return 0;

            var items = new List<ItemDrop.ItemData>(inv.GetAllItems());
            int available = 0;
            foreach (ItemDrop.ItemData src in items)
            {
                if (!MatchesConsume(src, sharedName, quality))
                    continue;
                available += src.m_stack;
            }
            if (leaveOne && available > 0)
                available -= 1;

            int take = Mathf.Min(amount, available);
            if (take <= 0)
                return 0;

            int taken = 0;
            foreach (ItemDrop.ItemData src in items)
            {
                if (taken >= take)
                    break;
                if (!MatchesConsume(src, sharedName, quality))
                    continue;

                int move = Mathf.Min(take - taken, src.m_stack);
                if (move <= 0)
                    continue;

                src.m_stack -= move;
                taken += move;
                if (src.m_stack <= 0)
                    inv.RemoveItem(src);
            }

            if (taken <= 0)
                return 0;

            ContainerFilter.SaveInventory(chest);
            return taken;
        }

        private static bool MatchesConsume(ItemDrop.ItemData src, string sharedName, int quality)
        {
            if (src?.m_shared == null || src.m_stack <= 0)
                return false;
            if (src.m_shared.m_name != sharedName)
                return false;
            if (quality >= 0 && src.m_quality != quality)
                return false;
            return true;
        }

        private static float MaxStoreRange()
        {
            if (Plugin.Settings == null)
                return 12f;
            return Mathf.Max(Plugin.Settings.MaxGameplayRange(), Plugin.Settings.AutoFillPlayerToChestRange());
        }

        private static bool ValidateRpc(Container container, long sender, float range)
        {
            if (!ContainerFilter.IsUsable(container))
                return false;
            if (!PrivateArea.CheckAccess(container.transform.position, 0f, false, true))
                return false;
            // AutomationLoadedArea: auto-fill / auto-store of a far station runs on the player who has it
            // loaded, so a chest in the sender's loaded area is enough.
            if (!RequirementBridge.SenderInRange(sender, container.transform.position, range)
                && !(Plugin.Settings != null && Plugin.Settings.AutomationLoadedArea.Value
                    && RequirementBridge.SenderLoadedArea(sender, container.transform.position)))
                return false;
            return true;
        }

        private static bool IsChestOwner(Container chest)
        {
            ZNetView nv = Refs.View(chest);
            return nv != null && nv.IsValid() && nv.IsOwner();
        }

        private static void Enqueue(PendingMove op)
        {
            if (Queue.Count > 80)
                return;
            Queue.Enqueue(op);
        }

        public static void Highlight(Container chest)
        {
            if (chest == null || Plugin.Settings == null)
                return;

            if (Plugin.Settings.HighlightOnStore.Value)
                Flash(chest);

            if (Plugin.Settings.PingOnStore.Value && Chat.instance != null)
                Chat.instance.SendPing(chest.transform.position);
        }

        /// <summary>Always flash WearNTear highlight (used by item search).</summary>
        public static void Flash(Container chest)
        {
            if (chest == null)
                return;
            WearNTear wnt = chest.GetComponent<WearNTear>()
                ?? chest.GetComponentInParent<WearNTear>()
                ?? chest.GetComponentInChildren<WearNTear>();
            if (wnt != null)
                wnt.Highlight();
        }
    }
}
