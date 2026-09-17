namespace ArcheCore.LoadTester
{
    /// <summary>
    /// Matches the AllowLoadTestBypass patch in AuthService.ValidateToken.
    /// Produces "loadtest:{n}" tokens the world server accepts directly —
    /// no AuthServer HTTP call, no seeded accounts, no token file. This is
    /// the fastest path to a first run and is what isolates WORLD SERVER
    /// capacity specifically, since auth and its DB never enter the
    /// picture.
    ///
    /// Requires AllowLoadTestBypass: true in the world server's
    /// appsettings for this test run, and it MUST be false again
    /// afterward — see the warning AuthService logs every time this path
    /// is used.
    ///
    /// Switch to HttpLoginTokenSource once you specifically want to
    /// exercise the real AuthServer/persistence pipeline together, not
    /// before.
    /// </summary>
    public sealed class LoadTestBypassTokenSource : ITokenSource
    {
        public Task<string> GetTokenAsync(int botIndex, CancellationToken ct) =>
            Task.FromResult($"loadtest:{botIndex}");
    }
}