using ArcheCore.Library.Net.Worldserver;

namespace ArcheCore.Network.Shared
{
    /// <summary>
    /// Which LiteNetLib channel each server-to-client opcode travels on
    /// (audit M4).
    ///
    /// Reliable-ordered delivery is ordered PER CHANNEL. With everything on
    /// channel 0, one big reliable payload (an auction page, the quest
    /// catalogue) that needed a resend held up every combat hit, spawn and
    /// despawn behind it. Now:
    ///
    ///   World (0)  spawns, despawns, combat, movement corrections, gold,
    ///              inventory changes, enter-world - anything the world
    ///              state on screen depends on, in order.
    ///   Bulk  (1)  windows and lists: auction, mail, cash shop, shop,
    ///              quest catalogue/log/updates, item data,
    ///              character list. Ordered among themselves (a catalogue
    ///              still arrives before the log that refers to it).
    ///   Chat  (2)  chat, MOTD, announcements.
    ///
    /// Anything not listed is World, so a new opcode is safe by default.
    /// BOTH NetManagers must be started with ChannelsCount = Count, or
    /// packets on the extra channels are dropped.
    /// </summary>
    public static class PacketChannels
    {
        public const byte World = 0;
        public const byte Bulk  = 1;
        public const byte Chat  = 2;

        /// <summary>Set NetManager.ChannelsCount to this on client AND server.</summary>
        public const byte Count = 3;

        public static byte For(Opcodes opcode)
        {
            switch (opcode)
            {
                case Opcodes.ChatMessage:
                case Opcodes.MOTD:
                case Opcodes.Announcement:
                    return Chat;

                case Opcodes.W2CCharacterList:
                case Opcodes.W2CCreateCharacterFailed:
                case Opcodes.ItemDataResponse:
                case Opcodes.W2CInventorySnapshot:
                case Opcodes.W2CShopOpen:
                case Opcodes.W2CShopResult:
                case Opcodes.W2CQuestCatalog:
                case Opcodes.W2CQuestLog:
                case Opcodes.W2CQuestUpdate:
                case Opcodes.W2CQuestOffers:
                case Opcodes.W2CMailList:
                case Opcodes.W2CAuctionList:
                case Opcodes.W2CMarketResult:
                case Opcodes.W2CCashShopList:
                    return Bulk;

                default:
                    return World;
            }
        }
    }
}
