using System.Text.Json;
using ExpertToJob.Agents.Staffing;
using FluentAssertions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The serialized form of the status fields, frozen as literals (EXP-76).
///
/// <para>Every one of these strings is already in somebody's database or already parsed by a client:
/// <c>report.candidates[].match.status</c> and <c>slices[].status</c> sit in the
/// <c>StaffingProposals.PackageJson</c> column of rows written months ago, and the step status rides
/// the SSE stream the SPA's stepper reads. A refactor of the C# types behind them is free; a change
/// to these bytes is a silent break.</para>
///
/// <para>Written as a round trip through a <b>literal</b> document rather than through typed
/// construction, for two reasons. It names no status constant, so it cannot drift with the code it
/// pins. And it asserts both directions at once: a document stored before the change still parses,
/// and what comes back out is spelled the same way it went in.</para>
/// </summary>
public class StatusJsonFreezeTests
{
    /// <summary>A persisted handoff document covering every status value on both fields. The
    /// shape is the one <c>StaffingHandoffDocument.Serialize</c> writes — camelCase, report
    /// unrolled around the package.</summary>
    private const string StoredDocument =
        """
        {
          "inputs": { "jobDescription": "Kafka platform engineer" },
          "report": {
            "requirements": ["Kafka", "Go"],
            "candidates": [
              {
                "expertId": "11111111-1111-1111-1111-111111111111",
                "name": "Ada Lovelace",
                "title": "Staff Engineer",
                "shortlist": {
                  "score": 0.82,
                  "coverage": { "matched": 2, "total": 2 },
                  "requirements": [{ "requirement": "Kafka", "matched": true, "snippet": "Ran the bus." }]
                },
                "match": { "status": "completed", "score": 88, "band": "strong", "answer": "Strong fit.", "error": null },
                "rationale": "Ada matched well."
              },
              {
                "expertId": "22222222-2222-2222-2222-222222222222",
                "name": "Grace Hopper",
                "title": "Platform Engineer",
                "shortlist": {
                  "score": 0.71,
                  "coverage": { "matched": 1, "total": 2 },
                  "requirements": [{ "requirement": "Go", "matched": false, "snippet": null }]
                },
                "match": { "status": "failed", "score": null, "band": null, "answer": null, "error": "model fault" },
                "rationale": "Grace could not be scored."
              },
              {
                "expertId": "33333333-3333-3333-3333-333333333333",
                "name": "Lin Zhao",
                "title": "Engineer",
                "shortlist": {
                  "score": 0.64,
                  "coverage": { "matched": 1, "total": 2 },
                  "requirements": [{ "requirement": "Kafka", "matched": true, "snippet": "Operated brokers." }]
                },
                "match": { "status": "skipped", "score": null, "band": null, "answer": null, "error": "cap reached" },
                "rationale": "Lin was not scored: the token cap was reached."
              }
            ],
            "recommendation": { "expertId": "11111111-1111-1111-1111-111111111111", "narrative": "Pick Ada." },
            "degraded": true,
            "notes": ["One match failed."]
          },
          "provenance": {
            "callerUserId": "44444444-4444-4444-4444-444444444444",
            "capsSnapshotAtStart": [],
            "startedAt": "1970-01-01T00:00:00+00:00"
          },
          "slices": [
            {
              "stage": "shortlist", "agentClientId": "staffing", "scopes": ["roster.read"],
              "modelId": "gpt-4-1-mini", "inputTokens": 10, "outputTokens": 20,
              "startedAt": "1970-01-01T00:00:00+00:00", "completedAt": "1970-01-01T00:00:01+00:00",
              "status": "completed", "degradeReason": null, "retryCount": null
            },
            {
              "stage": "match", "agentClientId": "match", "scopes": ["roster.read"],
              "modelId": "gpt-4-1-mini", "inputTokens": 5, "outputTokens": 0,
              "startedAt": "1970-01-01T00:00:01+00:00", "completedAt": "1970-01-01T00:00:02+00:00",
              "status": "failed", "degradeReason": "model fault", "retryCount": 1
            },
            {
              "stage": "match", "agentClientId": null, "scopes": [],
              "modelId": null, "inputTokens": 0, "outputTokens": 0,
              "startedAt": "1970-01-01T00:00:02+00:00", "completedAt": "1970-01-01T00:00:02+00:00",
              "status": "skipped", "degradeReason": "cap reached", "retryCount": null
            }
          ],
          "degradations": [{ "stage": "match", "whatWasLost": "One score.", "why": "model fault" }]
        }
        """;

    [Theory]
    [InlineData(0, "completed")]
    [InlineData(1, "failed")]
    [InlineData(2, "skipped")]
    public void A_stored_match_status_survives_the_document_round_trip(int candidate, string status)
    {
        using var round = RoundTrip();

        round.RootElement
            .GetProperty("report").GetProperty("candidates")[candidate]
            .GetProperty("match").GetProperty("status").GetString()
            .Should().Be(status);
    }

    [Theory]
    [InlineData(0, "completed")]
    [InlineData(1, "failed")]
    [InlineData(2, "skipped")]
    public void A_stored_stage_slice_status_survives_the_document_round_trip(int slice, string status)
    {
        using var round = RoundTrip();

        round.RootElement.GetProperty("slices")[slice].GetProperty("status").GetString()
            .Should().Be(status);
    }

    /// <summary>A document whose status strings are not ones this code knows is a stored document
    /// like any other: readers degrade on it, they never throw. <c>TryDeserialize</c> returning
    /// null is that degrade — the drill-in serves the snapshot columns with <c>package: null</c>.</summary>
    [Fact]
    public void A_stored_document_with_an_unknown_status_does_not_throw_at_the_reader()
    {
        var unknown = StoredDocument.Replace("\"status\": \"skipped\"", "\"status\": \"abandoned\"");

        var read = () => StaffingHandoffDocument.TryDeserialize(unknown);

        read.Should().NotThrow("a column nobody can parse is handled, never thrown over");
    }

    private static JsonDocument RoundTrip()
    {
        var document = StaffingHandoffDocument.TryDeserialize(StoredDocument);
        document.Should().NotBeNull("the stored shape is the one this code writes");
        return JsonDocument.Parse(document!.Serialize());
    }
}
