using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExpertToJob.RetrievalEval;

/// <summary>
/// One employee of the frozen eval corpus. This is measurement truth, distinct from any demo data:
/// changing a narrative here changes what every retrieval metric means, so treat edits like moving
/// the goalposts. Keys are stable slugs the golden set references.
/// </summary>
public sealed record EvalEmployee(
    string Key,
    string FirstName,
    string LastName,
    string Title,
    string? Location,
    string Summary,
    IReadOnlyList<EvalExperience> Experiences);

/// <summary>A work experience of a corpus employee. Months are "yyyy-MM"; a null end means current.</summary>
public sealed record EvalExperience(
    string Company,
    string Title,
    string StartMonth,
    string? EndMonth,
    string Summary,
    IReadOnlyList<string> Achievements);

/// <summary>What kind of retrieval ability a golden query probes.</summary>
public enum GoldenQueryCategory
{
    /// <summary>Meaning matches without shared vocabulary — pure semantic retrieval.</summary>
    Paraphrase,

    /// <summary>Acronyms/product names that appear literally in the expected narrative.</summary>
    Keyword,

    /// <summary>Combines two facets (e.g. role + industry) that must both hold.</summary>
    CrossFacet,

    /// <summary>Nothing in the corpus matches — the search must return no one.</summary>
    Negative,
}

/// <summary>One labelled query: the text, its category, and the corpus keys it must retrieve.</summary>
public sealed record GoldenQuery(
    string Query,
    GoldenQueryCategory Category,
    IReadOnlyList<string> Expected);

/// <summary>Loads the committed eval fixtures (JSON files copied next to the test binary).</summary>
public static class EvalFixtures
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public static IReadOnlyList<EvalEmployee> LoadCorpus()
        => Load<EvalEmployee>("eval-corpus.json");

    public static IReadOnlyList<GoldenQuery> LoadGoldenSet()
        => Load<GoldenQuery>("golden-set.json");

    private static IReadOnlyList<T> Load<T>(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<T>>(stream, Options)
               ?? throw new InvalidOperationException($"Fixture '{fileName}' deserialized to null.");
    }
}
