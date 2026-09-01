using ExpertToJob.Application.Search;
using ExpertToJob.Domain.Enums;
using FluentAssertions;

namespace ExpertToJob.Application.Tests;

public class ReconcilerTests
{
    private static readonly Guid Emp = Guid.NewGuid();

    [Fact]
    public void New_source_with_no_existing_chunk_is_an_insert()
    {
        var desired = new[] { Desired(SearchChunkSource.Experience, "hash-1") };

        var diff = Reconciler.Diff(desired, []);

        diff.Deletes.Should().BeEmpty();
        diff.Upserts.Should().ContainSingle();
        diff.Upserts[0].ExistingId.Should().BeNull();
    }

    [Fact]
    public void Unchanged_content_produces_no_work()
    {
        var chunk = Desired(SearchChunkSource.Experience, "hash-1");
        var existing = Existing(chunk.SourceType, chunk.SourceId, "hash-1");

        var diff = Reconciler.Diff([chunk], [existing]);

        diff.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Changed_content_is_an_update_in_place()
    {
        var chunk = Desired(SearchChunkSource.Experience, "hash-2");
        var existing = Existing(chunk.SourceType, chunk.SourceId, "hash-1");

        var diff = Reconciler.Diff([chunk], [existing]);

        diff.Deletes.Should().BeEmpty();
        diff.Upserts.Should().ContainSingle();
        diff.Upserts[0].ExistingId.Should().Be(existing.Id);
    }

    [Fact]
    public void Existing_chunk_with_no_desired_source_is_deleted()
    {
        var orphan = Existing(SearchChunkSource.Experience, Guid.NewGuid(), "hash-1");

        var diff = Reconciler.Diff([], [orphan]);

        diff.Upserts.Should().BeEmpty();
        diff.Deletes.Should().ContainSingle().Which.Should().Be(orphan.Id);
    }

    [Fact]
    public void Same_source_id_across_different_source_types_does_not_collide()
    {
        // An expert's summary chunk is keyed by the expert id; an experience could (in theory)
        // share an id space — the (type, id) key must keep them distinct.
        var shared = Guid.NewGuid();
        var summary = new DesiredChunk(Emp, SearchChunkSource.Summary, shared, "s", "hash-s");
        var experience = new DesiredChunk(Emp, SearchChunkSource.Experience, shared, "e", "hash-e");

        var diff = Reconciler.Diff([summary, experience], []);

        diff.Upserts.Should().HaveCount(2);
    }

    [Fact]
    public void Mixed_batch_inserts_updates_and_deletes_together()
    {
        var keep = Desired(SearchChunkSource.Experience, "same");      // unchanged
        var edit = Desired(SearchChunkSource.Experience, "new-hash");  // changed
        var orphan = Existing(SearchChunkSource.Experience, Guid.NewGuid(), "old");

        var existing = new[]
        {
            Existing(keep.SourceType, keep.SourceId, "same"),
            Existing(edit.SourceType, edit.SourceId, "old-hash"),
            orphan,
        };

        var diff = Reconciler.Diff([keep, edit], existing);

        diff.Upserts.Should().ContainSingle(u => u.Chunk.SourceId == edit.SourceId && u.ExistingId != null);
        diff.Deletes.Should().ContainSingle().Which.Should().Be(orphan.Id);
    }

    private static DesiredChunk Desired(SearchChunkSource type, string hash)
        => new(Emp, type, Guid.NewGuid(), "content", hash);

    private static ExistingChunk Existing(SearchChunkSource type, Guid sourceId, string hash)
        => new(Guid.NewGuid(), type, sourceId, hash);
}
