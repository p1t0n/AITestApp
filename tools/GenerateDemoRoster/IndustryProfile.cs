namespace CvManager.Tools.DemoRoster;

/// <summary>
/// Structural fragment data for one industry cluster: where its people work, what they are
/// called, and which catalog skills they plausibly hold. Narrative prose lives separately in
/// <see cref="NarrativeFragments"/>.
/// </summary>
/// <param name="RoleLadder">Titles ordered junior → senior; careers walk up this ladder.</param>
/// <param name="SkillPool">Catalog skill names; the first entries are the industry's signature skills.</param>
public sealed record IndustryProfile(
    string Id,
    IReadOnlyList<string> Companies,
    IReadOnlyList<string> RoleLadder,
    IReadOnlyList<string> SkillPool,
    IReadOnlyList<string> Certifications);

public static class IndustryProfiles
{
    public static IReadOnlyList<IndustryProfile> All { get; } =
    [
        new("fintech",
            Companies:
            [
                "LedgerPeak Capital", "Quantivo Markets", "BlueVault Payments", "Centime Clearing",
                "Fondura Bank", "StackRate Lending", "Aurelion Exchange", "PayForge Systems",
            ],
            RoleLadder:
            [
                "Junior Backend Engineer", "Backend Engineer", "Payments Engineer",
                "Senior Backend Engineer", "Staff Engineer, Trading Systems",
            ],
            SkillPool:
            [
                "FIX Protocol", "PCI-DSS", "ISO 20022", "Payment Gateways", "KYC/AML",
                "C#", "Java", "ASP.NET Core", "PostgreSQL", "Apache Kafka", "Redis",
                "Kubernetes", "gRPC", "Microservices",
            ],
            Certifications:
            [
                "AWS Certified Solutions Architect – Associate", "Certified Kubernetes Administrator (CKA)",
                "PCI Professional (PCIP)",
            ]),

        new("gaming",
            Companies:
            [
                "PolyForge Studios", "Nebula Nine Games", "GrimOtter Interactive", "Voxelwind Entertainment",
                "Redcap Arcade", "Skylark Softworks", "Bramblecore Games", "Pixelmarsh Studio",
            ],
            RoleLadder:
            [
                "Junior Gameplay Programmer", "Gameplay Programmer", "Engine Programmer",
                "Senior Game Developer", "Lead Engine Programmer",
            ],
            SkillPool:
            [
                "Unity", "Unreal Engine", "Unity ECS", "Shader Programming (HLSL)", "Multiplayer Netcode",
                "C++", "C#", "Python", "Redis", "AWS", "Docker", "CI/CD",
            ],
            Certifications: ["Unity Certified Professional Programmer", "AWS Certified Developer – Associate"]),

        new("healthtech",
            Companies:
            [
                "Medricor Health", "CarePath Digital", "VitalMesh Systems", "Clinovate Labs",
                "Orchid Medical Software", "PulseBridge Health", "Sanaflow Informatics", "TriageWorks",
            ],
            RoleLadder:
            [
                "Junior Software Engineer", "Integration Engineer", "Backend Engineer, Clinical Systems",
                "Senior Interoperability Engineer", "Principal Engineer, Health Platforms",
            ],
            SkillPool:
            [
                "HL7 v2", "FHIR", "DICOM", "HIPAA Compliance", "OAuth2 / OIDC",
                "C#", "Java", "ASP.NET Core", "PostgreSQL", "REST API Design",
                "Azure", "React", "Computer Vision",
            ],
            Certifications: ["HL7 FHIR R4 Proficiency", "Microsoft Certified: Azure Developer Associate"]),

        new("e-commerce",
            Companies:
            [
                "Cartwheel Commerce", "Snapbasket", "Marketloom", "Parcelbay",
                "Vendora Group", "Checkout Harbor", "Shelfstack", "Bumblecart",
            ],
            RoleLadder:
            [
                "Junior Frontend Developer", "Full-Stack Developer", "Frontend Engineer",
                "Senior Full-Stack Engineer", "Staff Engineer, Storefront Platform",
            ],
            SkillPool:
            [
                "TypeScript", "React", "Next.js", "GraphQL", "Elasticsearch",
                "JavaScript", "MUI", "PostgreSQL", "Redis", "MongoDB",
                "Payment Gateways", "AWS", "Docker", "Accessibility (WCAG)",
            ],
            Certifications: ["AWS Certified Solutions Architect – Associate", "Professional Scrum Master I"]),

        new("embedded",
            Companies:
            [
                "Ferrowatt Devices", "Cinderbolt Robotics", "Axlegrid Automotive", "Nordpine Sensors",
                "Heliotrope Avionics", "Quenchtec Instruments", "Wrenfield Controls", "Ottermill Mechatronics",
            ],
            RoleLadder:
            [
                "Junior Firmware Engineer", "Firmware Engineer", "Embedded Software Engineer",
                "Senior Embedded Engineer", "Principal Firmware Architect",
            ],
            SkillPool:
            [
                "Embedded C", "FreeRTOS", "Zephyr RTOS", "CAN Bus", "MQTT", "ARM Cortex-M",
                "C++", "Rust", "Python", "Docker", "CI/CD",
            ],
            Certifications: ["Certified LabVIEW Embedded Developer", "ISTQB Certified Tester – Foundation"]),

        new("data-ml",
            Companies:
            [
                "Tessellate AI", "Northquill Analytics", "Vectorbloom Labs", "Datamere",
                "Foxglove Intelligence", "Signalcraft ML", "Lanternfish Data", "Gradienta",
            ],
            RoleLadder:
            [
                "Junior Data Engineer", "Data Engineer", "Machine Learning Engineer",
                "Senior ML Engineer", "Staff Engineer, ML Platform",
            ],
            SkillPool:
            [
                "PyTorch", "TensorFlow", "MLflow", "LLM Integration", "RAG Pipelines",
                "Feature Engineering", "Computer Vision", "Python", "Apache Spark", "Apache Airflow",
                "SQL", "PostgreSQL", "Kubernetes", "GCP",
            ],
            Certifications: ["Google Cloud Professional ML Engineer", "Databricks Certified Data Engineer Associate"]),

        new("devops-platform",
            Companies:
            [
                "Cloudmoor Systems", "Pipewright Ops", "Basaltic Infrastructure", "Kitehawk Platform",
                "Turbinefall", "Anchorline DevTools", "Substrate Yard", "Foghorn Cloudworks",
            ],
            RoleLadder:
            [
                "Junior DevOps Engineer", "DevOps Engineer", "Site Reliability Engineer",
                "Senior Platform Engineer", "Staff SRE",
            ],
            SkillPool:
            [
                "Kubernetes", "Terraform", "Docker", "CI/CD", "GitHub Actions",
                "Prometheus", "Grafana", "AWS", "Azure", "GCP", "Go", "Python",
            ],
            Certifications:
            [
                "Certified Kubernetes Administrator (CKA)", "HashiCorp Certified: Terraform Associate",
                "AWS Certified DevOps Engineer – Professional",
            ]),

        new("mobile",
            Companies:
            [
                "Thumbline Apps", "Pocketpine Mobile", "Swipewell Studio", "Appleseed & Fern",
                "Tapforge Digital", "Brightpath Mobile", "Featherdial Labs", "Handglide Software",
            ],
            RoleLadder:
            [
                "Junior Mobile Developer", "Mobile Developer", "iOS Engineer",
                "Senior Mobile Engineer", "Lead Mobile Architect",
            ],
            SkillPool:
            [
                "Swift", "Kotlin", "SwiftUI", "Jetpack Compose", "React Native", "Flutter",
                "TypeScript", "GraphQL", "REST API Design", "CI/CD", "Accessibility (WCAG)",
            ],
            Certifications: ["Google Associate Android Developer", "Professional Scrum Master I"]),

        new("gov-enterprise",
            Companies:
            [
                "Civitas Digital Services", "Meridian Public Systems", "Granite Ledger Consulting",
                "Statecraft Software", "Harborline Enterprise IT", "Oakspire Solutions",
                "Bluepencil Systems", "Registry Works",
            ],
            RoleLadder:
            [
                "Junior Software Engineer", "Software Engineer", "Systems Integration Engineer",
                "Senior Enterprise Developer", "Principal Solutions Architect",
            ],
            SkillPool:
            [
                "Java", "C#", "Angular", "ASP.NET Core", "OAuth2 / OIDC", "Keycloak",
                "OWASP Top 10", "SQL", "PostgreSQL", "Domain-Driven Design", "Azure",
                "Microservices", "Technical Writing",
            ],
            Certifications:
            [
                "CompTIA Security+", "Microsoft Certified: Azure Administrator Associate",
                "TOGAF 9 Certified",
            ]),

        new("agency",
            Companies:
            [
                "Marmalade Digital", "Studio Ampersand", "Hoptoad Creative", "Bricklight Agency",
                "Paperglider", "Wolfnote Interactive", "Cobblestone Web Co", "Lumen & Larch",
            ],
            RoleLadder:
            [
                "Junior Web Developer", "Web Developer", "Full-Stack Developer",
                "Senior Frontend Engineer", "Technical Lead",
            ],
            SkillPool:
            [
                "TypeScript", "JavaScript", "React", "Next.js", "MUI", "Angular",
                "GraphQL", "REST API Design", "MongoDB", "AWS", "Docker",
                "Accessibility (WCAG)", "Flutter", "Agile / Scrum",
            ],
            Certifications: ["Professional Scrum Master I", "AWS Certified Cloud Practitioner"]),
    ];
}
