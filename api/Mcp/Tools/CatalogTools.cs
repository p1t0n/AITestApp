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
     Description("List all skill catalog categories (flat)."),
     Authorize(Policy = McpScopes.Read)]
    public static async Task<IReadOnlyList<CategoryDto>> ListCategories(
        ISkillCatalogService catalog, CancellationToken ct)
        => await catalog.ListCategoriesAsync(ct);

    [McpServerTool(Name = "category_tree", ReadOnly = true, Destructive = false),
     Description("Get the skill catalog category hierarchy as a tree."),
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
     Description("List all catalog skills."),
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
