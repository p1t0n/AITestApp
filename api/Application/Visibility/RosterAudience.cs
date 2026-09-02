namespace ExpertToJob.Application.Visibility;

/// <summary>
/// What a caller is looking at the roster <em>for</em> (P1T-185) — the one thing that decides
/// whether a paused Expert is filtered away or shown with a badge.
///
/// <para>Not the same question as the ownership scope, and not answerable from the role either: a
/// Service Manager administering the bench must see who paused themselves, and the same Service
/// Manager running a semantic search must not, because that search is about who is available for
/// work. So the availability-shaped surfaces — search, matching, digests, scan enumeration — never
/// ask; they filter unconditionally. This distinction exists only for the surfaces that are
/// genuinely about the record rather than about availability, and there it falls along the host:
/// the Web API administers, the MCP server serves agents.</para>
/// </summary>
public enum RosterAudience
{
    /// <summary>Who is available for work. Paused Experts are not here. Every agent, always.</summary>
    Bench = 1,

    /// <summary>Who the service holds records about. Paused Experts appear, marked — a bench that
    /// silently loses somebody is a bench nobody can explain.</summary>
    Administration = 2
}

/// <summary>
/// Resolves the current caller's <see cref="RosterAudience"/>. Mirrors
/// <c>IOwnershipScopeProvider</c>: the two seams answer different questions and are registered
/// separately, so a host cannot satisfy one by accident while leaving the other open.
/// </summary>
public interface IRosterAudienceProvider
{
    RosterAudience Current { get; }
}

/// <summary>
/// Always <see cref="RosterAudience.Bench"/>. The MCP server's answer — an agent is never
/// administering anything — and the default every host inherits until it says otherwise, because
/// the failure that matters is a paused person reaching a surface that should not have them.
/// </summary>
public sealed class BenchAudienceProvider : IRosterAudienceProvider
{
    public RosterAudience Current => RosterAudience.Bench;
}

/// <summary>
/// Always <see cref="RosterAudience.Administration"/>. The Web API's answer: its roster surfaces
/// are the bench's admin screens, and its one Expert-facing surface shows a person their own row —
/// which they must still reach after pausing it, since they are the one who paused it.
/// </summary>
public sealed class AdministrationAudienceProvider : IRosterAudienceProvider
{
    public RosterAudience Current => RosterAudience.Administration;
}
