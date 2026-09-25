using System;
using System.Collections.Generic;

namespace ArcheCore.Network.Shared
{
    /// <summary>
    /// What a character name may be (audit H5). Shared by the client (so the
    /// create screen can say what's wrong before sending) and the server
    /// (which decides). Names are also unique, case-insensitively - that is
    /// the database's job (unique index on characters.name).
    ///
    /// Letters and digits only, starting with a letter: no spaces, no
    /// punctuation, and so no way to put TMP rich-text tags (&lt;size=999&gt;),
    /// look-alike separators or control characters into a name that other
    /// players will see in chat, nameplates, mail and whispers.
    /// </summary>
    public static class CharacterNameRules
    {
        public const int MinLength = 3;
        public const int MaxLength = 16;

        /// <summary>Names nobody can create (case-insensitive, exact match).</summary>
        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "admin", "administrator", "gm", "gamemaster", "moderator", "mod", "system",
            "server", "support", "staff", "developer", "dev", "archecore",
            "auctionhouse", "mailbox", "cashshop"
        };

        /// <summary>True if the name is allowed. Otherwise <paramref name="reason"/> says why, for the player.</summary>
        public static bool IsValid(string? name, out string reason)
        {
            name ??= string.Empty;

            if (name.Length < MinLength || name.Length > MaxLength)
            {
                reason = $"Names are {MinLength}-{MaxLength} characters.";
                return false;
            }

            if (!IsAsciiLetter(name[0]))
            {
                reason = "Names start with a letter.";
                return false;
            }

            foreach (char c in name)
            {
                if (!IsAsciiLetter(c) && !(c >= '0' && c <= '9'))
                {
                    reason = "Names can only use letters A-Z and digits 0-9.";
                    return false;
                }
            }

            if (Reserved.Contains(name))
            {
                reason = "That name is reserved.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }
}
