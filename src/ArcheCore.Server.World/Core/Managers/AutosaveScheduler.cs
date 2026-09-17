using System;
using LiteNetLib;
using NLog;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Periodic character saves, spread evenly over the interval.
    ///
    /// Each player is assigned a slot by (networkId % intervalTicks). On every
    /// tick only the players in that tick's slot are considered, so with a
    /// 120s interval at 20Hz (2400 ticks) and 2400 players you save about one
    /// player per tick instead of 2400 at once. No per-player timer state is
    /// needed.
    ///
    /// Only dirty sessions (moved or levelled since the last save) are sent.
    /// Tick thread only.
    /// </summary>
    public sealed class AutosaveScheduler
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly SessionManager _sessions;
        private readonly CharacterPersistence _persistence;
        private readonly uint _intervalTicks;

        public AutosaveScheduler(
            SessionManager sessions,
            CharacterPersistence persistence,
            int intervalSeconds,
            int tickRate)
        {
            _sessions = sessions;
            _persistence = persistence;

            _intervalTicks = intervalSeconds > 0
                ? (uint)Math.Max(1, intervalSeconds * tickRate)
                : 0;

            if (_intervalTicks == 0)
                Logger.Warn("[Autosave] Disabled (World:AutosaveIntervalSeconds <= 0).");
            else
                Logger.Info($"[Autosave] Every {intervalSeconds}s, spread over {_intervalTicks} ticks.");
        }

        public void Tick(uint tick)
        {
            if (_intervalTicks == 0)
                return;

            uint slot = tick % _intervalTicks;

            foreach (NetPeer peer in _sessions.GetAllConnectedPeers())
            {
                if (peer.Tag is not PlayerSession { NetworkId: int networkId } session)
                    continue;

                if ((uint)networkId % _intervalTicks != slot)
                    continue;

                if (!session.IsDirty)
                    continue;

                _persistence.SaveInBackground(session);
            }
        }
    }
}
