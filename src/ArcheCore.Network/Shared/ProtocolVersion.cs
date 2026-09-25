namespace ArcheCore.Network.Shared
{
    /// <summary>
    /// The wire protocol's version (audit, client section). It is part of the
    /// LiteNetLib connection key, so a client built against a different
    /// ArcheCore.Network.dll is refused at connect - with a message saying
    /// so - instead of connecting and then failing on the first packet with
    /// a MessagePackSerializationException that looks like anything else.
    ///
    /// BUMP THIS whenever packets, opcodes, channels or encodings change in
    /// a way an older client can't read. Then rebuild and copy the DLL into
    /// the client's Assets/Plugins, as always.
    ///
    ///   1  everything up to 2026-09-24
    ///   2  2026-09-25: packet channels, velocity codec, create-failed packet
    /// </summary>
    public static class ProtocolVersion
    {
        public const int Current = 2;

        /// <summary>What the client connects with and the server accepts.</summary>
        public const string ConnectionKey = "ArcheCore/2";

        /// <summary>Pull the version out of a connection key, or -1 if it isn't one of ours.</summary>
        public static int Parse(string? key)
        {
            const string prefix = "ArcheCore/";
            if (key == null || !key.StartsWith(prefix)) return -1;
            return int.TryParse(key.Substring(prefix.Length), out int v) ? v : -1;
        }
    }
}
