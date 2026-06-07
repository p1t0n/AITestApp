using EmployeeManager.Domain.Entities;
using EmployeeManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EmployeeManager.Mcp.Tests;

/// <summary>
/// Shared in-memory MCP host for integration tests: swaps the database to EF InMemory,
/// configures the bearer key, and connects a real MCP client over the test server.
/// </summary>
internal static class McpTestHost
{
    public const string ApiKey = "test-key";

    public static WebApplicationFactory<Program> CreateFactory(string dbName) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Mcp:ApiKey", ApiKey);
            builder.ConfigureServices(services =>
            {
                var toRemove = services.Where(d =>
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(AppDbContext) ||
                    (d.ServiceType.IsGenericType &&
                     d.ServiceType.GetGenericTypeDefinition().Name == "IDbContextOptionsConfiguration`1"))
                    .ToList();
                foreach (var d in toRemove) services.Remove(d);

                services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
            });
        });

    public static async Task<McpClient> ConnectAsync(WebApplicationFactory<Program> factory)
    {
        var httpClient = factory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new("Bearer", ApiKey);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = httpClient.BaseAddress!,
                TransportMode = HttpTransportMode.StreamableHttp,
                Name = "mcp-tests",
            },
            httpClient);

        return await McpClient.CreateAsync(transport);
    }

    public static Employee SeedEmployee(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            FirstName = "Ada",
            LastName = "Lovelace",
            Title = "Engineer",
            Email = "ada@example.com",
        };
        db.Employees.Add(employee);
        db.SaveChanges();
        return employee;
    }

    public static string Text(CallToolResult result) =>
        (result.StructuredContent?.ToString() ?? "")
        + string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    public static string IdOf(CallToolResult result)
    {
        using var doc = JsonDocument.Parse(Text(result));
        return doc.RootElement.GetProperty("id").GetString()!;
    }
}

