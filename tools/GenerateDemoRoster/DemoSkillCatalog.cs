using EmployeeManager.Infrastructure.Persistence.SeedData;

namespace EmployeeManager.Tools.DemoRoster;

/// <summary>
/// The dataset's own skill catalog (~80 skills across the ten industry clusters). Carried
/// inside demo-roster.json so the seeder slice can upsert skills the base
/// <c>DbInitializer</c> catalog (~16 skills) is missing. Names of skills that also exist in
/// the base catalog match it exactly so the upsert can match by name.
/// </summary>
public static class DemoSkillCatalog
{
    public static IReadOnlyList<DemoRosterSkill> All { get; } = Build();

    private static List<DemoRosterSkill> Build()
    {
        var skills = new List<DemoRosterSkill>();
        void Add(string category, params string[] names) =>
            skills.AddRange(names.Select(n => new DemoRosterSkill { Name = n, Category = category }));

        Add("Languages",
            "JavaScript", "TypeScript", "C#", "Python", "Go", "Rust", "C++", "Kotlin", "Swift", "Java");
        Add("Frontend",
            "React", "MUI", "Angular", "Next.js", "Accessibility (WCAG)");
        Add("Backend / .NET",
            "ASP.NET Core", "Entity Framework Core", "gRPC", "GraphQL", "REST API Design",
            "Microservices", "RabbitMQ", "Apache Kafka");
        Add("Data",
            "PostgreSQL", "SQL", "Redis", "MongoDB", "Elasticsearch", "Apache Spark", "Apache Airflow");
        Add("Data / ML",
            "PyTorch", "TensorFlow", "MLflow", "LLM Integration", "RAG Pipelines",
            "Feature Engineering", "Computer Vision");
        Add("Cloud & DevOps",
            "Docker", "Kubernetes", "AWS", "Azure", "GCP", "Terraform", "CI/CD",
            "GitHub Actions", "Prometheus", "Grafana");
        Add("Mobile",
            "SwiftUI", "Jetpack Compose", "React Native", "Flutter");
        Add("Gaming",
            "Unity", "Unreal Engine", "Unity ECS", "Shader Programming (HLSL)", "Multiplayer Netcode");
        Add("Embedded",
            "Embedded C", "FreeRTOS", "Zephyr RTOS", "CAN Bus", "MQTT", "ARM Cortex-M");
        Add("Fintech / Trading",
            "FIX Protocol", "PCI-DSS", "ISO 20022", "Payment Gateways", "KYC/AML");
        Add("Healthtech",
            "HL7 v2", "FHIR", "DICOM", "HIPAA Compliance");
        Add("Security & Identity",
            "OAuth2 / OIDC", "Keycloak", "OWASP Top 10");
        Add("Practices",
            "Agile / Scrum", "Test-Driven Development", "Domain-Driven Design",
            "Team Leadership", "Technical Writing");

        return skills;
    }
}
