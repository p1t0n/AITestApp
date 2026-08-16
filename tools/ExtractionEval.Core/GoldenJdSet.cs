using CvManager.Agents.Agents;

namespace CvManager.ExtractionEval;

/// <summary>
/// One hand-labeled JD in the frozen golden set (P1T-119). Labels encode what a faithful,
/// honest extraction must and must not do:
/// <list type="bullet">
/// <item><see cref="ExpectedConcepts"/> — concept groups (alternative keywords) a faithful
/// reading surfaces; drives requirement recall.</item>
/// <item><see cref="MustHaveConcepts"/> — the only concepts the JD actually marks as
/// required/essential. A produced MustHave matching none of them is a fabrication.</item>
/// <item><see cref="StatedSeniority"/>/<see cref="StatedLocation"/>/<see cref="YearsStated"/> —
/// the honesty slots: when the JD is silent (null/false), a non-Unspecified/non-null value is a
/// fabrication, never an accuracy miss.</item>
/// </list>
/// </summary>
public sealed record GoldenJd(
    string Id,
    string JobDescription,
    string[][] ExpectedConcepts,
    string[][] MustHaveConcepts,
    JdSeniority? StatedSeniority,
    string? StatedLocation,
    bool YearsStated);

/// <summary>The frozen golden set: rich JDs across domains, deliberately sparse JDs, and
/// tricky/ambiguous ones (the honesty cases). Frozen — only move on a deliberate re-label.</summary>
public static class GoldenJdSet
{
    public static IReadOnlyList<GoldenJd> Load() =>
    [
        // ---- Rich JDs ------------------------------------------------------------------------
        new("react-senior",
            "Senior Frontend Engineer. 5+ years building production React/TypeScript apps required. " +
            "Strong on component architecture, state management, performance, and accessibility. " +
            "Some team leadership expected. Based in Berlin.",
            [["react"], ["typescript"], ["accessibility", "a11y"], ["lead", "leader", "mentor"], ["performance", "state management", "component"]],
            [["react"], ["typescript"]],
            JdSeniority.Senior, "Berlin", YearsStated: true),
        new("dotnet-backend",
            "Senior Backend Engineer. Deep C#/.NET and ASP.NET Core essential. PostgreSQL and EF Core, " +
            "REST API design, distributed systems, and cloud deployment. Experience mentoring and owning services end to end.",
            [["c#", ".net", "asp.net"], ["postgresql", "postgres"], ["rest", "api"], ["distributed", "cloud"], ["mentor", "own"]],
            [["c#", ".net", "asp.net"]],
            JdSeniority.Senior, null, YearsStated: false),
        new("platform-devops",
            "Platform Engineer. Kubernetes and Docker required; CI/CD pipelines, infrastructure-as-code " +
            "(Terraform), observability, and on-call ownership.",
            [["kubernetes", "k8s"], ["docker", "container"], ["ci/cd", "pipeline"], ["terraform", "infrastructure"], ["observability", "monitoring", "on-call"]],
            [["kubernetes", "k8s"], ["docker", "container"]],
            null, null, YearsStated: false),
        new("data-engineer",
            "Data Engineer to build and operate ELT pipelines with Apache Airflow and Spark on AWS. " +
            "Strong SQL and Python required; warehouse cost optimization a plus. Remote within the EU.",
            [["airflow"], ["spark"], ["aws"], ["sql"], ["python"], ["warehouse", "cost"]],
            [["sql"], ["python"], ["airflow"], ["spark"], ["aws"]],
            null, "EU", YearsStated: false),
        new("ml-engineer",
            "Machine Learning Engineer for LLM-powered products: retrieval-augmented generation, vector " +
            "search, prompt engineering, and model evaluation. Python and PyTorch required. 3+ years of ML experience.",
            [["rag", "retrieval"], ["vector", "embedding"], ["prompt"], ["python"], ["pytorch", "evaluation"], ["ml", "machine learning"]],
            [["python"], ["pytorch"], ["ml", "machine learning"]],
            null, null, YearsStated: true),
        new("mobile-lead",
            "Lead Mobile Developer for our iOS and Android apps: Swift/SwiftUI and Kotlin/Jetpack Compose, " +
            "app store releases, and mentoring a team of four.",
            [["swift", "swiftui", "ios"], ["kotlin", "compose", "android"], ["release", "app store"], ["mentor", "lead"]],
            [],
            JdSeniority.Lead, null, YearsStated: false),
        new("qa-automation",
            "QA Automation Engineer: end-to-end test frameworks (Playwright or Cypress) required, API " +
            "testing, CI integration, and a habit of hunting flaky tests.",
            [["playwright", "cypress", "end-to-end", "e2e"], ["api test", "api"], ["ci", "continuous integration"], ["flaky", "reliability"]],
            [["playwright", "cypress", "end-to-end", "e2e"]],
            null, null, YearsStated: false),
        new("security-engineer",
            "Application Security Engineer: threat modeling, secure code review, OAuth2/OIDC, secrets " +
            "management, and driving remediation with engineering teams. Principal level, Amsterdam office.",
            [["threat model"], ["code review", "secure code"], ["oauth", "oidc"], ["secret"], ["remediation"]],
            [],
            JdSeniority.Principal, "Amsterdam", YearsStated: false),
        new("fullstack-startup",
            "Full-stack engineer at an early-stage startup: React frontend, Node.js or .NET backend, " +
            "PostgreSQL, shipping fast with tests, comfortable talking to users.",
            [["react"], ["node", ".net", "backend"], ["postgresql", "postgres"], ["test"], ["user", "customer"]],
            [],
            null, null, YearsStated: false),
        new("embedded-firmware",
            "Embedded Firmware Engineer: C/C++ on FreeRTOS or Zephyr required, CAN bus communication, " +
            "low-power optimization, and hardware bring-up with electrical engineers. 7+ years embedded experience.",
            [["c++", "c/c++", " c "], ["freertos", "zephyr", "rtos"], ["can"], ["low-power", "power"], ["bring-up", "hardware"]],
            [["c++", "c/c++", " c ", "freertos", "zephyr", "rtos"]],
            null, null, YearsStated: true),
        new("sre",
            "Site Reliability Engineer: Linux internals, incident response, SLOs and error budgets, " +
            "Prometheus/Grafana, and automation in Go or Python. Mid-level role.",
            [["linux"], ["incident"], ["slo", "error budget"], ["prometheus", "grafana", "monitoring"], ["go", "python", "automation"]],
            [],
            JdSeniority.Mid, null, YearsStated: false),
        new("solutions-architect",
            "Solutions Architect for enterprise integrations: event-driven architecture, message brokers " +
            "(Kafka or RabbitMQ), API gateways, and customer-facing workshops. 10+ years in software required. Zurich or remote.",
            [["event-driven", "event"], ["kafka", "rabbitmq", "broker"], ["gateway", "api"], ["workshop", "customer"]],
            [["10", "years", "software"]],
            null, "Zurich", YearsStated: true),

        // ---- Sparse JDs (the honesty cases: almost everything must stay unspecified/null) -----
        // Recall labels are empty on purpose: an honest reading of a bare JD may produce zero
        // requirements — the sparse cases exist to test the honesty slots, not coverage.
        new("sparse-engineer",
            "Engineer wanted.",
            [],
            [],
            null, null, YearsStated: false),
        new("sparse-developer",
            "We are hiring a software developer to join our team.",
            [],
            [],
            null, null, YearsStated: false),
        new("sparse-data",
            "Looking for someone to help us with data.",
            [],
            [],
            null, null, YearsStated: false),
        new("sparse-webshop",
            "Our webshop needs technical help. Friendly team.",
            [],
            [],
            null, null, YearsStated: false),

        // ---- Tricky / ambiguous JDs ------------------------------------------------------------
        new("tricky-nice-to-have",
            "Backend developer. Java required. Kubernetes, AWS, and Terraform are all nice to have " +
            "but absolutely not required. We value curiosity over checklists.",
            [["java"], ["kubernetes"], ["aws"], ["terraform"]],
            [["java"]],
            null, null, YearsStated: false),
        new("tricky-years-on-one-skill",
            "Frontend developer: 4+ years of Vue.js specifically. Familiarity with Nuxt is a bonus.",
            [["vue"], ["nuxt"]],
            [["vue"]],
            null, null, YearsStated: true),
        new("tricky-contradictory-seniority",
            "Junior-to-senior engineer — we honestly have not decided the level yet, it depends on the " +
            "candidate. Solid Python either way.",
            [["python"]],
            [["python"]],
            null, null, YearsStated: false),
        new("tricky-location-hint",
            "Ruby on Rails developer. Our office has great coffee. Occasional travel to client sites.",
            [["ruby", "rails"], ["travel", "client"]],
            [],
            null, null, YearsStated: false),
        new("tricky-soft-only",
            "We need a great communicator who can bridge business and engineering. Some technical " +
            "background preferred.",
            [["communicat", "bridge", "business"], ["technical", "engineering"]],
            [["communicat", "bridge"]],
            null, null, YearsStated: false),
    ];
}
