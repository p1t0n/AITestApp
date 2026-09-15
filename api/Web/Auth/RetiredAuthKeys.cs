namespace ExpertToJob.Web.Auth;

/// <summary>
/// Configuration keys this host used to read and no longer does (P1T-236). A renamed key is the one
/// kind of rename the compiler cannot see: <c>IConfiguration</c> answers an unknown key with
/// <c>null</c>, binding fills the property with its default, and the host boots clean. For
/// <c>Auth:SeedAdministratorEmail</c> that means an environment which still sets the old key gets a
/// database with no Administrator at all — no bootstrap, no promotion, and no complaint — and the
/// first sign of it is an operator who cannot reach the roster they were promised.
///
/// <para>So the old key is not ignored: its presence is a startup failure, naming the key that
/// replaced it. Deployments carry configuration that outlives the code that read it, and an
/// environment variable set two releases ago is exactly the thing nobody re-reads.</para>
/// </summary>
public static class RetiredAuthKeys
{
    /// <summary>Each retired key and the key that replaced it.</summary>
    private static readonly (string Retired, string Replacement)[] Renames =
    [
        ($"{AuthOptions.Section}:SeedServiceManagerEmail", $"{AuthOptions.Section}:SeedAdministratorEmail"),
    ];

    /// <summary>
    /// Throws when configuration still carries a retired key. Called before the host is built, so
    /// the process fails where a misconfiguration is cheap to see rather than at the first sign-in.
    /// </summary>
    /// <exception cref="InvalidOperationException">A retired key is present.</exception>
    public static void ThrowIfPresent(IConfiguration configuration)
    {
        foreach (var (retired, replacement) in Renames)
        {
            // Presence, not truthiness: an empty value is still somebody's deliberate setting, and
            // treating it as absent would let the misconfiguration through on the one deployment
            // that disabled the bootstrap on purpose.
            if (configuration[retired] is not null)
            {
                throw new InvalidOperationException(
                    $"'{retired}' was renamed to '{replacement}' (P1T-236). The old key is no longer " +
                    "read, and leaving it set would silently disable the bootstrap. Rename it in " +
                    $"every environment that sets it (e.g. {retired.Replace(":", "__")} → " +
                    $"{replacement.Replace(":", "__")}).");
            }
        }
    }
}
