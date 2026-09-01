using System.ComponentModel;
using ExpertToJob.Application.Cv;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace ExpertToJob.Mcp.Tools;

[McpServerToolType]
public class CvTools
{
    [McpServerTool(Name = "cv_get", ReadOnly = true, Destructive = false),
     Description(
         "Assemble and return ONE employee's full CV by id as structured sections — header and " +
         "contact, professional summary, availability, skills grouped by category, languages, " +
         "experiences with their achievement bullets (each bullet carrying its achievementId), " +
         "education and certifications. Use it when the CV itself is the answer: reviewing or " +
         "rendering one person's CV, or collecting the verbatim evidence a tailoring, match or " +
         "interview step must quote — its achievementIds are the input to style_exemplar_search. " +
         "Do NOT use it to find or rank people — roster_semantic_search for capability questions, " +
         "roster_shortlist_search for a job description's requirements; do NOT use it to sweep " +
         "the roster — roster_digest_list pages compact digests, one page per call; prefer " +
         "employee_get when you need raw child records and their ids rather than CV sections. " +
         "Input: employeeId — the employee GUID; e.g. " +
         "{\"employeeId\": \"7b2e8d3a-1111-2222-3333-444455556666\"}. Returns DATA, not a PDF " +
         "or HTML, and no relevance scores, ranking or match commentary."),
     Authorize(Policy = McpScopes.Read)]
    public static Task<object> Get(
        ICvService cv,
        [Description("Employee id (GUID).")] Guid employeeId,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => cv.BuildAsync(employeeId, ct));
}
