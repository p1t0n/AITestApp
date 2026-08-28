using System.ComponentModel;
using CvManager.Application.Skills;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace CvManager.Mcp.Tools;

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
     Description("Create a category, optionally under a parent. Name must be unique within its parent."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> CreateCategory(
        ISkillCatalogService catalog, SaveCategoryDto dto, CancellationToken ct)
        => McpToolExecutor.RunAsync(() => catalog.CreateCategoryAsync(dto, ct));

    [McpServerTool(Name = "category_update", ReadOnly = false, Destructive = false),
     Description("Update a category by id (rename or re-parent). Cannot create a cycle."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> UpdateCategory(
        ISkillCatalogService catalog,
        [Description("Category id (GUID).")] Guid id,
        SaveCategoryDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => catalog.UpdateCategoryAsync(id, dto, ct));

    [McpServerTool(Name = "category_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description("Delete a category by id."),
     Authorize(Policy = McpScopes.Admin)]
    public static Task<object> DeleteCategory(
        ISkillCatalogService catalog,
        [Description("Category id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => catalog.DeleteCategoryAsync(id, ct));

    // ---- Skills ----

    [McpServerTool(Name = "skill_list", ReadOnly = true, Destructive = false),
     Description(
         "List every catalog skill flat across all categories — id, name, categoryId, " +
         "categoryName and rank. Use it to look up a skill id (e.g. before employee_skill_add) or " +
         "to inventory what the catalog tracks ('list all skills'). Do NOT use it for the " +
         "category hierarchy — category_tree nests skills under their category, category_list " +
         "gives flat categories; do NOT use it for one person's skills, levels or years — " +
         "employee_get; do NOT use it to add anything — skill_create adds a NEW skill to the " +
         "catalog, employee_skill_add attaches an EXISTING catalog skill to an employee. Input: " +
         "none; e.g. {}. Returns the catalog only: no employee data, no proficiency levels."),
     Authorize(Policy = McpScopes.Read)]
    public static async Task<IReadOnlyList<SkillDto>> ListSkills(
        ISkillCatalogService catalog, CancellationToken ct)
        => await catalog.ListSkillsAsync(ct);

    [McpServerTool(Name = "skill_create", ReadOnly = false, Destructive = false),
     Description("Create a catalog skill under a category. Name must be unique within the category."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> CreateSkill(
        ISkillCatalogService catalog, SaveSkillDto dto, CancellationToken ct)
        => McpToolExecutor.RunAsync(() => catalog.CreateSkillAsync(dto, ct));

    [McpServerTool(Name = "skill_update", ReadOnly = false, Destructive = false),
     Description("Update a catalog skill by id (rename or move to another category)."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> UpdateSkill(
        ISkillCatalogService catalog,
        [Description("Skill id (GUID).")] Guid id,
        SaveSkillDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => catalog.UpdateSkillAsync(id, dto, ct));

    [McpServerTool(Name = "skill_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description("Delete a catalog skill by id."),
     Authorize(Policy = McpScopes.Admin)]
    public static Task<object> DeleteSkill(
        ISkillCatalogService catalog,
        [Description("Skill id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => catalog.DeleteSkillAsync(id, ct));
}
