using System.Numerics;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Server.World.Core.Interaction;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Where a player may use the auction house and the mailbox (audit H4).
    ///
    /// ArcheAge-style: the auction house is an auctioneer you walk to, the
    /// mailbox is a mailbox you walk to. Opening either through Interact
    /// remembers WHICH NPC it was (PlayerSession.MarketTargetId); every
    /// later auction or mail packet is then checked against that NPC -
    /// still there, still offering that action, and the player still
    /// standing next to it. Same rule ShopManager uses for merchants: a
    /// client that keeps the window open and walks away can't keep trading
    /// from across the map, and a client that never opened it can't at all.
    ///
    /// The cash shop is deliberately NOT gated: it's the HUD's "Cash Shop"
    /// button, open anywhere (as in ArcheAge), and it can't be abused from
    /// range anyway - purchases are one persistence transaction and arrive
    /// by mail, which IS gated.
    ///
    /// Tick thread only.
    /// </summary>
    public class MarketAccess
    {
        /// <summary>Slack on top of the NPC's InteractRange for walking about while the window is open.</summary>
        public const float RangeTolerance = 2f;

        private readonly InteractionRegistry _interactions;
        private readonly InteractionActionCatalog _actions;

        public MarketAccess(InteractionRegistry interactions, InteractionActionCatalog actions)
        {
            _interactions = interactions;
            _actions = actions;
        }

        /// <summary>The player opened a market window through this NPC. Called by C2WInteractHandler.</summary>
        public void Opened(PlayerSession session, int targetNetworkId) =>
            session.MarketTargetId = targetNetworkId;

        /// <summary>
        /// May this player use <paramref name="action"/> right now? Tells
        /// the client why not, and returns false, if they may not.
        /// </summary>
        public bool Check(NetPeer peer, PlayerSession session, InteractionActionType action)
        {
            if (session.NetworkId == null)
                return false;

            string where = action == InteractionActionType.Auction ? "an auctioneer" : "a mailbox";

            if (session.MarketTargetId == 0 ||
                !_interactions.TryGet(session.MarketTargetId, out var target) ||
                !_actions.TryGet(target.Kind, target.TemplateId, (int)action, out var offered) ||
                !offered.IsEnabled)
            {
                W2CMarketResultPacketSender.Send(peer, false, $"You need to be at {where} for that.");
                return false;
            }

            if (Vector3.Distance(session.Position, target.Position) > target.InteractRange + RangeTolerance)
            {
                W2CMarketResultPacketSender.Send(peer, false, $"You've walked too far from {where}.");
                return false;
            }

            return true;
        }
    }
}
