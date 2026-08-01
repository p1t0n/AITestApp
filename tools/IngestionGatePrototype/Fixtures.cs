// PROTOTYPE (P1T-81) — throwaway. Eight synthetic resumes with hand-written ground truth.
// Names follow the demo-roster fantasy convention so no real person is described.
namespace CvManager.Tools.IngestionGate;

public sealed record TruthSkill(string Name, bool InCatalog);

public sealed record TruthExperience(string Company, string Title, string? StartDate, string? EndDate, int AchievementCount);

public sealed record GroundTruth(
    string FirstName,
    string LastName,
    string? Email,
    string Title,
    IReadOnlyList<TruthSkill> Skills,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Qualifications,
    IReadOnlyList<TruthExperience> Experiences);

public sealed record ResumeFixture(string Id, string Style, string Text, GroundTruth Truth);

public static class Fixtures
{
    /// <summary>Catalog subset the matcher uses — mirrors DemoSkillCatalog + base seed names.</summary>
    public static readonly HashSet<string> Catalog = new(StringComparer.OrdinalIgnoreCase)
    {
        "JavaScript", "TypeScript", "C#", "Python", "Go", "Rust", "C++", "Kotlin", "Swift", "Java",
        "React", "MUI", "Angular", "Next.js", "Accessibility (WCAG)",
        "ASP.NET Core", "Entity Framework Core", "gRPC", "GraphQL", "REST API Design",
        "Microservices", "RabbitMQ", "Apache Kafka",
        "PostgreSQL", "SQL", "Redis", "MongoDB", "Elasticsearch", "Apache Spark", "Apache Airflow",
        "PyTorch", "TensorFlow", "MLflow", "LLM Integration", "RAG Pipelines",
        "Feature Engineering", "Computer Vision",
        "Docker", "Kubernetes", "AWS", "Azure", "GCP", "Terraform", "CI/CD",
        "GitHub Actions", "Prometheus", "Grafana",
        "SwiftUI", "Jetpack Compose", "React Native", "Flutter",
    };

