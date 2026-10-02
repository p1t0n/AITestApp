using System.Reflection;
using ExpertToJob.Infrastructure.Search;
using FluentAssertions;
using FluentAssertions.Execution;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// The search and snippet sizes (EXP-106). Every one of them used to be a bindable property on
/// <see cref="SemanticSearchOptions"/>, and nothing — no settings file, no AppHost parameter, no
/// host — ever set one. A knob nobody turns is not a knob: it is a second place the number can be,
/// so a reader cannot tell which copy is load-bearing and a deployment can move a guardrail the
/// tests pin without a single test going red.
///
/// <para>So they are constants now, and these are the numbers, spelled as literals. Asserting them
/// against the constants they came from would pass on any value; the point of this file is that
/// changing one takes a deliberate edit here too.</para>
/// </summary>
public class SearchSizeConstantTests
{
    /// <summary>How many experts a search returns by default, and the ceiling a caller cannot ask
    /// past. The shortlist path has its own pair because it feeds a different consumer.</summary>
    [Fact]
    public void The_result_sizes_are_the_shipped_numbers()
    {
        using var _ = new AssertionScope();
        SemanticSearchOptions.DefaultTopK.Should().Be(5);
        SemanticSearchOptions.MaxTopK.Should().Be(20);
        SemanticSearchOptions.ShortlistDefaultTopK.Should().Be(10);
        SemanticSearchOptions.ShortlistMaxTopK.Should().Be(20);
    }

    /// <summary>Snippet budget: both halves of what a tool result is allowed to carry per expert.
    /// The <c>CostFloors</c> suites measure the payloads these two produce.</summary>
    [Fact]
    public void The_snippet_sizes_are_the_shipped_numbers()
    {
        using var _ = new AssertionScope();
        SemanticSearchOptions.MaxSnippetsPerExpert.Should().Be(3);
        SemanticSearchOptions.SnippetMaxChars.Should().Be(500);
    }

    /// <summary>The style-exemplar window: how many per bullet, and the length band outside which a
    /// bullet carries no imitable style.</summary>
    [Fact]
    public void The_exemplar_sizes_are_the_shipped_numbers()
    {
        using var _ = new AssertionScope();
        SemanticSearchOptions.ExemplarsPerBullet.Should().Be(2);
        SemanticSearchOptions.ExemplarsPerBulletMax.Should().Be(5);
        SemanticSearchOptions.ExemplarMinChars.Should().Be(40);
        SemanticSearchOptions.ExemplarMaxChars.Should().Be(300);
    }

    /// <summary>
    /// The similarity floor is the one value left on the instance, and it is not an operator knob
    /// either: <c>AddSearchIndexing</c> writes it from the active embedding provider's own block
    /// (EXP-64). A property re-added here is a size that can drift again, which is what this
    /// catches — the compiler cannot, because binding answers a key it does not know with silence.
    /// </summary>
    [Fact]
    public void MinSimilarity_is_the_only_property_left()
        => typeof(SemanticSearchOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Should().Equal(nameof(SemanticSearchOptions.MinSimilarity));
}
