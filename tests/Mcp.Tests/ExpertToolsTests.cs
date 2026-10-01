using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using Xunit;

namespace ExpertToJob.Mcp.Tests;

public class ExpertToolsTests
{
    private static string ResultText(CallToolResult result) =>
        (result.StructuredContent?.ToString() ?? "")
        + string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    private static string IdOf(CallToolResult result)
    {
        using var doc = JsonDocument.Parse(ResultText(result));
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private static Dictionary<string, object?> ValidDto(string lastName = "Lovelace") => new()
    {
        ["firstName"] = "Ada",
        ["lastName"] = lastName,
        ["title"] = "Engineer",
        ["email"] = "ada@example.com",
    };

    [Fact]
    public async Task expert_get_unknown_id_returns_not_found_error()
    {
        using var factory = McpTestHost.CreateFactory(nameof(expert_get_unknown_id_returns_not_found_error));
        await using var client = await McpTestHost.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "expert_get",
            new Dictionary<string, object?> { ["id"] = Guid.NewGuid().ToString() });

        result.IsError.Should().BeTrue();
        ResultText(result).Should().Contain("not_found");
    }

    [Fact]
    public async Task expert_create_with_invalid_input_returns_validation_error()
    {
        using var factory = McpTestHost.CreateFactory(nameof(expert_create_with_invalid_input_returns_validation_error));
        await using var client = await McpTestHost.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "expert_create",
            new Dictionary<string, object?>
            {
                ["dto"] = new Dictionary<string, object?>
                {
                    ["firstName"] = "",
                    ["lastName"] = "X",
                    ["title"] = "T",
                    ["email"] = "not-an-email",
                },
            });

