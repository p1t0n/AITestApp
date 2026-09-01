using System.ComponentModel;
using ExpertToJob.Application.Experts;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace ExpertToJob.Mcp.Tools;

[McpServerToolType]
public class QualificationTools
{
    [McpServerTool(Name = "qualification_add", ReadOnly = false, Destructive = false),
     Description(
         "Add ONE formal qualification to a person — either a DEGREE (institution, field, study " +
         "dates) or a CERTIFICATION (issuer, credential id, issue/expiry dates); the type field " +
         "picks which. Use it for 'record her AWS Solutions Architect certification', 'add his BSc " +
         "in Computer Science'. Do NOT use it for a skill — a certification is not a catalog skill: " +
         "expert_skill_add records proficiency, this records the credential; do NOT use it for a " +
         "job — experience_add; do NOT use it to amend a qualification the person already has — " +
         "qualification_update by its id. Input: expertId + dto {type, name, institution?, field?, " +
         "startDate?, endDate?, issuer?, credentialId?, issueDate?, expiryDate?} with dates as " +
         "yyyy-MM-dd; e.g. {\"expertId\": \"7b2e8d3a-1111-2222-3333-444455556666\", " +
         "\"dto\": {\"type\": \"Certification\", \"name\": \"AWS Solutions Architect\", " +
         "\"issuer\": \"Amazon\"}}. Leave fields the source does not state as null — never " +
         "invent an issuer, credential id or date. Returns the created qualification."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Add(
        IQualificationService qualifications,
        [Description("Expert id (GUID) holding the qualification.")] Guid expertId,
        [Description("type: Degree or Certification; name required. Degrees use institution / " +
                     "field / startDate / endDate; certifications use issuer / credentialId / " +
                     "issueDate / expiryDate. All dates yyyy-MM-dd; unknown fields stay null.")]
        SaveQualificationDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => qualifications.AddAsync(expertId, dto, ct));

    [McpServerTool(Name = "qualification_update", ReadOnly = false, Destructive = false),
     Description(
         "CHANGE one existing qualification by QUALIFICATION id (from expert_get or cv_get) — its " +
         "name, type, institution/issuer or dates. Use it for 'that certification expires in " +
         "2027', 'fix the institution'. This is a full replace: send every field that should " +
         "survive. Do NOT " +
         "use it to add a second qualification — qualification_add with the expert id. Input: id " +
         "+ dto (same shape as qualification_add); e.g. {\"id\": " +
         "\"7a7a7a7a-1111-2222-3333-444455556666\", \"dto\": {\"type\": " +
         "\"Certification\", \"name\": \"AWS Solutions Architect\", \"issuer\": " +
         "\"Amazon\", \"expiryDate\": \"2027-05-01\"}}. Returns the updated qualification."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Update(
        IQualificationService qualifications,
        [Description("Qualification id (GUID) from expert_get / cv_get — not the expert id.")]
        Guid id,
        [Description("The qualification AFTER the edit — full replace, dates yyyy-MM-dd.")]
        SaveQualificationDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => qualifications.UpdateAsync(id, dto, ct));

    [McpServerTool(Name = "qualification_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description(
         "DESTRUCTIVE: delete one degree or certification by qualification id (from expert_get). " +
         "Do NOT use it for an expired certification that is still true history — " +
         "qualification_update carries an expiryDate for that. Input: id; e.g. {\"id\": " +
         "\"7a7a7a7a-1111-2222-3333-444455556666\"}. Requires the admin scope; idempotent. " +
         "Returns no data."),
     Authorize(Policy = McpScopes.Admin)]
    public static Task<object> Delete(
        IQualificationService qualifications,
        [Description("Qualification id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => qualifications.DeleteAsync(id, ct));
}
