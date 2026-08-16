using CvManager.Agents.Agents;
using CvManager.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace CvManager.Agents.Tests.Eval;

/// <summary>One JD with the capability concepts a faithful reading must surface. A concept is
/// covered when any produced requirement mentions one of its alternative keywords.</summary>
public sealed record JdFixture(string Id, string JobDescription, string[][] ExpectedConcepts);

/// <summary>
/// Live requirement-extraction eval (P1T-97): distilling a JD into 3-8 requirement phrases feeds
/// the retrieval — garbage here poisons the whole staffing pipeline. Since P1T-117 the
/// distillation lives in <see cref="JdRequirementExtractor"/> (the single source for every
/// consumer), so the eval runs THAT against the real model. Floors in
/// <see cref="AgentEvalBaselines"/>.
/// </summary>
[Trait("Category", "eval")]
public class RequirementExtractionEvalTests(ITestOutputHelper output)
{
    private static readonly IReadOnlyList<JdFixture> Fixtures =
    [
        new("react-senior",
            "Senior Frontend Engineer. 5+ years building production React/TypeScript apps. Strong on component architecture, state management, performance, and accessibility. Some team leadership expected.",
            [["react"], ["typescript"], ["accessibility", "a11y"], ["lead", "leader", "mentor"]]),
        new("dotnet-backend",
            "Senior Backend Engineer. Deep C#/.NET and ASP.NET Core. PostgreSQL and EF Core, REST API design, distributed systems, and cloud deployment. Experience mentoring and owning services end to end.",
            [["c#", ".net", "asp.net"], ["postgresql", "postgres"], ["rest", "api"], ["distributed", "cloud"], ["mentor", "own"]]),
        new("platform-devops",
            "Platform Engineer. Kubernetes, Docker, CI/CD pipelines, infrastructure-as-code (Terraform), observability, and on-call ownership.",
            [["kubernetes", "k8s"], ["docker", "container"], ["ci/cd", "pipeline"], ["terraform", "infrastructure as code", "infrastructure-as-code", "iac"], ["observability", "monitoring", "on-call"]]),
        new("data-engineer",
            "Data Engineer to build and operate ELT pipelines with Apache Airflow and Spark on AWS. Strong SQL and Python; warehouse cost optimization a plus.",
            [["airflow"], ["spark"], ["aws"], ["sql"], ["python"]]),
        new("ml-engineer",
            "Machine Learning Engineer for LLM-powered products: retrieval-augmented generation, vector search, prompt engineering, and model evaluation. Python and PyTorch required.",
            [["rag", "retrieval"], ["vector", "embedding"], ["prompt"], ["python"], ["pytorch", "evaluation"]]),
        new("mobile-lead",
            "Lead Mobile Developer for our iOS and Android apps: Swift/SwiftUI and Kotlin/Jetpack Compose, app store releases, and mentoring a team of four.",
            [["swift", "swiftui", "ios"], ["kotlin", "compose", "android"], ["release", "app store"], ["mentor", "lead"]]),
        new("qa-automation",
            "QA Automation Engineer: end-to-end test frameworks (Playwright or Cypress), API testing, CI integration, and a habit of hunting flaky tests.",
            [["playwright", "cypress", "end-to-end", "e2e"], ["api test"], ["ci", "continuous integration"], ["flaky", "reliability"]]),
        new("security-engineer",
            "Application Security Engineer: threat modeling, secure code review, OAuth2/OIDC, secrets management, and driving remediation with engineering teams.",
            [["threat model"], ["code review", "secure code"], ["oauth", "oidc"], ["secret"], ["remediation"]]),
        new("fullstack-startup",
            "Full-stack engineer at an early-stage startup: React frontend, Node.js or .NET backend, PostgreSQL, shipping fast with tests, comfortable talking to users.",
            [["react"], ["node", ".net", "backend"], ["postgresql", "postgres"], ["test"], ["user", "customer"]]),
        new("embedded-firmware",
            "Embedded Firmware Engineer: C/C++ on FreeRTOS or Zephyr, CAN bus communication, low-power optimization, and hardware bring-up with electrical engineers.",
            [["c++", "c/c++", " c "], ["freertos", "zephyr", "rtos"], ["can"], ["low-power", "power"], ["bring-up", "hardware"]]),
    ];

    [SkippableFact]
    public async Task Requirement_extraction_does_not_regress_below_the_committed_baseline()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
            "Live requirement eval needs a Gemini API key in GEMINI_API_KEY.");

        var chatClient = LiveGemini.CreateChatClient();
        var coverages = new List<double>();
        var precisions = new List<double>();
        var withinBand = 0;

        var extractor = new JdRequirementExtractor(chatClient);
        foreach (var jd in Fixtures)
        {
            var outcome = await extractor.ExtractAsync(jd.JobDescription);
            var requirements = (outcome.Requirements?.Requirements ?? [])
                .Select(r => r.Text.ToLowerInvariant())
                .ToList();

            var covered = jd.ExpectedConcepts.Count(concept =>
                concept.Any(keyword => requirements.Any(r => r.Contains(keyword))));
            var coverage = (double)covered / jd.ExpectedConcepts.Length;
            var precise = requirements.Count(r =>
                jd.ExpectedConcepts.Any(concept => concept.Any(keyword => r.Contains(keyword)))
                || jd.JobDescription.ToLowerInvariant().Contains(r.Split(' ')[^1]));
            var precision = requirements.Count == 0 ? 0 : (double)precise / requirements.Count;
            var inBand = requirements.Count >= AgentEvalBaselines.RequirementCountMin
                         && requirements.Count <= AgentEvalBaselines.RequirementCountMax;
            if (inBand) withinBand++;

            coverages.Add(coverage);
            precisions.Add(precision);
            output.WriteLine(
                $"{jd.Id,-18} n={requirements.Count} coverage={coverage:P0} precision={precision:P0} " +
                $"[{string.Join(" · ", requirements)}]");

            await Task.Delay(TimeSpan.FromSeconds(6)); // free-tier RPM headroom
        }

        output.WriteLine("");
        output.WriteLine($"concept coverage = {coverages.Average():F4}");
        output.WriteLine($"phrase precision = {precisions.Average():F4}");
        output.WriteLine($"within 3-8 band  = {withinBand}/{Fixtures.Count}");

        using (new FluentAssertions.Execution.AssertionScope())
        {
            coverages.Average().Should().BeGreaterThanOrEqualTo(AgentEvalBaselines.RequirementCoverageFloor);
            precisions.Average().Should().BeGreaterThanOrEqualTo(AgentEvalBaselines.RequirementPrecisionFloor);
            withinBand.Should().Be(Fixtures.Count, "every run must respect the 3-8 requirement band");
        }
    }
}
