using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;

namespace ArcheCore.Server.World.Replication
{
    /// <summary>
    /// Builds one W2CWorldSnapshot datagram for one observer.
    ///
    /// Writes straight into a rented byte[] rather than going through
    /// MessagePack. Two reasons, both about the 20k target:
    ///
    ///   1. MessagePack's per-field key/type overhead is roughly 3-4x the
    ///      actual payload for a struct this small. At 20k players x 10Hz x
    ///      ~100 entities each, that overhead alone is multiple Gbps.
    ///   2. MessagePack serialization allocates. This runs 200,000 times a
    ///      second at target load; anything that allocates here turns into
    ///      Gen2 pressure, and a 200ms Gen2 pause rubber-bands every player
    ///      on the shard simultaneously.
    ///
    /// Buffers come from ArrayPool and MUST be returned via Dispose (or a
    /// using block). LiteNetLib copies the payload into its own send buffer
    /// synchronously inside Send(), so returning the array immediately
    /// after Send() is safe.
    ///
    /// Everything reliable - spawns, despawns, chat, combat results - stays
    /// on the existing W2C senders. This packet is unreliable and carries
    /// ONLY movement for entities the client has already been told about.
    /// That split is deliberate: an unreliable packet is allowed to be
    /// dropped, so it must never be the only delivery of state the client
    /// cannot recover from.
    ///
    /// ENTRIES ARE NOW VARIABLE LENGTH. An entry is 12 bytes without
    /// velocity and 15 with it, and which one it is has to be read off the
    /// flags byte. SnapshotReader on the client does exactly that; if you
    /// add another optional field, add it to BOTH sides in the same commit
    /// or the reader will desync mid-packet and misparse every entry after
    /// the first one that differs.
    /// </summary>
    public struct SnapshotWriter : IDisposable
    {
        // Header: opcode(2) + tick(4) + origin(12) + entryCount(2)
        private const int HeaderSize = 20;
        private const int EntryCountOffset = 18;

        // Entry sizes: id(4) + flags(1) + pos(6) + yaw(1) [+ velocity(3)]
        private const int BaseEntrySize = 12;
        private const int VelocityBytes = 3;

        /// <summary>
        /// Worst case, used by IsFull. Reserving the velocity bytes even
        /// for an entry that may not carry them costs at most 3 bytes of
        /// unused budget at the tail of the packet, and in exchange IsFull
        /// never has to know what the next entry will look like.
        /// </summary>
        private const int MaxEntrySize = BaseEntrySize + VelocityBytes;

        private byte[] _buffer;
        private int _offset;
        private ushort _entryCount;

        private readonly int _originX;
        private readonly int _originY;
        private readonly int _originZ;
        private readonly int _capacity;

        /// <param name="mtuBudget">
        /// Hard cap on datagram size. LiteNetLib throws TooBigPacketException
        /// above 1023 bytes for Unreliable/ReliableSequenced sends — this
        /// default leaves real headroom under THAT ceiling, not under a
        /// generic internet path MTU. Getting this wrong doesn't fail
        /// gracefully: it throws from inside the send call, and left
        /// uncaught it takes the whole tick loop down with it (see the
        /// try/catch added around SnapshotDispatcher.Flush's caller too).
        /// </param>
        public SnapshotWriter(ushort opcode, uint tick, Vector3 origin, int mtuBudget = 900)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(mtuBudget);
            _capacity = mtuBudget;
            _entryCount = 0;

            _originX = (int)MathF.Round(origin.X);
            _originY = (int)MathF.Round(origin.Y);
            _originZ = (int)MathF.Round(origin.Z);

            var span = _buffer.AsSpan();
            BinaryPrimitives.WriteUInt16LittleEndian(span[0..], opcode);
            BinaryPrimitives.WriteUInt32LittleEndian(span[2..], tick);
            BinaryPrimitives.WriteInt32LittleEndian(span[6..], _originX);
            BinaryPrimitives.WriteInt32LittleEndian(span[10..], _originY);
            BinaryPrimitives.WriteInt32LittleEndian(span[14..], _originZ);
            BinaryPrimitives.WriteUInt16LittleEndian(span[EntryCountOffset..], 0);

            _offset = HeaderSize;
        }

        public readonly int Length => _offset;
        public readonly byte[] Buffer => _buffer;
        public readonly ushort EntryCount => _entryCount;

        /// <summary>True when another entry would exceed the MTU budget.</summary>
        public readonly bool IsFull => _offset + MaxEntrySize > _capacity;

        /// <summary>
        /// Appends one entity's movement state. Returns false if the packet
        /// is full, which is a normal condition, not an error: the caller
        /// sorts by priority before writing, so a full packet means the
        /// low-priority tail got dropped this tick and will be picked up on
        /// the next one. That is exactly the behavior you want in a crowded
        /// hub - degrade the far-away stuff, never the nearby stuff.
        /// </summary>
        /// <param name="includeVelocity">
        /// Only true for near-tier entities. A far-tier entity updates
        /// every 10th tick; by the time the client would have extrapolated
        /// anywhere useful the next real update has landed, so the three
        /// bytes buy nothing and cost ~25% of the entry. This is the reason
        /// velocity is a flag rather than a fixed field.
        /// </param>
        public bool TryWriteEntity(
            int networkId,
            Vector3 position,
            Vector3 velocity,
            float yaw,
            bool isNpc,
            bool includeVelocity)
        {
            if (IsFull) return false;

            var flags = EntityStateCodec.EntryFlags.Position | EntityStateCodec.EntryFlags.Yaw;
            if (isNpc)           flags |= EntityStateCodec.EntryFlags.IsNpc;
            if (includeVelocity) flags |= EntityStateCodec.EntryFlags.Velocity;

            var span = _buffer.AsSpan(_offset);

            BinaryPrimitives.WriteInt32LittleEndian(span, networkId);
            span[4] = (byte)flags;
            BinaryPrimitives.WriteInt16LittleEndian(span[5..],  EntityStateCodec.Quantize(position.X, _originX));
            BinaryPrimitives.WriteInt16LittleEndian(span[7..],  EntityStateCodec.Quantize(position.Y, _originY));
            BinaryPrimitives.WriteInt16LittleEndian(span[9..],  EntityStateCodec.Quantize(position.Z, _originZ));
            span[11] = EntityStateCodec.QuantizeYaw(yaw);

            var written = BaseEntrySize;

            if (includeVelocity)
            {
                span[12] = (byte)EntityStateCodec.QuantizeVelocity(velocity.X);
                span[13] = (byte)EntityStateCodec.QuantizeVelocity(velocity.Y);
                span[14] = (byte)EntityStateCodec.QuantizeVelocity(velocity.Z);
                written += VelocityBytes;
            }

            _offset += written;
            _entryCount++;
            return true;
        }

        /// <summary>Backfills the entry count into the header. Call before sending.</summary>
        public void Finish()
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                _buffer.AsSpan(EntryCountOffset), _entryCount);
        }

        public void Dispose()
        {
            if (_buffer == null) return;
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }
    }
}