        result.IsError.Should().BeTrue();
        var text = ResultText(result);
        text.Should().Contain("validation");
        text.Should().Contain("Email");
    }

    [Fact]
    public async Task expert_create_get_update_delete_round_trip()
    {
        using var factory = McpTestHost.CreateFactory(nameof(expert_create_get_update_delete_round_trip));
        await using var client = await McpTestHost.ConnectAsync(factory);

        var created = await client.CallToolAsync(
            "expert_create", new Dictionary<string, object?> { ["dto"] = ValidDto() });
        created.IsError.Should().NotBe(true);
        var id = IdOf(created);

        var got = await client.CallToolAsync(
            "expert_get", new Dictionary<string, object?> { ["id"] = id });
        ResultText(got).Should().Contain("Lovelace");

        var updated = await client.CallToolAsync(
            "expert_update",
            new Dictionary<string, object?> { ["id"] = id, ["dto"] = ValidDto("Byron") });
        updated.IsError.Should().NotBe(true);
        ResultText(updated).Should().Contain("Byron");

        var deleted = await client.CallToolAsync(
            "expert_delete", new Dictionary<string, object?> { ["id"] = id });
        deleted.IsError.Should().NotBe(true);

        var gone = await client.CallToolAsync(
            "expert_get", new Dictionary<string, object?> { ["id"] = id });
        gone.IsError.Should().BeTrue();
        ResultText(gone).Should().Contain("not_found");
    }

    [Fact]
    public async Task expert_update_with_only_title_leaves_other_fields_untouched()
    {
        using var factory = McpTestHost.CreateFactory(nameof(expert_update_with_only_title_leaves_other_fields_untouched));
        await using var client = await McpTestHost.ConnectAsync(factory);

        var created = await client.CallToolAsync(
            "expert_create", new Dictionary<string, object?> { ["dto"] = ValidDto() });
        var id = IdOf(created);

        var updated = await client.CallToolAsync(
            "expert_update",
            new Dictionary<string, object?>
            {
                ["id"] = id,
                ["dto"] = new Dictionary<string, object?> { ["title"] = "Staff Engineer" },
            });

        updated.IsError.Should().NotBe(true);
        var text = ResultText(updated);
        text.Should().Contain("Staff Engineer");
        text.Should().Contain("Lovelace");
        text.Should().Contain("ada@example.com");
    }

    /// <summary>Seeds one expert per location through the write tools, so the listing below is
    /// measured against rows the server itself created.</summary>
    private static async Task SeedAsync(ModelContextProtocol.Client.McpClient client,
        params (string LastName, string Location)[] people)
    {
        foreach (var (lastName, location) in people)
        {
            var dto = ValidDto(lastName);
            dto["email"] = $"{lastName.ToLowerInvariant()}@example.com";
            dto["location"] = location;
            (await client.CallToolAsync("expert_create", new Dictionary<string, object?> { ["dto"] = dto }))
                .IsError.Should().NotBe(true);
        }
    }

    /// <summary>
    /// EXP-94: asked "how many experts are based in Warsaw" over a roster that holds some, the
    /// agent used to get all 505 rows, have them refused by the Tool Result Budget, and answer
    /// "none". The filter is what makes the question answerable, and total is what makes the count
    /// answerable without reading a single row.
    /// </summary>
    [Fact]
    public async Task expert_list_filtered_by_location_returns_only_those_rows_and_their_total()
    {
        using var factory = McpTestHost.CreateFactory(nameof(expert_list_filtered_by_location_returns_only_those_rows_and_their_total));
        await using var client = await McpTestHost.ConnectAsync(factory);

        await SeedAsync(client,
            ("Kowalski", "Warsaw, Poland"),
            ("Nowak", "warsaw, poland"),
            ("Schmidt", "Berlin, Germany"),
            ("Nowhere", ""));

        // Lower-case needle against a capitalised location: the match is case-insensitive and a
        // substring, which is how a person (or a model) spells a city.
        var result = await client.CallToolAsync(
            "expert_list", new Dictionary<string, object?> { ["location"] = "warsaw" });

        result.IsError.Should().NotBe(true);
        using var doc = JsonDocument.Parse(ResultText(result));
        doc.RootElement.GetProperty("total").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("lastName").GetString())
            .Should().BeEquivalentTo("Kowalski", "Nowak");
    }

    [Fact]
    public async Task expert_list_without_a_filter_still_returns_the_whole_bench_with_its_total()
    {
        using var factory = McpTestHost.CreateFactory(nameof(expert_list_without_a_filter_still_returns_the_whole_bench_with_its_total));
        await using var client = await McpTestHost.ConnectAsync(factory);

        await SeedAsync(client, ("Kowalski", "Warsaw, Poland"), ("Schmidt", "Berlin, Germany"));

        var result = await client.CallToolAsync("expert_list", new Dictionary<string, object?>());

        result.IsError.Should().NotBe(true);
        using var doc = JsonDocument.Parse(ResultText(result));
        doc.RootElement.GetProperty("total").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    /// <summary>A location nobody is in is zero rows and a zero total — not an error, and not the
    /// whole bench. "No match" and "no filter" must not look the same.</summary>
    [Fact]
    public async Task expert_list_filtered_by_a_location_nobody_is_in_returns_an_empty_match()
    {
        using var factory = McpTestHost.CreateFactory(nameof(expert_list_filtered_by_a_location_nobody_is_in_returns_an_empty_match));
        await using var client = await McpTestHost.ConnectAsync(factory);

        await SeedAsync(client, ("Kowalski", "Warsaw, Poland"));

        var result = await client.CallToolAsync(
            "expert_list", new Dictionary<string, object?> { ["location"] = "Reykjavik" });

        result.IsError.Should().NotBe(true);
        using var doc = JsonDocument.Parse(ResultText(result));
        doc.RootElement.GetProperty("total").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    /// <summary>Drafts stay out of expert_list, which its description promises — so a status
    /// filter naming them narrows to nobody rather than opening the gate.</summary>
    [Fact]
    public async Task expert_list_filtered_by_status_matches_the_status_name_case_insensitively()
    {
        using var factory = McpTestHost.CreateFactory(nameof(expert_list_filtered_by_status_matches_the_status_name_case_insensitively));
        await using var client = await McpTestHost.ConnectAsync(factory);

        await SeedAsync(client, ("Kowalski", "Warsaw, Poland"));
        var draft = ValidDto("Draftsman");
        draft["email"] = "";
        draft["location"] = "Warsaw, Poland";
        (await client.CallToolAsync("expert_create_draft", new Dictionary<string, object?> { ["dto"] = draft }))
            .IsError.Should().NotBe(true);

        var active = await client.CallToolAsync(
            "expert_list", new Dictionary<string, object?> { ["status"] = "active" });
        var drafts = await client.CallToolAsync(
            "expert_list", new Dictionary<string, object?> { ["status"] = "Draft" });

        using var activeDoc = JsonDocument.Parse(ResultText(active));
        activeDoc.RootElement.GetProperty("total").GetInt32().Should().Be(1);
        using var draftDoc = JsonDocument.Parse(ResultText(drafts));
        draftDoc.RootElement.GetProperty("total").GetInt32().Should().Be(0);
    }
}
