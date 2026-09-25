using MessagePack;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer.P2W
{
    public enum CharacterSaveResult : byte
    {
        /// <summary>Written. CurrentSeq == the request's SaveSeq.</summary>
        Saved = 0,

        /// <summary>
        /// Not written: the database already holds this SaveSeq or a newer
        /// one. If CurrentSeq == the request's SaveSeq, this exact request
        /// was applied earlier (a retry after a lost answer) - i.e. success.
        /// </summary>
        Stale = 1,

        /// <summary>No such character for that account. Nothing written.</summary>
        NotFound = 2,

        /// <summary>ClaimMailId no longer exists. Nothing written.</summary>
        MailGone = 3,

        /// <summary>
        /// The request itself is malformed (negative gold, a slot listed
        /// twice...). It will never succeed, so it is not retried. Always a
        /// world server bug - the persistence log says what was wrong.
        /// </summary>
        Invalid = 4
    }

    [MessagePackObject(true)]
    public class P2WCharacterSaveFullResponse
    {
        public CharacterSaveResult Result;

        /// <summary>save_seq in the database after this request.</summary>
        public long CurrentSeq;
    }
}
