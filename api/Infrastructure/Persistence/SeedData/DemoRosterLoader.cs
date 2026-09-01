using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExpertToJob.Infrastructure.Persistence.SeedData;

/// <summary>
/// (De)serializes the <c>demo-roster.json</c> asset. Shared by the generation tool
/// (tools/GenerateDemoRoster), the validation tests, and the later seeder slice, so the
/// three can never drift on the wire format.
/// </summary>
public static class DemoRosterLoader
{
    /// <summary>Strict on read (unknown properties throw) so schema drift surfaces in tests, not in the seeder.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static DemoRosterDataset Load(string json) =>
        JsonSerializer.Deserialize<DemoRosterDataset>(json, Options)
            ?? throw new JsonException("demo roster JSON deserialized to null");

    public static DemoRosterDataset Load(Stream stream) =>
        JsonSerializer.Deserialize<DemoRosterDataset>(stream, Options)
            ?? throw new JsonException("demo roster JSON deserialized to null");

    public static string Serialize(DemoRosterDataset dataset) =>
        JsonSerializer.Serialize(dataset, Options);
}
