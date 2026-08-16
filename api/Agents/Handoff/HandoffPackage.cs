namespace CvManager.Agents.Handoff;

/// <summary>
/// The structured handoff package (P1T-132): everything the next step — another agent stage or
/// the human deciding a proposal — needs to trust a run without re-running it. Inputs, provenance
/// (who ran it, under which caps), one <see cref="StageSlice"/> per unit of agent work, and an
/// honest ledger of what degraded along the way.
///
/// <para><b>Authorization state travels as provenance, never as credentials.</b> Slices carry the
/// agent's OAuth client id and scopes — the facts a reviewer needs to judge what the stage was
/// allowed to touch. No type in this envelope has a field that could hold a token, secret, or
/// header; the shape itself is the guarantee, and a test pins the serialized key surface.</para>
/// </summary>
public sealed record HandoffPackage(
    IReadOnlyDictionary<string, string?> Inputs,
    RunProvenance Provenance,
    IReadOnlyList<StageSlice> Slices,
    IReadOnlyList<DegradationEntry> Degradations);

/// <summary>Who initiated the run and the cap state it started under. The caps snapshot is
/// fail-open like the caps themselves: an unreadable usage store yields an empty snapshot, never
/// a failed run.</summary>
public sealed record RunProvenance(
    Guid? CallerUserId,
    IReadOnlyList<CapWindowSnapshot> CapsSnapshotAtStart,
    DateTimeOffset StartedAt);

/// <summary>One cap window as it stood when the run began (mirrors the usage windows).</summary>
public sealed record CapWindowSnapshot(string Window, long Used, long Cap, DateTimeOffset ResetAt);

/// <summary>
/// One stage's slice of the run: which agent identity did the work (null client id for tool-less
/// chat stages — they hold no MCP identity), what model it used, what it cost, when it ran, and
/// how it ended. Failed and skipped slices are first-class: a stage that spent tokens before
/// failing reports both the tokens and the failure.
/// </summary>
public sealed record StageSlice(
    string Stage,
    string? AgentClientId,
    IReadOnlyList<string> Scopes,
    string? ModelId,
    long InputTokens,
    long OutputTokens,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string Status,
    string? DegradeReason = null,
    int? RetryCount = null);

/// <summary>The pinned <see cref="StageSlice.Status"/> values.</summary>
public static class StageSliceStatus
{
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

/// <summary>What a degradation cost the consumer, in their terms — the package-side mirror of the
/// report's notes, split so a reader can weigh the loss without parsing prose.</summary>
public sealed record DegradationEntry(string Stage, string WhatWasLost, string Why);
