using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace EmployeeManager.Web.Auth;

/// <summary>
/// <see cref="IChallengeStore"/> over <see cref="IDistributedCache"/>. Entries expire after the
/// configured challenge timeout and are single-use (removed on consume).
/// </summary>
public sealed class DistributedCacheChallengeStore(
    IDistributedCache cache,
    IOptions<AuthOptions> options) : IChallengeStore
{
    private const string KeyPrefix = "webauthn:ceremony:";
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(options.Value.Passkey.ChallengeTimeoutSeconds);

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
