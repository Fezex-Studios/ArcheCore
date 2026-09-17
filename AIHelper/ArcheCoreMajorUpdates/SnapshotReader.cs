using System;
using System.Buffers.Binary;
using UnityEngine;

namespace ArcheCore.Client.Networking
{
    /// <summary>
    /// Client side of W2CWorldSnapshot. Mirror of SnapshotWriter - if you
    /// change the layout on one side, change it here in the same commit.
    ///
    /// Snapshots are UNRELIABLE and may arrive out of order. The tick field
    /// exists so you can discard stale ones: keep the highest tick applied
    /// per entity and drop anything older. Applying an out-of-order
    /// snapshot is what produces the classic remote-player stutter.
    ///
    /// Entities not present in a snapshot have NOT despawned. They were
    /// either far enough away to be on a slower LOD tier, or past the
    /// per-packet cap this tick. Despawn only ever arrives on the reliable
    /// W2CPlayerLeave / W2CNpcDespawn channel. Treating absence as despawn
    /// will make distant players blink in and out constantly.
    /// </summary>
    public static class SnapshotReader
    {
        private const float PositionScale = 64f;
        private const int HeaderSize = 20;

        [Flags]
        public enum EntryFlags : byte
        {
            None     = 0,
            Position = 1 << 0,
            Yaw      = 1 << 1,
            IsNpc    = 1 << 2,
        }

        public readonly struct Entry
        {
            public readonly int     NetworkId;
            public readonly Vector3 Position;
            public readonly float   Yaw;
            public readonly bool    IsNpc;

            public Entry(int id, Vector3 pos, float yaw, bool isNpc)
            {
                NetworkId = id;
                Position  = pos;
                Yaw       = yaw;
                IsNpc     = isNpc;
            }
        }

        public delegate void EntryHandler(in Entry entry, uint tick);

        /// <summary>
        /// Decodes in place and invokes the handler per entity. No
        /// allocation, no intermediate list - this runs 10x a second with
        /// up to ~100 entries and lives on the Unity main thread.
        /// The opcode is assumed already consumed by the dispatcher.
        /// </summary>
        public static void Read(ReadOnlySpan<byte> payload, EntryHandler onEntry)
        {
            if (payload.Length < HeaderSize - 2)
                return;

            // Offsets are relative to the start of the packet including the
            // 2-byte opcode, matching SnapshotWriter. Caller passes the full
            // packet; we skip the opcode here.
            var tick    = BinaryPrimitives.ReadUInt32LittleEndian(payload[2..]);
            var originX = BinaryPrimitives.ReadInt32LittleEndian(payload[6..]);
            var originY = BinaryPrimitives.ReadInt32LittleEndian(payload[10..]);
            var originZ = BinaryPrimitives.ReadInt32LittleEndian(payload[14..]);
            var count   = BinaryPrimitives.ReadUInt16LittleEndian(payload[18..]);

            var offset = HeaderSize;

            for (int i = 0; i < count; i++)
            {
                if (offset + 5 > payload.Length) break;

                var span  = payload[offset..];
                var id    = BinaryPrimitives.ReadInt32LittleEndian(span);
                var flags = (EntryFlags)span[4];

                offset += 5;

                var position = Vector3.zero;
                var yaw = 0f;

                if ((flags & EntryFlags.Position) != 0)
                {
                    if (offset + 6 > payload.Length) break;

                    var p = payload[offset..];
                    position = new Vector3(
                        originX + BinaryPrimitives.ReadInt16LittleEndian(p)      / PositionScale,
                        originY + BinaryPrimitives.ReadInt16LittleEndian(p[2..]) / PositionScale,
                        originZ + BinaryPrimitives.ReadInt16LittleEndian(p[4..]) / PositionScale);

                    offset += 6;
                }

                if ((flags & EntryFlags.Yaw) != 0)
                {
                    if (offset + 1 > payload.Length) break;
                    yaw = payload[offset] / 256f * (Mathf.PI * 2f);
                    offset += 1;
                }

                var entry = new Entry(id, position, yaw, (flags & EntryFlags.IsNpc) != 0);
                onEntry(in entry, tick);
            }
        }
    }
}
