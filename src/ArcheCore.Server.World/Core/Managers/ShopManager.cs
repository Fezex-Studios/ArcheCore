using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Core.Entities;
using ArcheCore.Server.World.GameData.Shops;
using ArcheCore.Server.World.Networking.W2C;
using ArcheCore.Server.World.Utils.Database.SQLite;
using LiteNetLib;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// NPC merchants. Roadmap F.
    ///
    /// A shop hangs off an NPC TEMPLATE (ShopTemplate.NpcTemplateId), so
    /// interacting with any NPC spawned from that template opens it. The
    /// list is fixed, stock is unlimited, and every price the client sees
    /// is display-only: each buy/sell is re-priced here from the server's
    /// own table.
    ///
    /// EVERY TRANSACTION IS CHECK-EVERYTHING-THEN-CHANGE
    ///
    ///   Buy:  merchant real + in range -> item on the list and for sale
    ///         -> quantity sane -> can afford -> room in the bag
    ///         -> THEN take gold, THEN add item.
    ///   Sell: merchant real + in range -> slot has something
    ///         -> merchant buys it -> gold won't overflow
    ///         -> THEN take item, THEN pay.
    ///
    /// All checks happen before either change, and everything runs on the
    /// single tick thread, so nothing can slip in between the two steps.
    /// The one "can't happen" failure (item add failing after the gold was
    /// taken) refunds and logs an error rather than leaving the player out
    /// of pocket.
    ///
    /// Gold and items only ever change through PlayerManager.TryAddGold /
    /// TryAddItem / TryTakeFromSlot - the same single paths as everything
    /// else, so the client hears about both through the normal
    /// W2CGoldUpdate / W2CInventorySlotChanged packets.
    /// </summary>
    public class ShopManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>Most of one item a single buy request can ask for.</summary>
        public const int MaxBuyQuantity = 100;

        /// <summary>Same slack HarvestManager uses on range checks.</summary>
        private const float RangeTolerance = 1.0f;

        private readonly IDbContextFactory<WorldDataDbContext> _dbFactory;
        private readonly ItemManager _items;
        private readonly PlayerManager _players;
        private readonly SpawnManager _spawnManager;

        private sealed class Shop
        {
            public ShopTemplate Template;
            public ShopItem[] Rows;                       // sorted, for the open packet
            public Dictionary<int, ShopItem> ByItemId;    // for pricing a buy/sell
        }

        private readonly Dictionary<int, Shop> _byNpcTemplate = new();

        public ShopManager(
            IDbContextFactory<WorldDataDbContext> dbFactory,
            ItemManager items,
            PlayerManager players,
            SpawnManager spawnManager)
        {
            _dbFactory = dbFactory;
            _items = items;
            _players = players;
            _spawnManager = spawnManager;
        }

        /// <summary>Call once at boot, after ItemManager has loaded.</summary>
        public void LoadFromDatabase()
        {
            using var db = _dbFactory.CreateDbContext();

            var rowsByShop = db.ShopItems.ToList()
                .GroupBy(r => r.ShopId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var template in db.Shops.ToList())
            {
                if (_byNpcTemplate.ContainsKey(template.NpcTemplateId))
                {
                    Logger.Warn("[Shop] NPC template {Npc} has more than one shop - keeping the first, skipping shop {Id}",
                        template.NpcTemplateId, template.Id);
                    continue;
                }

                var rows = new List<ShopItem>();
                var byItem = new Dictionary<int, ShopItem>();

                if (rowsByShop.TryGetValue(template.Id, out var candidates))
                {
                    foreach (var row in candidates.OrderBy(r => r.SortOrder).ThenBy(r => r.Id))
                    {
                        string problem =
                            !_items.Exists(row.ItemId)            ? $"ItemId {row.ItemId} is not in Items" :
                            row.BuyPrice < 0 || row.SellPrice < 0 ? "negative price" :
                            row.BuyPrice == 0 && row.SellPrice == 0 ? "neither buys nor sells (both prices 0)" :
                            byItem.ContainsKey(row.ItemId)        ? "duplicate item in this shop" :
                            null;

                        if (problem != null)
                        {
                            Logger.Warn("[Shop] Shop {Shop} row {Row} skipped: {Problem}", template.Id, row.Id, problem);
                            continue;
                        }

                        rows.Add(row);
                        byItem[row.ItemId] = row;
                    }
                }

                _byNpcTemplate[template.NpcTemplateId] = new Shop
                {
                    Template = template,
                    Rows = rows.ToArray(),
                    ByItemId = byItem
                };
            }

            Logger.Info("[Shop] Loaded {Count} shop(s).", _byNpcTemplate.Count);
        }

        public bool HasShop(NpcEntity npc) => _byNpcTemplate.ContainsKey(npc.TemplateId);

        // ── Open ─────────────────────────────────────────────────────

        /// <summary>
        /// Called by C2WInteractHandler after its own range check. Returns
        /// false if this NPC isn't a merchant, so the caller can fall
        /// through to ordinary dialogue.
        /// </summary>
        public bool TryOpen(NetPeer peer, NpcEntity npc)
        {
            if (!_byNpcTemplate.TryGetValue(npc.TemplateId, out var shop))
                return false;

            var items = new ShopItemData[shop.Rows.Length];
            for (int i = 0; i < shop.Rows.Length; i++)
            {
                var row = shop.Rows[i];
                items[i] = new ShopItemData
                {
                    ItemTemplateId = row.ItemId,
                    ItemName       = ItemName(row.ItemId),
                    BuyPrice       = row.BuyPrice,
                    SellPrice      = row.SellPrice
                };
            }

            W2CShopOpenPacketSender.Send(peer, npc.NetworkId, shop.Template.Id, shop.Template.Name, items);
            return true;
        }

        // ── Buy ──────────────────────────────────────────────────────

        public void TryBuy(NetPeer peer, int npcNetworkId, int itemTemplateId, int quantity)
        {
            if (!TryResolveMerchant(peer, npcNetworkId, out var shop))
                return;

            if (!shop.ByItemId.TryGetValue(itemTemplateId, out var row) || row.BuyPrice <= 0)
            {
                Fail(peer, "That isn't for sale here.");
                return;
            }

            if (quantity < 1 || quantity > MaxBuyQuantity)
            {
                Fail(peer, $"You can buy between 1 and {MaxBuyQuantity} at a time.");
                return;
            }

            if (!_players.TryGetSession(peer, out var session))
                return;

            long total = (long)row.BuyPrice * quantity;
            if (total > session.Inventory.Gold)
            {
                Fail(peer, $"You need {total}g for that - you have {session.Inventory.Gold}g.");
                return;
            }

            if (!_players.CanAddItem(peer, itemTemplateId, quantity))
            {
                Fail(peer, "Your inventory is full.");
                return;
            }

            // Every check passed. Pay, then deliver.
            if (!_players.TryAddGold(peer, -(int)total))
            {
                Fail(peer, "You can't afford that.");
                return;
            }

            if (!_players.TryAddItem(peer, itemTemplateId, quantity))
            {
                // Should be impossible - CanAddItem just said yes and nothing
                // else runs on this thread. Refund rather than eat the gold.
                _players.TryAddGold(peer, (int)total);
                Logger.Error("[Shop] Account {Account}: TryAddItem failed after CanAddItem passed (item {Item} x{Qty}) - refunded {Total}g",
                    session.AccountId, itemTemplateId, quantity, total);
                Fail(peer, "Something went wrong - you were not charged.");
                return;
            }

            string name = ItemName(itemTemplateId);
            Logger.Info("[Shop] Account {Account} bought {Qty}x {Item} ({Id}) for {Total}g from shop {Shop}",
                session.AccountId, quantity, name, itemTemplateId, total, shop.Template.Id);

            W2CShopResultPacketSender.Send(peer, true, $"Bought {quantity}x {name} for {total}g.");
        }

        // ── Sell ─────────────────────────────────────────────────────

        public void TrySell(NetPeer peer, int npcNetworkId, int slot, int quantity)
        {
            if (!TryResolveMerchant(peer, npcNetworkId, out var shop))
                return;

            if (!_players.TryPeekSlot(peer, slot, out var contents) || contents.ItemTemplateId == 0)
            {
                Fail(peer, "There's nothing there to sell.");
                return;
            }

            if (!shop.ByItemId.TryGetValue(contents.ItemTemplateId, out var row) || row.SellPrice <= 0)
            {
                Fail(peer, "The merchant won't buy that.");
                return;
            }

            if (!_players.TryGetSession(peer, out var session))
                return;

            int count = quantity <= 0 || quantity > contents.Quantity ? contents.Quantity : quantity;
            long total = (long)row.SellPrice * count;

            if ((long)session.Inventory.Gold + total > int.MaxValue)
            {
                Fail(peer, "You can't carry that much gold.");
                return;
            }

            // Every check passed. Take the goods, then pay.
            if (!_players.TryTakeFromSlot(peer, slot, count, out int itemId, out int removed) || removed != count)
            {
                Logger.Error("[Shop] Account {Account}: TryTakeFromSlot failed after peek (slot {Slot}, wanted {Count}, removed {Removed})",
                    session.AccountId, slot, count, removed);
                Fail(peer, "Something went wrong - nothing was sold.");
                return;
            }

            _players.TryAddGold(peer, (int)total);

            string name = ItemName(itemId);
            Logger.Info("[Shop] Account {Account} sold {Qty}x {Item} ({Id}) for {Total}g to shop {Shop}",
                session.AccountId, removed, name, itemId, total, shop.Template.Id);

            W2CShopResultPacketSender.Send(peer, true, $"Sold {removed}x {name} for {total}g.");
        }

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>
        /// The NPC id is real, it's a merchant, and the player is standing
        /// next to it. Checked on EVERY buy and sell, not just when the
        /// window opens - a client that keeps the window open and walks
        /// off can't keep trading from across the map.
        /// </summary>
        private bool TryResolveMerchant(NetPeer peer, int npcNetworkId, out Shop shop)
        {
            shop = null;

            if (!_players.TryGetSession(peer, out var session) || session.NetworkId == null)
                return false;

            if (!_spawnManager.TryGetNpc(npcNetworkId, out var npc) ||
                !_byNpcTemplate.TryGetValue(npc.TemplateId, out shop))
            {
                Fail(peer, "That merchant is no longer here.");
                return false;
            }

            if (Vector3.Distance(session.Position, npc.Position) > npc.InteractRange + RangeTolerance)
            {
                Fail(peer, "You're too far away from the merchant.");
                shop = null;
                return false;
            }

            return true;
        }

        private string ItemName(int itemTemplateId) =>
            _items.GetById(itemTemplateId)?.name ?? $"#{itemTemplateId}";

        private static void Fail(NetPeer peer, string message) =>
            W2CShopResultPacketSender.Send(peer, false, message);
    }
}
