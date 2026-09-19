using System;

namespace ArcheCore.Network.Shared
{
    /// <summary>
    /// What a character is DOING, as opposed to where it is. Lives in the
    /// shared network library so the client that produces it and the server
    /// that relays it compile against one definition - a client-side copy
    /// that drifts from the server's is a silent wire break, not a compile
    /// error.
    ///
    /// This is the field that was missing, and no amount of interpolation
    /// substitutes for it. Position and velocity tell an observer where to
    /// draw a character; they do not say whether it is running, falling,
    /// or standing still with a stiff breeze behind it. Without a state
    /// field a remote character can never be animated correctly, because
    /// the observing client has nothing to select an animation from.
    ///
    /// A bitfield rather than an enum of exclusive states, following
    /// ArcheAge, which does the same thing with its own move-type flags -
    /// moving, stopping, in-combat, standing-on-object - and famously
    /// encodes jumping as moving and stopping set together. Real
    /// characters are in several states at once (running AND airborne AND
    /// in combat), and a bitfield says that directly instead of needing a
    /// combinatorial enum.
    ///
    /// ONE BYTE, FIXED WIDTH, SENT ON EVERY ENTRY. It can't be
    /// change-gated: SnapshotDispatcher deliberately keeps no per
    /// observer-entity state (that's O(n^2) memory at 20k players), so
    /// "has this changed since I last told THIS observer" is not a
    /// question it can answer. Every entry carries the current state.
    ///
    /// TRUST: this is client-reported and a client can lie about it. It is
    /// presentation-only today, so a lie makes the liar's animation look
    /// wrong to other people and nothing else. That stops being true the
    /// moment any server logic reads it - if you ever gate a speed limit
    /// on Gliding, or an interrupt on InCombat, the server has to verify
    /// the claim (does this character actually have a glider equipped)
    /// rather than believe the byte.
    /// </summary>
    [Flags]
    public enum MovementState : byte
    {
        None = 0,

        /// <summary>Under its own horizontal power. Set by the client today.</summary>
        Moving = 1 << 0,

        /// <summary>Not in contact with the ground. Set by the client today.</summary>
        Airborne = 1 << 1,

        /// <summary>
        /// Airborne because of a deliberate jump, as opposed to walking off
        /// something. Falling is Airborne without this, which is why there
        /// is no separate Falling bit - it would be derivable and therefore
        /// a second source of truth that could disagree. Set by the client
        /// today.
        /// </summary>
        Jumping = 1 << 2,

        // --- Reserved. Nothing sets these yet. ---
        //
        // Defined now rather than later because the byte is fixed-width and
        // already on the wire: claiming the bit meanings up front costs
        // nothing, while adding them later to a shipped format means every
        // client and server has to change in the same deploy.

        /// <summary>In water, above the swim threshold. Nothing sets this yet.</summary>
        Swimming = 1 << 3,

        /// <summary>Glider deployed. Nothing sets this yet.</summary>
        Gliding = 1 << 4,

        /// <summary>In combat - drives weapon-drawn stance. Nothing sets this yet.</summary>
        InCombat = 1 << 5,

        /// <summary>Sitting, resting, or otherwise deliberately idle. Nothing sets this yet.</summary>
        Sitting = 1 << 6,

        /// <summary>Dead or downed. Nothing sets this yet.</summary>
        Dead = 1 << 7,
    }
}