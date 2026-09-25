using ArcheCore.Network.Shared;
using ArcheCore.Library.Net.Worldserver;
using System.Numerics;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Core.Entities;
using ArcheCore.Server.World.Core.Interaction;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using MessagePack;
using NLog;

namespace ArcheCore.Server.World.Networking.C2W
{
    /// <summary>
    /// The player pressed F or G (or right-clicked) on something. This handler
    /// owns what every interaction shares, then dispatches on the ACTION:
    ///
    ///   1. The target exists and the player is in range.
    ///   2. Work out the action: the one the client sent, or - for a client
    ///      that sends none - the target's F action.
    ///   3. The target actually OFFERS that action (InteractionActionCatalog,
    ///      the same list the client drew its prompts from) and it's enabled.
    ///   4. Dispatch:
    ///        Harvest  -> HarvestManager.TryBeginHarvest
    ///        LootAll  -> LootManager.TryLootAll
    ///        OpenLoot -> LootManager.OpenWindow
    ///        Talk     -> the NPC's Greeting, then Lua OnInteract
    ///        Trade    -> ShopManager.TryOpen
    ///        Climb    -> refused (listed in data, not built yet)
    ///
    /// Step 3 is what stops a modified client asking a rock to trade or
    /// climbing a tree that only offers Chop.
    ///
    /// NPCs with NO action rows keep the old behaviour - Lua OnInteract plus
    /// "open a shop if there is one" - so scripts written before actions
    /// existed still run.
    /// </summary>
    [PacketOpcode(Opcodes.Interact)]
    public class C2WInteractHandler : IPacketHandler
    {
        private readonly PlayerManager _playerManager;
        private readonly InteractionRegistry _interactions;
        private readonly InteractionActionCatalog _actions;
        private readonly HarvestManager _harvest;
        private readonly ShopManager _shops;
        private readonly LootManager _loot;
        private readonly QuestManager _quests;
        private readonly MailManager _mail;
        private readonly AuctionManager _auctions;
        private readonly CashShopManager _cashShop;
        private readonly MarketAccess _market;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public C2WInteractHandler(
            PlayerManager playerManager,
            InteractionRegistry interactions,
            InteractionActionCatalog actions,
            HarvestManager harvest,
            ShopManager shops,
            LootManager loot,
            QuestManager quests,
            MailManager mail,
            AuctionManager auctions,
            CashShopManager cashShop,
            MarketAccess market)
        {
            _market = market;
            _mail = mail;
            _auctions = auctions;
            _cashShop = cashShop;
            _playerManager = playerManager;
            _interactions = interactions;
            _actions = actions;
            _harvest = harvest;
            _shops = shops;
            _loot = loot;
            _quests = quests;
        }

