using System.Collections.Generic;
using System.Linq;
using ArcheCore.Server.World.GameData.Mounts;
using ArcheCore.Server.World.Networking.W2C;
using ArcheCore.Server.World.Utils.Database.SQLite;
using LiteNetLib;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Mounts. Roadmap N: no second entity, no vehicle - a mounted player is
    /// the same player, moving faster, drawn with a model under them.
    ///
    /// Using a mount item toggles it. Mounting sets the session's speed
    /// multiplier, which is what the movement check allows for, so the
    /// server stays the authority on speed rather than trusting a client
    /// that says it's riding.
    ///
    /// Anything that should interrupt a ride calls Dismount: taking a hit,
    /// attacking, dying, harvesting. That's a rule, not physics, and keeping
    /// it in one method is what stops it being half-applied.
    ///
    /// Not persisted: you log in on foot.
    /// </summary>
    public class MountManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Set when the mounts load. The player spawn sender is static and
        /// needs to name a rider's mount model; same handle pattern as
        /// InteractionActionCatalog.Current and QuestManager.Current.
        /// </summary>
        public static MountManager Current { get; private set; }

        private readonly IDbContextFactory<WorldDataDbContext> _dbFactory;
        private readonly PlayerManager _players;
        private readonly InterestManager _interest;
        private readonly ReplicationManager _replication;

        private Dictionary<int, MountTable> _mounts = new();
        private readonly List<NetPeer> _peerScratch = new();

        public MountManager(
            IDbContextFactory<WorldDataDbContext> dbFactory,
            PlayerManager players,
            InterestManager interest,
            ReplicationManager replication)
        {
            _dbFactory = dbFactory;
            _players = players;
            _interest = interest;
            _replication = replication;
        }

        public void LoadFromDatabase()
        {
            using var db = _dbFactory.CreateDbContext();

            _mounts = new Dictionary<int, MountTable>();
            foreach (var mount in db.Mounts.ToList())
            {
                string problem =
                    string.IsNullOrWhiteSpace(mount.ModelType) ? "ModelType is empty" :
                    mount.SpeedMultiplier < 1f                 ? $"SpeedMultiplier {mount.SpeedMultiplier} would be slower than walking" :
                    mount.SpeedMultiplier > 4f                 ? $"SpeedMultiplier {mount.SpeedMultiplier} is too high - it widens the speed check by the same amount" :
                    null;

                if (problem != null)
                {
                    Logger.Warn("[Mounts] Mount {Id} '{Name}' skipped: {Problem}", mount.Id, mount.Name, problem);
                    continue;
                }

                _mounts[mount.Id] = mount;
            }

            Current = this;
            Logger.Info("[Mounts] Loaded {Count} mount(s).", _mounts.Count);
        }

        public bool TryGetModel(int mountId, out string modelType)
        {
            if (_mounts.TryGetValue(mountId, out var mount))
            {
                modelType = mount.ModelType;
                return true;
            }

            modelType = null;
            return false;
        }

        /// <summary>Using a mount item: get on, or get off if already riding.</summary>
        public bool Toggle(NetPeer peer, PlayerSession session, int mountId)
        {
            if (session.MountId != 0)
            {
                Dismount(peer, session, "You dismount.");
                return true;
            }

            if (session.IsDead)
                return false;

            if (!_mounts.TryGetValue(mountId, out var mount))
            {
                Logger.Warn("[Mounts] Item tried to summon unknown mount {Id}", mountId);
                return false;
            }

            session.MountId = mount.Id;
            session.SpeedMultiplier = mount.SpeedMultiplier;

            Broadcast(peer, session, mount.Id, mount.ModelType, mount.SpeedMultiplier);
            return true;
        }

        /// <summary>
        /// Get off, for any reason. Safe to call when not mounted - which is
        /// what lets every interruption call it without checking first.
        /// </summary>
        public void Dismount(NetPeer peer, PlayerSession session, string reason = null)
        {
            if (session == null || session.MountId == 0)
                return;

            session.MountId = 0;
            session.SpeedMultiplier = 1f;

            Broadcast(peer, session, 0, string.Empty, 1f);

            if (!string.IsNullOrEmpty(reason))
                W2CInteractDeniedPacketSender.Send(peer, reason);
        }

        /// <summary>Convenience for callers that only have the player id (combat, harvesting).</summary>
        public void DismountById(int playerNetworkId, string reason = null)
        {
            if (_players.TryGetPeer(playerNetworkId, out var peer) &&
                _players.TryGetSession(peer, out var session))
                Dismount(peer, session, reason);
        }

        private void Broadcast(NetPeer peer, PlayerSession session, int mountId, string modelType, float multiplier)
        {
            if (session.NetworkId is not int playerId)
                return;

            _peerScratch.Clear();
            _peerScratch.Add(peer);   // the rider always hears about it

            foreach (int observerId in _interest.GetKnownBy(playerId))
            {
                if (SpawnManager.IsNpcId(observerId)) continue;
                if (_players.TryGetPeer(observerId, out var other) && other != peer)
                    _peerScratch.Add(other);
            }

            W2CMountStatePacketSender.Send(_replication, _peerScratch, playerId, mountId, modelType, multiplier);
        }
    }
}
