using System.ComponentModel;
using ExpertToJob.Application.Skills;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace ExpertToJob.Mcp.Tools;

[McpServerToolType]
public class CatalogTools
{
    // ---- Categories ----

    [McpServerTool(Name = "category_list", ReadOnly = true, Destructive = false),
     Description(
         "List every skill-catalog category as a flat row — id, name and parentId (null for a " +
         "root). Use it when you need category ids or a plain inventory of category names ('what " +
         "skill categories exist'). Do NOT use it when NESTING matters (which categories sit " +
         "under which, or which skills live in a category) — category_tree returns the hierarchy " +
         "with each node's skills; do NOT use it to list skills — skill_list returns every " +
         "catalog skill; do NOT use it for one person's skills — employee_get returns those with " +
         "level and years. Input: none; e.g. {}. Returns categories only: no skills, no counts, " +
         "no employee data."),
     Authorize(Policy = McpScopes.Read)]
    public static async Task<IReadOnlyList<CategoryDto>> ListCategories(
        ISkillCatalogService catalog, CancellationToken ct)
        => await catalog.ListCategoriesAsync(ct);

    [McpServerTool(Name = "category_tree", ReadOnly = true, Destructive = false),
     Description(
         "Get the skill catalog as a TREE — root categories with their children nested, and each " +
         "node's catalog skills attached. Use it when the shape matters: showing the hierarchy " +
         "('categories with children under parents') or answering 'which skills do we track " +
         "under <category>' in one call. Do NOT use it when a flat list of category ids and names " +
         "is enough — category_list is cheaper; do NOT use it for every skill regardless of " +
         "category — skill_list; do NOT use it for one person's skills — employee_get. Input: " +
         "none; e.g. {}. Returns catalog structure only: no employee data, no proficiency levels " +
         "or years."),
     Authorize(Policy = McpScopes.Read)]
    public static async Task<IReadOnlyList<CategoryNodeDto>> CategoryTree(
        ISkillCatalogService catalog, CancellationToken ct)
        => await catalog.GetTreeAsync(ct);