    public static readonly IReadOnlyList<ResumeFixture> All =
    [
        // 1 — clean, well-structured markdown CV; everything catalog-known.
        new("clean-markdown", "clean markdown CV",
            """
            # Torvald Emberwright
            Senior .NET Engineer — Riga | torvald.emberwright@postbox.example | +371 2000 1111

            ## Summary
            Backend engineer with 11 years building transactional systems on .NET. Comfortable owning
            services end to end, from EF Core data models to Kubernetes deploys.

            ## Skills
            C# (expert), ASP.NET Core, Entity Framework Core, PostgreSQL, Redis, Docker, Kubernetes, CI/CD

            ## Experience
            **Lead Backend Engineer — Brasswater Ledger** (Mar 2019 – present)
            - Cut settlement batch runtime from 4h to 35min by reworking EF Core change tracking.
            - Introduced outbox-based messaging that removed 100% of dual-write incidents.

            **Backend Engineer — Quillstone Systems** (Jun 2014 – Feb 2019)
            - Shipped a PostgreSQL sharding layer serving 40k requests/minute.

            ## Education
            BSc Computer Science, University of Latvia, 2010–2014

            ## Languages
            Latvian (native), English (fluent), Russian (professional)
            """,
            new GroundTruth("Torvald", "Emberwright", "torvald.emberwright@postbox.example", "Senior .NET Engineer",
                [new("C#", true), new("ASP.NET Core", true), new("Entity Framework Core", true), new("PostgreSQL", true), new("Redis", true), new("Docker", true), new("Kubernetes", true), new("CI/CD", true)],
                ["Latvian", "English", "Russian"],
                ["BSc Computer Science"],
                [new("Brasswater Ledger", "Lead Backend Engineer", "2019-03", null, 2),
                 new("Quillstone Systems", "Backend Engineer", "2014-06", "2019-02", 1)])),

        // 2 — LinkedIn-style dump: headline, About, informal experience blurbs.
        new("linkedin-dump", "LinkedIn profile dump",
            """
            Sable Quickwater
            Frontend Developer | React | TypeScript enthusiast
            Vilnius, Lithuania · sable.qw@mailhouse.example

            About
            I love building accessible interfaces. 6 years of React, strong TypeScript, recently digging
            into Next.js server components. I mentor juniors and run our design-system guild.

            Experience
            Frontend Developer · Glasswing Commerce
            Jan 2021 - Present · Vilnius
            Design system, checkout rewrite in Next.js, Core Web Vitals push (LCP 4.1s -> 1.8s).

            Junior Web Developer · Peatlight Agency
            Aug 2018 - Dec 2020
            Marketing sites, learned React and accessibility basics, WCAG audits for two banking clients.

            Education
            Vilnius University, Bachelor's in Information Systems (2014-2018)

            Languages: Lithuanian (native), English (fluent)
            """,
            new GroundTruth("Sable", "Quickwater", "sable.qw@mailhouse.example", "Frontend Developer",
                [new("React", true), new("TypeScript", true), new("Next.js", true), new("Accessibility (WCAG)", true)],
                ["Lithuanian", "English"],
                ["Bachelor's in Information Systems"],
                [new("Glasswing Commerce", "Frontend Developer", "2021-01", null, 3),
                 new("Peatlight Agency", "Junior Web Developer", "2018-08", "2020-12", 3)])),

        // 3 — terse plain text, minimal structure.
        new("terse-plain", "terse plain text",
            """
            Rooke Fennelbard, data engineer, Warsaw. rooke.f@courier.example
            Python, SQL, Apache Airflow, Apache Spark, AWS. Some Terraform.
            2020- now: Data Engineer, Cinderfall Analytics. Built ELT for 30+ sources, cut warehouse cost 45%.
            2017-2020: Analyst, Millbrook Retail. Dashboards, forecasting.
            MSc Applied Mathematics, Warsaw University of Technology, 2017.
            Polish native, English professional.
            """,
            new GroundTruth("Rooke", "Fennelbard", "rooke.f@courier.example", "Data Engineer",
                [new("Python", true), new("SQL", true), new("Apache Airflow", true), new("Apache Spark", true), new("AWS", true), new("Terraform", true)],
                ["Polish", "English"],
                ["MSc Applied Mathematics"],
                [new("Cinderfall Analytics", "Data Engineer", "2020", null, 2),
                 new("Millbrook Retail", "Analyst", "2017", "2020", 1)])),

        // 4 — messy formatting: ALLCAPS headers, mixed bullets, inconsistent date styles.
        new("messy-formatting", "messy formatting",
            """
            ==== WREN ASHGROVE ====
            devops / platform / sre ....... wren.ashgrove@relay.example ....... Tallinn

            SKILLZ::  Kubernetes ; Docker ; Terraform ; AWS ; Prometheus + Grafana ; GitHub Actions ; golang(Go)

            WORK
            *** Skyshard Hosting *** PLATFORM ENGINEER *** since Nov. 2022
              -> autoscaling rebuild, saved ~30% infra spend
              -> on-call revamp, MTTR 90min => 25min

            *** Ferrous Pike Ltd *** SYSADMIN *** 05/2018 -- 10/2022
              -> ran 200+ VMs, migrated everything to containers

            CERTS: CKA (Certified Kubernetes Administrator), 2023. AWS Solutions Architect Associate 2021.
            speaks: estonian - native / english - fluent
            """,
            new GroundTruth("Wren", "Ashgrove", "wren.ashgrove@relay.example", "Platform Engineer",
                [new("Kubernetes", true), new("Docker", true), new("Terraform", true), new("AWS", true), new("Prometheus", true), new("Grafana", true), new("GitHub Actions", true), new("Go", true)],
                ["Estonian", "English"],
                ["CKA (Certified Kubernetes Administrator)", "AWS Solutions Architect Associate"],
                [new("Skyshard Hosting", "Platform Engineer", "2022-11", null, 2),
                 new("Ferrous Pike Ltd", "Sysadmin", "2018-05", "2022-10", 1)])),

        // 5 — career changer with a gap; earlier career off-domain.
        new("career-changer", "career changer with gap",
            """
            Marisol Tarnwick
            Data Analyst (career changer) — Kraków — marisol.tarnwick@inbox.example

            After eight years teaching high-school mathematics I retrained as a data analyst
            (career break 2021–2022 for the retraining bootcamp and family reasons).

            Experience
            Data Analyst, Bryerfield Logistics — since March 2023
            • Built Python forecasting notebooks that reduced stockouts by 18%
            • Own the weekly SQL reporting pack for three warehouses

            Mathematics Teacher, Liceum nr 7 — September 2013 to June 2021
            • Taught statistics and calculus; ran the school data club

            Skills: Python, SQL, Feature Engineering, Excel
            Education: MA Mathematics Education, Jagiellonian University, 2013
            Languages: Polish (native), English (professional), Spanish (conversational)
            """,
            new GroundTruth("Marisol", "Tarnwick", "marisol.tarnwick@inbox.example", "Data Analyst",
                [new("Python", true), new("SQL", true), new("Feature Engineering", true), new("Excel", false)],
                ["Polish", "English", "Spanish"],
                ["MA Mathematics Education"],
                [new("Bryerfield Logistics", "Data Analyst", "2023-03", null, 2),
                 new("Liceum nr 7", "Mathematics Teacher", "2013-09", "2021-06", 1)])),

        // 6 — NO EMAIL. Validator requires one; the honest outcome is a core abort, not an invented address.
        new("no-email", "missing email (validator tension)",
            """
            Corbin Nightriver — Mobile Developer, Prague (contact via agency)

            Six years shipping iOS and Android apps.

            Skills: Swift, SwiftUI, Kotlin, Jetpack Compose

            Experience:
            Mobile Developer at Lanternfox Apps, 2021 to present.
            Delivered a banking app rated 4.7 stars; drove crash-free sessions to 99.8%.

            Junior iOS Developer at Bellwether Studio, 2019-2021.
            Two shipped titles.

            Education: BSc Software Engineering, Czech Technical University, 2019.
            Languages: Czech native, English fluent.
            """,
            new GroundTruth("Corbin", "Nightriver", null, "Mobile Developer",
                [new("Swift", true), new("SwiftUI", true), new("Kotlin", true), new("Jetpack Compose", true)],
                ["Czech", "English"],
                ["BSc Software Engineering"],
                [new("Lanternfox Apps", "Mobile Developer", "2021", null, 2),
                 new("Bellwether Studio", "Junior iOS Developer", "2019", "2021", 1)])),

        // 7 — mostly non-catalog skills: proposal path.
        new("noncatalog-skills", "non-catalog skills (proposal path)",
            """
            Hesper Coalbrook
            Industrial Automation Engineer — Ostrava — hesper.coalbrook@works.example

            15 years automating factory lines.

            Core skills: LabVIEW, COBOL (legacy line controllers), Ladder Logic, SCADA systems,
            OPC UA, plus scripting glue in Python.

            Experience
            Automation Engineer, Ironvale Manufacturing, 2015–present.
            – Retrofitted 12 production lines with SCADA monitoring; downtime fell 22%.
            Controls Technician, Deepfurnace Steel, 2010–2015.
            – Maintained PLC cabinets across two plants.

            Certification: Siemens TIA Portal Programmer, 2016.
            Languages: Czech (native), English (conversational), German (conversational).
            """,
            new GroundTruth("Hesper", "Coalbrook", "hesper.coalbrook@works.example", "Industrial Automation Engineer",
                [new("LabVIEW", false), new("COBOL", false), new("Ladder Logic", false), new("SCADA systems", false), new("OPC UA", false), new("Python", true)],
                ["Czech", "English", "German"],
                ["Siemens TIA Portal Programmer"],
                [new("Ironvale Manufacturing", "Automation Engineer", "2015", null, 1),
                 new("Deepfurnace Steel", "Controls Technician", "2010", "2015", 1)])),

        // 8 — date traps: seasons, overlaps, single-year ranges.
        new("date-traps", "ambiguous/overlapping dates",
            """
            Ivo Greyhalloway | full-stack developer | Budapest | ivo.greyhalloway@post.example

            Skills: JavaScript, TypeScript, React, MongoDB, GraphQL

            Work history:
            Freelance Full-Stack Developer — Summer 2019 to the end of 2021 (overlapped with the next role
            for a few months while handing projects over).
            Built storefronts for ~15 clients.

            Full-Stack Developer, Copperbeam Media — from October 2021 until 2021 year-end contract wrap,
            then extended permanently through today.
            Leads the subscription platform team since 2023.

            Education: self-taught; freeCodeCamp full-stack certification (2018).
            Languages: Hungarian (native), English (fluent).
            """,
            new GroundTruth("Ivo", "Greyhalloway", "ivo.greyhalloway@post.example", "Full-Stack Developer",
                [new("JavaScript", true), new("TypeScript", true), new("React", true), new("MongoDB", true), new("GraphQL", true)],
                ["Hungarian", "English"],
                ["freeCodeCamp full-stack certification"],
                [new("Freelance", "Full-Stack Developer", "2019", "2021", 1),
                 new("Copperbeam Media", "Full-Stack Developer", "2021-10", null, 2)])),
    ];
}
