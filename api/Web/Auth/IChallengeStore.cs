namespace EmployeeManager.Web.Auth;

/// <summary>
/// Short-lived storage for a WebAuthn ceremony's pending options (which carry the server-issued
/// challenge). The client receives a ceremony id, performs the authenticator step, and posts it
/// back; the endpoint reloads the original options to verify the response. Backed by a distributed
/// cache so it survives across the two request round-trip without relying on cookies/session.
/// </summary>
public interface IChallengeStore
{
    /// <summary>Stash ceremony options (JSON) under a fresh id and return that id.</summary>
    Task<string> StashAsync(string optionsJson, CancellationToken ct = default);

    /// <summary>Fetch and remove the stashed options for an id. Null if missing/expired.</summary>
    Task<string?> ConsumeAsync(string ceremonyId, CancellationToken ct = default);
}