        public void Handle(NetPeer peer, NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<C2WInteractPacket>(reader.GetRemainingBytes());

            if (!_playerManager.TryGetNetworkId(peer, out int playerId))
                return;

            if (_playerManager.TryGetSession(peer, out var session) && session.IsDead)
            {
                W2CInteractDeniedPacketSender.Send(peer, "You can't do that while dead.");
                return;
            }

            // 1
            if (!_interactions.TryGet(packet.TargetNetworkId, out var target))
            {
                W2CInteractDeniedPacketSender.Send(peer, "That's no longer there.");
                return;
            }

            if (!_playerManager.TryGetPosition(playerId, out Vector3 playerPos))
                return;

            float distance = Vector3.Distance(playerPos, target.Position);
            if (distance > target.InteractRange)
            {
                W2CInteractDeniedPacketSender.Send(peer, "Too far away.");
                return;
            }

            // 2
            int actionType = packet.ActionType != 0
                ? packet.ActionType
                : _actions.DefaultActionType(target.Kind, target.TemplateId);

            Logger.Debug($"[Interact] Player {playerId} -> {target.Kind} template {target.TemplateId}, action {(InteractionActionType)actionType}, distance {distance:F2}");

            if (actionType == 0)
            {
                // No actions defined for this object at all.
                if (target is NpcEntity legacyNpc)
                    LegacyNpcInteract(peer, playerId, legacyNpc);
                return;
            }

            // 3
            if (!_actions.TryGet(target.Kind, target.TemplateId, actionType, out var action))
            {
                W2CInteractDeniedPacketSender.Send(peer, "You can't do that.");
                return;
            }

            if (!action.IsEnabled)
            {
                W2CInteractDeniedPacketSender.Send(peer, $"{action.Label} isn't available yet.");
                return;
            }

            // Anything other than harvesting stops a harvest in progress.
            if (actionType != (int)InteractionActionType.Harvest)
                _harvest.CancelFor(playerId, "You stop gathering.");

            // 4
            switch ((InteractionActionType)actionType)
            {
                case InteractionActionType.Harvest:
                    if (_harvest.TryGetNode(packet.TargetNetworkId, out var node))
                        _harvest.TryBeginHarvest(peer, playerId, node);
                    break;

                case InteractionActionType.LootAll:
                    if (_loot.TryGetCorpse(packet.TargetNetworkId, out var corpseAll))
                        _loot.TryLootAll(peer, playerId, corpseAll);
                    break;

                case InteractionActionType.OpenLoot:
                    if (_loot.TryGetCorpse(packet.TargetNetworkId, out var corpseOpen))
                        _loot.OpenWindow(peer, playerId, corpseOpen);
                    break;

                case InteractionActionType.Talk:
                    if (target is NpcEntity talker)
                        Talk(peer, talker);
                    break;

                case InteractionActionType.Trade:
                    if (target is NpcEntity trader && !_shops.TryOpen(peer, trader))
                        W2CInteractDeniedPacketSender.Send(peer, $"{trader.Name} has nothing to trade.");
                    break;

                case InteractionActionType.Quests:
                    if (target is NpcEntity questGiver && session != null)
                        _quests.SendOffers(peer, session, questGiver);
                    break;

                case InteractionActionType.Mailbox:
                    if (_playerManager.TryGetSession(peer, out var mailSession))
                    {
                        _market.Opened(mailSession, packet.TargetNetworkId);
                        _ = _mail.SendMailboxAsync(peer, mailSession);
                    }
                    break;

                case InteractionActionType.Auction:
                    if (_playerManager.TryGetSession(peer, out var auctionSession))
                    {
                        _market.Opened(auctionSession, packet.TargetNetworkId);
                        _ = _auctions.BrowseAsync(peer, auctionSession, search: null, mineOnly: false);
                    }
                    break;

                case InteractionActionType.CashShop:
                    if (_playerManager.TryGetSession(peer, out var cashSession))
                        _ = _cashShop.BrowseAsync(peer, cashSession);
                    break;

                case InteractionActionType.Climb:
                    W2CInteractDeniedPacketSender.Send(peer, "Climbing isn't available yet.");
                    break;

                default:
                    W2CInteractDeniedPacketSender.Send(peer, "You can't do that.");
                    break;
            }
        }

        private void Talk(NetPeer peer, NpcEntity npc)
        {
            if (!string.IsNullOrWhiteSpace(npc.Greeting))
                W2CInteractDialoguePacketSender.Send(peer, npc.Name, npc.Greeting);

            FireLua(peer, npc);
        }

        /// <summary>Pre-actions behaviour, for NPCs with no InteractableActions rows.</summary>
        private void LegacyNpcInteract(NetPeer peer, int playerId, NpcEntity npc)
        {
            _harvest.CancelFor(playerId, "You stop gathering.");
            _shops.TryOpen(peer, npc);
            FireLua(peer, npc);
        }

        private void FireLua(NetPeer peer, NpcEntity npc)
        {
            var luaPlayer = _playerManager.CreateLuaPlayer(peer);
            if (luaPlayer != null)
                _playerManager.FireInteractEvent(luaPlayer, npc);
        }
    }
}