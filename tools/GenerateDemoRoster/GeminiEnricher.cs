using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ExpertToJob.Infrastructure.Persistence.SeedData;

namespace ExpertToJob.Tools.DemoRoster;

/// <summary>
/// Optional online pass: rewrites the fragment-assembled narratives via the Gemini
/// chat-completions endpoint in small batches, so summaries and achievements read like
/// individually written CVs rather than filled templates. Strictly best-effort — any batch
/// that fails, rate-limits, or returns text violating the dataset invariants keeps its
/// offline fragment prose.
/// </summary>
public sealed class GeminiEnricher(string apiKey, string endpoint = "https://generativelanguage.googleapis.com/v1beta/openai", string model = "gemini-flash-lite-latest")
{
    private const int BatchSize = 4;

    /// <summary>Protocol/product tokens that mark acronym-heavy narratives; rewrites must keep them.</summary>
    private static readonly string[] Markers =
    [
        "FIX 4.4", "PCI-DSS", "ISO 20022", "HL7", "FHIR", "DICOM", "Unity ECS", "HLSL",
        "FreeRTOS", "Zephyr", "CAN 2.0B", "ONNX", "OIDC", "SAML", "WCAG 2.2", "gRPC",
    ];

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

    /// <summary>Rewrites narratives in place; returns how many employees were enriched.</summary>
    public async Task<int> EnrichAsync(DemoRosterDataset dataset, Action<string> log)
    {
        var enriched = 0;
        var batches = dataset.Employees.Chunk(BatchSize).ToList();
        for (var b = 0; b < batches.Count; b++)
        {
            try
            {
                var count = await EnrichBatchAsync(batches[b]);
                enriched += count;
                log($"batch {b + 1}/{batches.Count}: enriched {count}/{batches[b].Length}");
            }
            catch (Exception ex)
            {
                log($"batch {b + 1}/{batches.Count}: kept fragment text ({ex.Message})");
            }

            await Task.Delay(TimeSpan.FromSeconds(3)); // stay under free-tier RPM limits
        }

        return enriched;
    }

    private async Task<int> EnrichBatchAsync(IReadOnlyList<DemoRosterEmployee> employees)
    {
        var prompt = BuildPrompt(employees);
        var responseJson = await PostWithRetryAsync(prompt);
        return Apply(responseJson, employees);
    }

    private static string BuildPrompt(IReadOnlyList<DemoRosterEmployee> employees)
    {
        var drafts = employees.Select((e, i) => new
        {
            index = i,
            industry = e.Industry,
            title = e.Title,
            summaryDraft = e.Summary,
            experiences = e.Experiences.Select(x => new
            {
                company = x.Company,
                role = x.Title,
                skills = x.Skills,
                summaryDraft = x.Summary,
                achievementsDraft = x.Achievements,
            }),
        });

        return JsonSerializer.Serialize(drafts);
    }

    private async Task<string> PostWithRetryAsync(string draftsJson)
    {
        var payload = new
        {
            model,
            temperature = 0.9,
            max_tokens = 4000,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content =
                        "You rewrite draft CV narratives for synthetic demo employees. For each employee rewrite " +
                        "the professional summary, and each experience's summary and achievement bullets, in varied, " +
                        "concrete, third-person-implied CV voice (no 'I', no employee names). Rules: keep every " +
                        "company name, role, technology, protocol, product and version token from the drafts (e.g. " +
                        "FIX 4.4, HL7 v2, Unity ECS, WCAG 2.2) and keep the numbers plausible; keep exactly the same " +
                        "number of achievements per experience; experience summaries 110-260 characters; achievements " +
                        "55-200 characters; employee summaries 110-260 characters; vary sentence openings across the " +
                        "batch. Respond with JSON only: {\"employees\":[{\"index\":0,\"summary\":\"...\"," +
                        "\"experiences\":[{\"summary\":\"...\",\"achievements\":[\"...\"]}]}]}",
                },
                new { role = "user", content = draftsJson },
            },
        };

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var choice = doc.RootElement.GetProperty("choices")[0];
                if (choice.GetProperty("finish_reason").GetString() != "stop")
                    throw new InvalidOperationException("response truncated");
                return choice.GetProperty("message").GetProperty("content").GetString()
                       ?? throw new InvalidOperationException("empty completion");
            }

            if (attempt >= 4 || ((int)response.StatusCode != 429 && (int)response.StatusCode < 500))
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}");

            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(15 * attempt);
            await Task.Delay(delay);
        }
    }

    /// <summary>Applies rewrites that pass validation; anything else silently keeps fragment prose.</summary>
    private static int Apply(string responseJson, IReadOnlyList<DemoRosterEmployee> employees)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var applied = 0;

        foreach (var rewritten in doc.RootElement.GetProperty("employees").EnumerateArray())
        {
            var employee = employees[rewritten.GetProperty("index").GetInt32()];
            var experiences = rewritten.GetProperty("experiences");
            if (experiences.GetArrayLength() != employee.Experiences.Count)
                continue;

            var newSummary = rewritten.GetProperty("summary").GetString();
            if (!IsValidText(newSummary, 90, 400))
                continue;

            var valid = true;
            var newExperiences = new List<(string Summary, List<string> Achievements)>();
            for (var i = 0; i < employee.Experiences.Count; i++)
            {
                var original = employee.Experiences[i];
                var summary = experiences[i].GetProperty("summary").GetString();
                var achievements = experiences[i].GetProperty("achievements").EnumerateArray()
                    .Select(a => a.GetString()).ToList();

                valid = IsValidText(summary, 90, 400)
                        && achievements.Count == original.Achievements.Count
                        && achievements.All(a => IsValidText(a, 45, 300))
                        && KeepsMarkers(original, summary!, achievements!);
                if (!valid)
                    break;

                newExperiences.Add((summary!, achievements.Cast<string>().ToList()));
            }

            if (!valid)
                continue;

            employee.Summary = newSummary;
            for (var i = 0; i < employee.Experiences.Count; i++)
            {
                employee.Experiences[i].Summary = newExperiences[i].Summary;
                employee.Experiences[i].Achievements = newExperiences[i].Achievements;
            }
            applied++;
        }

        return applied;
    }

    private static bool IsValidText(string? text, int min, int max) =>
        !string.IsNullOrWhiteSpace(text) && text.Length >= min && text.Length <= max
        && !text.Contains('{') && !text.Contains('}');

    /// <summary>An acronym-heavy experience must stay acronym-heavy after the rewrite.</summary>
    private static bool KeepsMarkers(DemoRosterExperience original, string summary, IReadOnlyList<string> achievements)
    {
        var originalText = original.Summary + " " + string.Join(" ", original.Achievements);
        var newText = summary + " " + string.Join(" ", achievements);
        return Markers.Count(m => originalText.Contains(m)) < 2
               || Markers.Count(m => newText.Contains(m)) >= 2;
    }
}
