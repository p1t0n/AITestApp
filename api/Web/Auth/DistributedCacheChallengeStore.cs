using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace ExpertToJob.Web.Auth;

/// <summary>
/// Short-lived storage for a WebAuthn ceremony's pending options (which carry the server-issued
/// challenge). The client receives a ceremony id, performs the authenticator step, and posts it
/// back; the endpoint reloads the original options to verify the response. Backed by
/// <see cref="IDistributedCache"/> so it survives the two-request round-trip without relying on
/// cookies/session; entries expire after the configured challenge timeout and are single-use
/// (removed on consume).
/// </summary>
public sealed class DistributedCacheChallengeStore(
    IDistributedCache cache,
    IOptions<AuthOptions> options)
{
    private const string KeyPrefix = "webauthn:ceremony:";
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(options.Value.Passkey.ChallengeTimeoutSeconds);

    /// <summary>Stash ceremony options (JSON) under a fresh id and return that id.</summary>
    public async Task<string> StashAsync(string optionsJson, CancellationToken ct = default)
    {
        var ceremonyId = Guid.NewGuid().ToString("N");
        await cache.SetAsync(
            KeyPrefix + ceremonyId,
            Encoding.UTF8.GetBytes(optionsJson),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl },
            ct);
        return ceremonyId;
    }

    /// <summary>Fetch and remove the stashed options for an id. Null if missing/expired.</summary>
    public async Task<string?> ConsumeAsync(string ceremonyId, CancellationToken ct = default)
    {
        var key = KeyPrefix + ceremonyId;
        var bytes = await cache.GetAsync(key, ct);
        if (bytes is null)
        {
            return null;
        }

        await cache.RemoveAsync(key, ct);
        return Encoding.UTF8.GetString(bytes);
    }
}
