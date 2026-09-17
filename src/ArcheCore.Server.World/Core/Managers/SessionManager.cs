using System.Collections.Generic;
using LiteNetLib;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Everything to do with "which peer is which player" lives here. This
    /// is the only class that should touch peer.Tag directly.
    /// </summary>
    public class SessionManager
    {
        private int _nextNetworkId = 1;

        // Reverse indexes only - these map FROM an id TO a peer.
        private readonly Dictionary<int, NetPeer> _idToPeer = new();
        private readonly Dictionary<int, NetPeer> _accountToPeer = new();

        public int NextNetworkId() => _nextNetworkId++;

        public bool TryGetSession(NetPeer peer, out PlayerSession session)
        {
            if (peer.Tag is PlayerSession s)
            {
                session = s;
                return true;
            }

            session = null;
            return false;
        }

        public bool TryGetSessionByNetworkId(int networkId, out PlayerSession session)
        {
            if (_idToPeer.TryGetValue(networkId, out var peer) &&
                peer.Tag is PlayerSession s)
            {
                session = s;
                return true;
            }

            session = null;
            return false;
        }

        public bool TryGetNetworkId(NetPeer peer, out int networkId)
        {
            if (peer.Tag is PlayerSession { NetworkId: int id })
            {
                networkId = id;
                return true;
            }

            networkId = -1;
            return false;
        }

        public bool TryGetPeer(int networkId, out NetPeer peer) =>
            _idToPeer.TryGetValue(networkId, out peer);

        public bool TryGetPeerByName(string name, out NetPeer peer)
        {
            foreach (var kvp in _idToPeer)
            {
                if (kvp.Value.Tag is PlayerSession s &&
                    string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    peer = kvp.Value;
                    return true;
                }
            }

            peer = null;
            return false;
        }

        /// <summary>Every spawned (in-world) player's peer.</summary>
        public IEnumerable<NetPeer> GetAllConnectedPeers() => _idToPeer.Values;

        public bool TryGetAccountPeer(int accountId, out NetPeer peer) =>
            _accountToPeer.TryGetValue(accountId, out peer);

        public void RegisterAccountPeer(int accountId, NetPeer peer) =>
            _accountToPeer[accountId] = peer;

        public void UnregisterAccountPeerIfCurrent(int accountId, NetPeer peer)
        {
            if (_accountToPeer.TryGetValue(accountId, out var registered) && registered == peer)
                _accountToPeer.Remove(accountId);
        }

        public void RegisterNetworkId(int networkId, NetPeer peer) =>
            _idToPeer[networkId] = peer;

        public void UnregisterNetworkId(int networkId) =>
            _idToPeer.Remove(networkId);

        /// <summary>
        /// Call once a token has been validated. Creates a session for this
        /// peer with no NetworkId yet - that's what "pending" means.
        /// </summary>
        public void TrackPendingSelection(NetPeer peer, int accountId)
        {
            peer.Tag = new PlayerSession { AccountId = accountId };
        }

        /// <summary>
        /// Non-null only for a peer that authenticated but hasn't spawned
        /// yet. A peer that's already in-world, or never authenticated,
        /// returns null.
        /// </summary>
        public int? GetPendingAccountId(NetPeer peer)
        {
            return peer.Tag is PlayerSession { NetworkId: null } session
                ? session.AccountId
                : null;
        }

        /// <summary>
        /// Claims the one allowed select/create for a pending peer. Returns
        /// false if the peer isn't pending or a request was already accepted
        /// (double-clicked "Enter World", resent packet, etc).
        /// </summary>
        public bool TryBeginSpawn(NetPeer peer)
        {
            if (peer.Tag is PlayerSession { NetworkId: null, SpawnRequested: false } session)
            {
                session.SpawnRequested = true;
                return true;
            }

            return false;
        }

        public int GetLevel(NetPeer peer) =>
            peer.Tag is PlayerSession session ? session.Level : -1;

        public long GetCharacterId(NetPeer peer) =>
            peer.Tag is PlayerSession session ? session.CharacterId : -1;

        public string? GetName(NetPeer peer) =>
            peer.Tag is PlayerSession session ? session.Name : null;
    }
}
