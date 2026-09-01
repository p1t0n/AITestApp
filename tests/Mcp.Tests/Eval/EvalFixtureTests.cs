using FluentAssertions;

using ExpertToJob.RetrievalEval;

namespace ExpertToJob.Mcp.Tests.Eval;

/// <summary>
/// Structural validation of the committed eval fixtures (no Docker, no embeddings). Guards the
/// contract the live eval relies on: every golden label points at a real corpus employee, the
/// category mix is honoured, and the frozen corpus stays within its designed size.
/// </summary>
public class EvalFixtureTests
{
    private static readonly IReadOnlyList<EvalEmployee> Corpus = EvalFixtures.LoadCorpus();
    private static readonly IReadOnlyList<GoldenQuery> GoldenSet = EvalFixtures.LoadGoldenSet();

    [Fact]
    public void Corpus_has_the_designed_size_and_unique_keys()
    {
        Corpus.Count.Should().BeInRange(20, 30);
        Corpus.Select(e => e.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Corpus_employees_all_have_narrative_text_to_embed()
    {
        foreach (var employee in Corpus)
        {
            employee.Key.Should().NotBeNullOrWhiteSpace();
            employee.FirstName.Should().NotBeNullOrWhiteSpace();
            employee.LastName.Should().NotBeNullOrWhiteSpace();
            employee.Title.Should().NotBeNullOrWhiteSpace();
            employee.Summary.Should().NotBeNullOrWhiteSpace(
                $"employee '{employee.Key}' needs a summary chunk");
            employee.Experiences.Should().NotBeEmpty(
                $"employee '{employee.Key}' needs at least one experience chunk");
            employee.Experiences.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.Summary),
                $"every experience of '{employee.Key}' must carry narrative text");
        }
    }

    [Fact]
    public void Golden_set_has_the_designed_size()
        => GoldenSet.Count.Should().BeInRange(30, 50);

    [Fact]
    public void Golden_set_has_no_duplicate_queries()
        => GoldenSet.Select(q => q.Query.Trim().ToLowerInvariant())
            .Should().OnlyHaveUniqueItems();

    [Fact]
    public void Golden_set_expected_keys_all_exist_in_the_corpus()
    {
        var corpusKeys = Corpus.Select(e => e.Key).ToHashSet();

        foreach (var query in GoldenSet)
        {
            query.Expected.Should().BeSubsetOf(corpusKeys,
                $"query '{query.Query}' must only expect employees that exist in the corpus");
        }
    }

    [Fact]
    public void Golden_set_meets_the_minimum_category_mix()
    {
        GoldenSet.Count(q => q.Category == GoldenQueryCategory.Negative)
            .Should().BeGreaterThanOrEqualTo(5);
        GoldenSet.Count(q => q.Category == GoldenQueryCategory.Keyword)
            .Should().BeGreaterThanOrEqualTo(5);
        GoldenSet.Should().Contain(q => q.Category == GoldenQueryCategory.Paraphrase);
        GoldenSet.Should().Contain(q => q.Category == GoldenQueryCategory.CrossFacet);
    }

    [Fact]
    public void Negative_queries_expect_nothing_and_positive_queries_expect_someone()
    {
        foreach (var query in GoldenSet)
        {
            if (query.Category == GoldenQueryCategory.Negative)
            {
                query.Expected.Should().BeEmpty($"negative query '{query.Query}' must expect no one");
            }
            else
            {
                query.Expected.Should().NotBeEmpty($"positive query '{query.Query}' must expect someone");
            }
        }
    }

    [Fact]
    public void Keyword_queries_literally_appear_in_every_expected_employees_narrative()
    {
        // A keyword query is only defensible if the acronym/product name is actually in the text
        // the expected employee gets embedded with.
        var byKey = Corpus.ToDictionary(e => e.Key, NarrativeOf);

        foreach (var query in GoldenSet.Where(q => q.Category == GoldenQueryCategory.Keyword))
        {
            foreach (var key in query.Expected)
            {
                byKey[key].Should().ContainEquivalentOf(query.Query.Split(' ')[0],
                    $"keyword query '{query.Query}' expects '{key}' to mention the term");
            }
        }
    }

    private static string NarrativeOf(EvalEmployee employee)
        => string.Join('\n',
            new[] { employee.Summary }
                .Concat(employee.Experiences.Select(x =>
                    $"{x.Title} @ {x.Company}\n{x.Summary}\n{string.Join('\n', x.Achievements)}")));
}
