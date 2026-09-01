namespace ExpertToJob.ToolSelectionEval;

/// <summary>One labeled prompt: the tool a well-described toolset should make the model call
/// FIRST, plus (sparingly) alternatives that are genuinely also-correct — first-tool credit is
/// given for either; anything else is a miss.</summary>
public sealed record GoldenPrompt(
    string Id,
    string Cluster,
    string Text,
    string ExpectedTool,
    string[]? AlsoAcceptable = null);

/// <summary>
/// The frozen tool-selection golden set (P1T-127): prompts spanning the confusable clusters the
/// P1T-112 audit named. Frozen — only move on a deliberate re-label; the whole point is comparing
/// the same prompts before and after the description pass.
/// </summary>
public static class GoldenPromptSet
{
    public const string Capability = "capability";
    public const string Shortlist = "shortlist";
    public const string Style = "style";
    public const string ExactFact = "exact-fact";
    public const string BulkSweep = "bulk-sweep";
    public const string Catalog = "catalog";
    public const string Writes = "writes";

    public static IReadOnlyList<GoldenPrompt> Load() =>
    [
        // ---- Capability questions: meaning over the narratives → roster_semantic_search ------
        new("cap-payments", Capability,
            "Who has built real-time payments systems?", "roster_semantic_search"),
        new("cap-kafka-lead", Capability,
            "Find people with fintech experience who have also led a team.", "roster_semantic_search"),
        new("cap-migrations", Capability,
            "Anyone who has done large cloud migrations?", "roster_semantic_search"),
        new("cap-embedded", Capability,
            "Which of our people have worked close to hardware or firmware?", "roster_semantic_search"),
        new("cap-ml", Capability,
            "Who has shipped machine learning models to production?", "roster_semantic_search"),
        new("cap-incident", Capability,
            "I need someone who has handled major production incidents before.", "roster_semantic_search"),

        // ---- Multi-requirement JDs → roster_shortlist_search ----------------------------------
        new("sl-jd-backend", Shortlist,
            "Shortlist candidates for this role: senior backend engineer, Kafka event streaming, " +
            "Kubernetes, and team leadership.", "roster_shortlist_search"),
        new("sl-jd-frontend", Shortlist,
            "Rank our best matches against these requirements: React, TypeScript, accessibility, " +
            "and design-system experience.", "roster_shortlist_search"),
        new("sl-coverage", Shortlist,
            "Which candidates cover the most of: Python, Airflow, AWS, and data modeling?",
            "roster_shortlist_search"),
        new("sl-jd-paste", Shortlist,
            "Here is a job description with several must-haves — find the top candidates with " +
            "per-requirement evidence.", "roster_shortlist_search"),

        // ---- Phrasing exemplars → style_exemplar_search ----------------------------------------
        new("style-bullet", Style,
            "Show me examples of strongly phrased achievement bullets about cost reduction from " +
            "other CVs.", "style_exemplar_search"),
        new("style-rewrite", Style,
            "I want well-written phrasing samples for describing platform migration work.",
            "style_exemplar_search"),
        new("style-metrics", Style,
            "Find exemplar bullets that quantify impact with concrete metrics.", "style_exemplar_search"),

        // ---- Exact facts: structured reads, not meaning ----------------------------------------
        new("fact-list-emails", ExactFact,
            "List all employees with their titles and emails.", "employee_list"),
        new("fact-count", ExactFact,
            "How many employees do we have right now?", "employee_list",
            AlsoAcceptable: ["roster_digest_list"]),
        new("fact-locations", ExactFact,
            "Show me every employee and their location.", "employee_list"),
        new("fact-one-employee", ExactFact,
            "Show me everything we have on employee 7b2e8d3a-1111-2222-3333-444455556666, " +
            "including languages and availability.", "employee_get"),
        new("fact-availability", ExactFact,
            "What are the availability entries for employee 7b2e8d3a-1111-2222-3333-444455556666?",
            "employee_get", AlsoAcceptable: ["availability_list"]),
        new("fact-cv", ExactFact,
            "Give me the assembled CV for employee 7b2e8d3a-1111-2222-3333-444455556666.", "cv_get"),
        new("fact-cv-render", ExactFact,
            "I need the full CV content of one specific person to review before a client meeting; " +
            "their id is 7b2e8d3a-1111-2222-3333-444455556666.", "cv_get"),

        // ---- Bulk sweeps → roster_digest_list ---------------------------------------------------
        new("sweep-page", BulkSweep,
            "Page through career digests of the whole roster so I can bulk-assess everyone.",
            "roster_digest_list"),
        new("sweep-score-all", BulkSweep,
            "I want to score every single employee against one job description — fetch the roster " +
            "in compact digest form, first page.", "roster_digest_list"),
        new("sweep-second-page", BulkSweep,
            "Get me page 2 of the roster digests, 50 per page.", "roster_digest_list"),

        // ---- Catalog reads ----------------------------------------------------------------------
        new("cat-list", Catalog,
            "What skill categories exist?", "category_list", AlsoAcceptable: ["category_tree"]),
        new("cat-tree", Catalog,
            "Show the skill category hierarchy with children nested under parents.", "category_tree"),
        new("cat-skills", Catalog,
            "List all skills in the catalog.", "skill_list"),
        new("cat-skills-under", Catalog,
            "Which skills do we track under the Frontend category?", "skill_list",
            AlsoAcceptable: ["category_tree"]),

        // ---- Writes: the classic person-vs-catalog and create-vs-draft traps -------------------
        new("write-skill-to-person", Writes,
            "Add the existing catalog skill 8a8a8a8a-1111-2222-3333-444455556666 to employee " +
            "7b2e8d3a-1111-2222-3333-444455556666 at Advanced level.", "employee_skill_add"),
        new("write-new-catalog-skill", Writes,
            "We don't track 'Rust' yet — add it to the skill catalog under category " +
            "9c9c9c9c-1111-2222-3333-444455556666.", "skill_create"),
        new("write-skill-trap-person", Writes,
            "Mark that employee 7b2e8d3a-1111-2222-3333-444455556666 knows PostgreSQL " +
            "(catalog skill 8a8a8a8a-1111-2222-3333-444455556666), 5 years of it.", "employee_skill_add"),
        new("write-skill-trap-catalog", Writes,
            "Create a brand-new skill entry called 'Zig' so we can start tagging people with it.",
            "skill_create",
            // categoryId is a required argument the prompt never supplies (P1T-137): a
            // prerequisite read is the legally correct first call, not a miss.
            AlsoAcceptable: ["category_list", "category_tree", "skill_list"]),
        new("write-new-employee", Writes,
            "Add a new employee: Jane Doe, Senior Engineer, jane@example.com, Berlin.", "employee_create"),
        new("write-draft", Writes,
            "Stage this pasted resume as a draft employee for later human review.", "employee_create_draft"),
        new("write-language", Writes,
            "Record that employee 7b2e8d3a-1111-2222-3333-444455556666 speaks German at " +
            "Professional level.", "language_add"),
        new("write-availability", Writes,
            "Set employee 7b2e8d3a-1111-2222-3333-444455556666 to 50% capacity from 2026-10-01.",
            "availability_add"),
        new("write-experience", Writes,
            "Add a work experience to employee 7b2e8d3a-1111-2222-3333-444455556666: Platform " +
            "Lead at FlowWorks since March 2019.", "experience_add"),
        new("write-achievement", Writes,
            "Append an achievement bullet 'Cut deploy time by 40%' to experience " +
            "5d5d5d5d-1111-2222-3333-444455556666.", "achievement_add"),
        new("write-qualification", Writes,
            "Record a certification for employee 7b2e8d3a-1111-2222-3333-444455556666: AWS " +
            "Solutions Architect, issued by Amazon.", "qualification_add"),
        new("write-update-title", Writes,
            "Change the title of employee 7b2e8d3a-1111-2222-3333-444455556666 to Staff Engineer.",
            "employee_update"),
    ];
}
