using System.Linq;
using EmployeeManager.Domain.Entities;
using EmployeeManager.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using Xunit;

namespace EmployeeManager.Mcp.Tests;

public class McpServerSmokeTests
{
    private const string ApiKey = "test-key";

    private static WebApplicationFactory<Program> CreateFactory(string dbName) =>
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

    private static void SeedEmployee(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(),
            FirstName = "Ada",
            LastName = "Lovelace",
            Title = "Engineer",
            Email = "ada@example.com",
        });
        db.SaveChanges();
    }

    private static async Task<McpClient> ConnectAsync(WebApplicationFactory<Program> factory)
    {
        var httpClient = factory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new("Bearer", ApiKey);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = httpClient.BaseAddress!,
                TransportMode = HttpTransportMode.StreamableHttp,
                Name = "smoke-test",
            },
            httpClient);

        return await McpClient.CreateAsync(transport);
    }

    [Fact]
    public async Task Request_without_bearer_token_is_rejected_with_401()
    {
        using var factory = CreateFactory(nameof(Request_without_bearer_token_is_rejected_with_401));
        var http = factory.CreateClient();

        var response = await http.PostAsync("/", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticated_client_can_list_tools_including_employee_list()
    {
        using var factory = CreateFactory(nameof(Authenticated_client_can_list_tools_including_employee_list));
        await using var client = await ConnectAsync(factory);

        var tools = await client.ListToolsAsync();

        tools.Select(t => t.Name).Should().Contain("employee_list");
    }

    [Fact]
    public async Task Calling_employee_list_returns_the_seeded_employee()
    {
        using var factory = CreateFactory(nameof(Calling_employee_list_returns_the_seeded_employee));
        SeedEmployee(factory);
        await using var client = await ConnectAsync(factory);

        var result = await client.CallToolAsync("employee_list");

        result.IsError.Should().NotBe(true);
        var text = (result.StructuredContent?.ToString() ?? "")
            + string.Join("\n", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(c => c.Text));
        text.Should().Contain("Lovelace");
    }
}