    [McpServerTool(Name = "category_create", ReadOnly = false, Destructive = false),
     Description(
         "Create a skill-catalog CATEGORY — a grouping that holds skills — optionally nested under " +
         "a parent category. Use it when a new area of the catalog is needed ('add a Data " +
         "Engineering category under Engineering'). Do NOT use it to create a SKILL — skill_create " +
         "adds the skill itself and needs an existing categoryId; do NOT use it for anything about " +
         "a person. Input: dto {name, parentId?} — omit or null parentId for a root category; e.g. " +
         "{\"dto\": {\"name\": \"Data Engineering\", \"parentId\": " +
         "\"9c9c9c9c-1111-2222-3333-444455556666\"}}. The name must be unique within its parent " +
         "— a repeat returns conflict. Returns the new category, which contains no skills yet."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> CreateCategory(
        ISkillCatalogService catalog,
        [Description("name: the category name; parentId: an existing category id (GUID) to nest " +
                     "under, or null/omitted for a root category.")]
        SaveCategoryDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => catalog.CreateCategoryAsync(dto, ct));

    [McpServerTool(Name = "category_update", ReadOnly = false, Destructive = false),
     Description(
         "Rename a catalog CATEGORY or re-parent it (move a subtree), by category id. Use it to fix " +
         "the catalog's shape; the skills inside move with it. Do NOT use it to move a SKILL " +
         "between categories — skill_update does that one skill; do NOT use it to add or delete " +
         "anything. Input: id + dto {name, parentId?} (full replace); e.g. {\"id\": " +
         "\"9c9c9c9c-1111-2222-3333-444455556666\", \"dto\": {\"name\": \"Platform\", " +
         "\"parentId\": null}}. A parentId that would make a cycle (its own descendant) is " +
         "rejected as a validation error. Returns the updated category."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> UpdateCategory(
        ISkillCatalogService catalog,
        [Description("Category id (GUID).")] Guid id,
        [Description("name and parentId AFTER the edit; null parentId makes it a root. A parentId " +
                     "inside its own subtree is rejected.")]
        SaveCategoryDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => catalog.UpdateCategoryAsync(id, dto, ct));

    [McpServerTool(Name = "category_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description(
         "DESTRUCTIVE: delete a catalog CATEGORY by id. Use it only to prune an empty or mistaken " +
         "grouping on a human's instruction — check what it holds with category_tree first, since " +
         "the catalog vocabulary is shared by every employee. Do NOT use it to remove one skill — " +
         "skill_delete (catalog) or employee_skill_delete (one person); do NOT use it to rename — " +
         "category_update. Input: id; e.g. {\"id\": " +
         "\"9c9c9c9c-1111-2222-3333-444455556666\"}. Requires the admin scope; idempotent. " +
         "Returns no data."),
     Authorize(Policy = McpScopes.Admin)]
    public static Task<object> DeleteCategory(
        ISkillCatalogService catalog,
        [Description("Category id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => catalog.DeleteCategoryAsync(id, ct));

    // ---- Skills ----

    [McpServerTool(Name = "skill_list", ReadOnly = true, Destructive = false),
     Description(
         "List catalog skills flat — id, name, categoryId, categoryName, rank. Pass nameContains " +
         "to resolve ONE skill id (before employee_skill_add, or to get a skillId to filter a " +
         "search by); unfiltered it inventories the catalog, one page per call. Do NOT use it for " +
         "the category hierarchy — category_tree nests skills under their category, category_list " +
         "gives flat categories; do NOT use it for one person's skills, levels or years — " +
         "employee_get; do NOT use it to add anything — skill_create adds a NEW catalog skill, " +
         "employee_skill_add attaches an EXISTING one to an employee. Input, all optional: " +
         "nameContains, page, pageSize; e.g. {\"nameContains\": \"react\"} for a lookup, e.g. {} " +
         "for the catalog. Carries total, so no match reads differently from a page past the end. " +
         "Returns the catalog only: no employee data, no proficiency levels."),
     Authorize(Policy = McpScopes.Read)]
    public static Task<object> ListSkills(
        ISkillCatalogService catalog,
        CancellationToken ct,
        [Description("Case-insensitive substring of the skill name; omit for the whole catalog.")]
        string? nameContains = null,
        [Description("1-based page number (default 1).")]
        int? page = null,
        [Description("Skills per page (default 100, max 200).")]
        int? pageSize = null)
        => McpToolExecutor.RunAsync(
            () => catalog.SearchSkillsAsync(new SkillQuery(nameContains, page, pageSize), ct));

    [McpServerTool(Name = "skill_create", ReadOnly = false, Destructive = false),
     Description(
         "Add a NEW SKILL TO THE CATALOG — the shared vocabulary every employee is tagged from — " +
         "under an existing category. Use it only when the catalog genuinely lacks the skill: 'we " +
         "do not track Rust yet, add it', 'create a brand-new skill entry called Zig so we can " +
         "start tagging people'. It touches NO employee. Do NOT use it to say a PERSON has a skill " +
         "— employee_skill_add attaches an existing catalog skill to someone, and if the skill " +
         "already exists (check skill_list first) that is the only call needed; do NOT use it to " +
         "create a CATEGORY — category_create does that, and this tool needs an existing " +
         "categoryId. Input: dto {name, categoryId}; e.g. {\"dto\": {\"name\": \"Rust\", " +
         "\"categoryId\": \"9c9c9c9c-1111-2222-3333-444455556666\"}}. The name must be unique " +
         "within that category — a repeat returns conflict, an unknown categoryId not_found. " +
         "Returns the new catalog skill; nobody has it yet."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> CreateSkill(
        ISkillCatalogService catalog,
        [Description("name: the skill as the catalog should list it; categoryId: an EXISTING " +
                     "category id (GUID) from category_list or category_tree.")]
        SaveSkillDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => catalog.CreateSkillAsync(dto, ct));

    [McpServerTool(Name = "skill_update", ReadOnly = false, Destructive = false),
     Description(
         "Rename a CATALOG skill or move it to another category, by catalog skill id. The change " +
         "applies to the shared vocabulary, so it shows up for every employee tagged with that " +
         "skill. Use it to fix a catalog typo or re-file a skill. Do NOT use it to change one " +
         "person's level or years — employee_skill_update edits that person's row; do NOT use it " +
         "to add a skill — skill_create (catalog) or employee_skill_add (a person). Input: id + " +
         "dto {name, categoryId} (full replace); e.g. {\"id\": " +
         "\"8a8a8a8a-1111-2222-3333-444455556666\", \"dto\": {\"name\": \"Rust\", " +
         "\"categoryId\": \"9c9c9c9c-1111-2222-3333-444455556666\"}}. Returns the updated " +
         "catalog skill; no employee data."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> UpdateSkill(
        ISkillCatalogService catalog,
        [Description("Catalog skill id (GUID) from skill_list — not an employee-skill row id.")]
        Guid id,
        [Description("name and categoryId AFTER the edit; both are replaced.")]
        SaveSkillDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => catalog.UpdateSkillAsync(id, dto, ct));

    [McpServerTool(Name = "skill_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description(
         "DESTRUCTIVE: remove a skill from the CATALOG entirely, for everyone. Use it only to prune " +
         "a mistaken or duplicate catalog entry on a human's instruction. Do NOT use it to take a " +
         "skill off ONE person — employee_skill_delete removes their row and leaves the catalog " +
         "alone (this is the usual intent); do NOT use it to rename — skill_update. Input: id (the " +
         "catalog skill GUID); e.g. {\"id\": \"8a8a8a8a-1111-2222-3333-444455556666\"}. " +
         "Requires the admin scope; idempotent. Returns no data."),
     Authorize(Policy = McpScopes.Admin)]
    public static Task<object> DeleteSkill(
        ISkillCatalogService catalog,
        [Description("Skill id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => catalog.DeleteSkillAsync(id, ct));
}
