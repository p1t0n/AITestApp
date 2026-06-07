using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using Xunit;

namespace EmployeeManager.Mcp.Tests;

public class EmployeeToolsTests
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
    public async Task employee_get_unknown_id_returns_not_found_error()
    {
        using var factory = McpTestHost.CreateFactory(nameof(employee_get_unknown_id_returns_not_found_error));
        await using var client = await McpTestHost.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "employee_get",
            new Dictionary<string, object?> { ["id"] = Guid.NewGuid().ToString() });

        result.IsError.Should().BeTrue();
        ResultText(result).Should().Contain("not_found");
    }

    [Fact]
    public async Task employee_create_with_invalid_input_returns_validation_error()
    {
        using var factory = McpTestHost.CreateFactory(nameof(employee_create_with_invalid_input_returns_validation_error));
        await using var client = await McpTestHost.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "employee_create",
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
    public async Task employee_create_get_update_delete_round_trip()
    {
        using var factory = McpTestHost.CreateFactory(nameof(employee_create_get_update_delete_round_trip));
        await using var client = await McpTestHost.ConnectAsync(factory);

        var created = await client.CallToolAsync(
            "employee_create", new Dictionary<string, object?> { ["dto"] = ValidDto() });
        created.IsError.Should().NotBe(true);
        var id = IdOf(created);

        var got = await client.CallToolAsync(
            "employee_get", new Dictionary<string, object?> { ["id"] = id });
        ResultText(got).Should().Contain("Lovelace");

        var updated = await client.CallToolAsync(
            "employee_update",
            new Dictionary<string, object?> { ["id"] = id, ["dto"] = ValidDto("Byron") });
        updated.IsError.Should().NotBe(true);
        ResultText(updated).Should().Contain("Byron");

        var deleted = await client.CallToolAsync(
            "employee_delete", new Dictionary<string, object?> { ["id"] = id });
        deleted.IsError.Should().NotBe(true);

        var gone = await client.CallToolAsync(
            "employee_get", new Dictionary<string, object?> { ["id"] = id });
        gone.IsError.Should().BeTrue();
        ResultText(gone).Should().Contain("not_found");
    }
}
